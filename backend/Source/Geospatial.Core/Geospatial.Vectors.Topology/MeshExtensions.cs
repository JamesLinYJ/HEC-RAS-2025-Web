using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.Vectors.MeshTopology;
using Microsoft.Extensions.Logging;
using Utility.Extensions;
using Utility.Logging;
using Utility.Memory;
using Utility.Progress;

namespace Geospatial.Vectors.Topology;

public static class MeshExtensions
{
	public static class MessageCodes
	{
		public static readonly MessageClass ErrorDiscretizingQuadPatch = new MessageClass("MeshGeneration_ErrorDiscretizingQuadPatch", MessageSeverity.Error, "Error discretizing quad patch for Region {ControlPointIndex}.");

		public static readonly MessageClass ErrorDistortedQuadPatch = new MessageClass("MeshGeneration_ErrorDistortedQuadPatch", MessageSeverity.Error, "Error generating quad patch for Region {ControlPointIndex}. Consider cell aspect ratio and number of triangles");

		public static readonly MessageClass QuadPatchRequires4Arcs = new MessageClass("MeshGeneration_QuadPatchRequiresFourArcs", MessageSeverity.Error, "Error resolving quad or 1D patch - Region {ControlPointIndex} (FID {RegionIndex}) requires at least 4 Arcs.");

		public static readonly MessageClass BadConstantSizeFunction = new MessageClass("MeshGeneration_BadConstantSizeFunction", MessageSeverity.Error, "Metadata Error - Region {ControlPointIndex} (FID {RegionIndex}) requires a constant-size value > 0 (Actual: {Size}).");

		public static readonly MessageClass ErrorNoRegions = new MessageClass("MeshGeneration_ErrorNoRegions", MessageSeverity.Error, "No non-empty regions defined");

		public static readonly MessageClass UnknownMeshingError = new MessageClass("MeshGeneration_UnknownMeshingError", MessageSeverity.Error, "Unknown meshing error in Region {ControlPointIndex} (FID {RegionIndex}).");

		public static readonly MessageClass UnknownMeshPostProcessingError = new MessageClass("MeshGeneration_UnknownMeshPostProcessingError", MessageSeverity.Error, "Unknown mesh post-processing error in Region {ControlPointIndex} (FID {RegionIndex}).");

		public static Message GetErrorDiscretizingQuadPatch(int ctrlPtIdx, int rgnIdx, string innerMsg)
		{
			return new Message(ErrorDiscretizingQuadPatch, "Error discretizing quad patch for Region {ControlPointIndex}. " + innerMsg, ctrlPtIdx);
		}

		public static Message GetErrorDistortedQuadPatch(int ctrlPtIdx, int rgnIdx, string innerMsg)
		{
			return new Message(ErrorDistortedQuadPatch, $"Error generating quad patch for Region {{{"ControlPointIndex"}}} (FID {"RegionIndex"}). \"{innerMsg}\". Consider cell aspect ratio and number of triangles", ctrlPtIdx);
		}

		public static Message GetQuadPatchRequires4Arcs(int ctrlPtIdx, int rgnIdx)
		{
			return QuadPatchRequires4Arcs.Create(ctrlPtIdx, rgnIdx);
		}

		public static Message GetBadConstantSizeFunction(int ctrlPtIdx, int rgnIdx, float cs)
		{
			return BadConstantSizeFunction.Create(ctrlPtIdx, rgnIdx, cs);
		}

		public static Message GetErrorNoRegionsToMesh()
		{
			return ErrorNoRegions.Create();
		}

		public static Message GetUnknownMeshingError(int ctrlPtIdx, int rgnIdx)
		{
			return UnknownMeshingError.Create(ctrlPtIdx, rgnIdx);
		}

		public static Message GetUnknownMeshPostProcessingError(int ctrlPtIdx, int rgnIdx)
		{
			return UnknownMeshPostProcessingError.Create(ctrlPtIdx, rgnIdx);
		}
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("MeshExtensions");

	public static readonly bool ContinueOnBadMeshgen = true;

	public const string PatchTypeColumnName = "PatchType";

	public static readonly TableDefault PatchTypeColumn = new TableDefault("PatchType", typeof(PatchType), PatchType.TrianglePaver);

	public const string SizeFunctionColumnName = "SizeFunction";

	public static readonly TableDefault ArcSizeFunctionColumn = new TableDefault("SizeFunction", typeof(LinearSizeFunction), LinearSizeFunction.Constant);

	public static readonly TableDefault ControlPointSizeFunctionColumn = new TableDefault("SizeFunction", typeof(SizeFunction), SizeFunction.Constant);

	public const string ConstantSizeColumnName = "ConstantSize";

	public static readonly TableDefault ConstantSizeColumn = new TableDefault("ConstantSize", typeof(float), ArcMetadata.DefaultConstantSize);

	public const string ComputedSizeColumnName = "COMPUTED_ConstantSize";

	public static readonly TableDefault ComputedSizeColumn = new TableDefault("COMPUTED_ConstantSize", typeof(float), 0f);

	public const string SecondaryConstantSizeColumnName = "SecondaryConstantSize";

	public static readonly TableDefault SecondaryConstantSizeColumn = new TableDefault("SecondaryConstantSize", typeof(float), MeshRegionMetadata.DefaultConstantSize);

	public const string ComputedSecondarySizeColumnName = "COMPUTED_SecondaryConstantSize";

	public static readonly TableDefault ComputedSecondarySizeColumn = new TableDefault("COMPUTED_SecondaryConstantSize", typeof(float), 0f);

	public const string RotationDegreesColumnName = "RotationDegrees";

	public static readonly TableDefault RotationDegreesColumn = new TableDefault("RotationDegrees", typeof(float), 0f);

	public const string RecipeColumnName = "Recipe";

	public static readonly TableDefault RecipeColumn = new TableDefault("Recipe", typeof(string), "Default()");

	public const string StretchRateColumnName = "StretchRate";

	public static readonly TableDefault StretchRateColumn = new TableDefault("StretchRate", typeof(float), float.NaN);

	public const string MultipointColumnName = "Multipoint";

	public static readonly TableDefault MultipointColumn = new TableDefault("Multipoint", typeof(float), 1f);

	public const string TinResolutionColumnName = "TINResolution";

	public static readonly TableDefault TinResolutionColumn = new TableDefault("TINResolution", typeof(float), 1f);

	public const string SizeToleranceColumnName = "SizeTolerance";

	public static readonly TableDefault SizeToleranceColumn = new TableDefault("SizeTolerance", typeof(float), 1f);

	public const string ConstrainStretchingColumnName = "ConstrainStretching";

	public static readonly TableDefault ConstrainStretchingColumn = new TableDefault("ConstrainStretching", typeof(ConstrainStretchingOptions), ConstrainStretchingOptions.Default);

	public const string LaplaceBCColumnName = "LaplaceBC";

	public static readonly TableDefault LaplaceBCColumn = new TableDefault("LaplaceBC", typeof(LaplaceBCOptions), LaplaceBCOptions.Default);

	public const string ComputedConstrainStretchingColumnName = "COMPUTED_ConstrainStretching";

	public static readonly TableDefault ComputedConstrainStretchingColumn = new TableDefault("COMPUTED_ConstrainStretching", typeof(ConstrainStretchingOptions), ConstrainStretchingOptions.Default);

	public const string CountColumnName = "Count";

	public static readonly TableDefault ArcPointCountColumn = new TableDefault("Count", typeof(int), 8);

	public const string BiasColumnName = "Bias";

	public static readonly TableDefault ArcBiasColumn = new TableDefault("Bias", typeof(float), 1f);

	public const string DistanceStraightLineColumnName = "DistanceStraightLine";

	public static readonly TableDefault DistanceStraightLineColumn = new TableDefault("DistanceStraightLine", typeof(bool), true);

	public const string AreaWeightColumnName = "AreaWeight";

	public static readonly TableDefault AreaWeightColumn = new TableDefault("AreaWeight", typeof(float), 0f);

	public const string DiscretizeMagnetismColumnName = "DiscretizeMagnetism";

	public static readonly TableDefault DiscretizeMagnetismColumn = new TableDefault("DiscretizeMagnetism", typeof(float), 0.1f);

	public const string FollowExactlyColumnName = "FollowExactly";

	public static readonly TableDefault FollowExactlyColumn = new TableDefault("FollowExactly", typeof(bool), true);

	public const string ArcAngleLeftColumnName = "ArcAngleLeft";

	public static readonly TableDefault ArcAngleLeftColumn = new TableDefault("ArcAngleLeft", typeof(float), 0f);

	public const string ArcAngleRightColumnName = "ArcAngleRight";

	public static readonly TableDefault ArcAngleRightColumn = new TableDefault("ArcAngleRight", typeof(float), 0f);

	public static readonly IReadOnlyList<TableDefault> ArcDefaults = new List<TableDefault>
	{
		ArcPointCountColumn, ArcBiasColumn, ArcSizeFunctionColumn, ConstantSizeColumn, ConstrainStretchingColumn, LaplaceBCColumn, DistanceStraightLineColumn, AreaWeightColumn, DiscretizeMagnetismColumn, FollowExactlyColumn,
		ArcAngleLeftColumn, ArcAngleRightColumn
	};

	public static readonly IReadOnlyList<TableDefault> ControlPointDefaults = new List<TableDefault> { ControlPointSizeFunctionColumn, PatchTypeColumn, ConstantSizeColumn, RecipeColumn, StretchRateColumn, MultipointColumn, TinResolutionColumn, SizeToleranceColumn, SecondaryConstantSizeColumn, RotationDegreesColumn };

	public static readonly IReadOnlyList<TableDefault> GlobalMetadataDefaults = new List<TableDefault> { PatchTypeColumn, ControlPointSizeFunctionColumn, ConstantSizeColumn, SecondaryConstantSizeColumn, RotationDegreesColumn, StretchRateColumn, MultipointColumn, TinResolutionColumn, SizeToleranceColumn };

	private static HashSet<string> _wellKnownColumnNames;

	public static List<int> InverseSet(int count, List<int> selectedRegions)
	{
		return Enumerable.Range(0, count).Except(selectedRegions).ToList();
	}

	public static OperationResult BuildConceptualMeshAsSliding(this Topology topo, out ConceptualMesh cmesh, out SlidingMesh slidingMesh, ConceptualMeshCache cache = null, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		ProgressReporter reporter2 = reporter.WithProgressModifier(0, 0.05f);
		OperationResult operationResult = topo.BuildConceptualMesh(out cmesh, cache, reporter2);
		if (reporter.CancelRequested)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetUserOperationCancelled("Building Conceptual Mesh"));
			cmesh = null;
			slidingMesh = null;
			return operationResult;
		}
		stopwatch.Stop();
		if (!operationResult)
		{
			_logger.LogDebug("Error building conceptual mesh");
			reporter?.ReportMessage("Failed to build conceptual mesh.");
			slidingMesh = null;
			return operationResult;
		}
		_logger.LogDebug("Conceptual mesh compute took {Elapsed}", stopwatch.Elapsed);
		reporter?.ReportMessage("Built conceptual mesh");
		SlidingMesh[] slidingMeshes = new SlidingMesh[topo.CompletedRegions.Count];
		int recover = 0;
		if (cache != null)
		{
			recover = cache.Recover(topo, slidingMeshes);
			_logger.LogDebug("{Count} Sliding Meshes recovered from the cache.", recover);
		}
		MeshProfiler.Instance.StartTask(MeshProfiler.MeshTask.BuildMesh);
		ProgressReporter reporter3 = reporter.WithProgressModifier(5, 0.92f);
		stopwatch.Restart();
		operationResult.MergeIn(topo.BuildMeshInternal_Parallel(cmesh, slidingMeshes, reporter3));
		if (!operationResult)
		{
			slidingMesh = null;
			return operationResult;
		}
		stopwatch.Stop();
		_logger.LogDebug("Building {Count} Sliding Meshes took {Elapsed}", slidingMeshes.Length - recover, stopwatch.Elapsed);
		reporter?.ReportMessage("Generated region meshes");
		MeshProfiler.Instance.StopTask(MeshProfiler.MeshTask.BuildMesh);
		cache?.Set(topo, slidingMeshes);
		int slidingMeshesSlidingMeshTerm = slidingMeshes.Count((SlidingMesh sm) => sm != null);
		if (slidingMeshesSlidingMeshTerm == 0)
		{
			slidingMesh = null;
			return operationResult;
		}
		stopwatch.Restart();
		operationResult.MergeIn(SlidingMesh.Merge(slidingMeshes, out slidingMesh, 0.0001, cmesh));
		reporter.ReportProgress(97);
		slidingMesh.RelaxConstraintMode();
		reporter.ReportProgress(98);
		reporter?.ReportMessage("Merged region meshes");
		TinSizeFunction globalSizeFunction = cmesh.GetGlobalSizeFunction(operationResult);
		operationResult.MergeIn(PostprocessGlobal(ref slidingMesh, cmesh, globalSizeFunction, reporter));
		reporter.ReportProgress(99);
		reporter?.ReportMessage("Global postprocessing complete");
		stopwatch.Stop();
		_logger.LogDebug("Merging {Count} Sliding Meshes took {Elapsed}", slidingMeshesSlidingMeshTerm, stopwatch.Elapsed);
		return operationResult;
	}

	public static OperationResult BuildConceptualMesh(this Topology topo, out ConceptualMesh cmesh, out Mesh finalMesh, ConceptualMeshCache cache = null, ProgressReporter reporter = null)
	{
		MeshProfiler.Instance.Reset();
		MeshProfiler.Instance.StartTask(MeshProfiler.MeshTask.BuildMeshTotal);
		OperationResult operationResult = topo.BuildConceptualMeshAsSliding(out cmesh, out var slidingMesh, cache, reporter);
		Stopwatch stopwatch = Stopwatch.StartNew();
		finalMesh = null;
		try
		{
			if (slidingMesh != null)
			{
				operationResult.MergeIn(slidingMesh.ToMesh(out finalMesh, cmesh));
			}
		}
		catch (StructuredException ex)
		{
			operationResult.AddMessages(ex.Messages);
			finalMesh = null;
			operationResult.Result = false;
		}
		catch (Exception ex2)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetExceptionOccurred("Merging regional meshes to final mesh", ex2));
			finalMesh = null;
		}
		if (finalMesh != null)
		{
			slidingMesh.LabelMesh(cmesh, out var cellRegions, out var _);
			if (topo.ReorderSwitch == Topology.Reorder.Global)
			{
				try
				{
					finalMesh?.ReorderCellsByAdjacency();
				}
				catch (Exception exception)
				{
					_logger.LogWarning(exception, "Global ReorderCellsByAdjacency failed; continuing with original mesh order.");
				}
			}
			else if (topo.ReorderSwitch == Topology.Reorder.Regions)
			{
				List<int> quadRegions = cmesh.GetQuadRegions();
				int startIdx = cmesh.RegionFarthestFromAverageCentroid(quadRegions);
				List<int> first = cmesh.BreadthFirstAdjacentCells(startIdx, quadRegions);
				List<int> inverseSet = InverseSet(cmesh.CellCount, quadRegions);
				int startIdx2 = cmesh.RegionFarthestFromAverageCentroid(inverseSet);
				List<int> second = cmesh.BreadthFirstAdjacentCells(startIdx2, inverseSet);
				List<int> regionOrder = first.Concat(second).ToList();
				List<int>[] regionCells = Mesh.RegionCellsLists(cellRegions, topo.CompletedRegions.Count);
				try
				{
					finalMesh?.ReorderCellsByAdjacency(regionOrder, cellRegions, regionCells);
				}
				catch (Exception exception2)
				{
					_logger.LogWarning(exception2, "Regional ReorderCellsByAdjacency failed; continuing with original mesh order.");
				}
			}
			else if (topo.ReorderSwitch == Topology.Reorder.Random)
			{
				try
				{
					finalMesh?.ReorderCellsRandom();
				}
				catch (Exception exception3)
				{
					_logger.LogWarning(exception3, "Random ReorderCellsRandom failed; continuing with original mesh order.");
				}
			}
		}
		stopwatch.Stop();
		MeshProfiler.Instance.StopTask(MeshProfiler.MeshTask.BuildMeshTotal);
		reporter?.ReportProgress(100);
		_logger.LogDebug("Export sliding mesh took {Elapsed}", stopwatch.Elapsed);
		reporter?.ReportMessage("Mesh generation complete");
		reporter?.ReportMessage(MeshProfiler.Instance.BriefSummary(), LogLevel.Information);
		reporter?.ReportMessage(MeshProfiler.Instance.Summary(), LogLevel.Debug);
		return operationResult;
	}

	public static OperationResult BuildConceptualMesh(this Topology topo, out ConceptualMesh cmesh, ConceptualMeshCache cache, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		reporter.ReportProgress(0);
		OperationResult operationResult = topo.BuildConceptualMeshTopology(out cmesh, cache, reporter);
		if (!operationResult.Result)
		{
			return operationResult;
		}
		operationResult.MergeIn(topo.BuildConceptualMeshDiscretization(ref cmesh, reporter));
		return operationResult;
	}

	public static float RegionStretchRate(MeshRegionMetadata md)
	{
		if (float.IsNaN(md.StretchRate) || md.StretchRate < 0f)
		{
			if (md.PatchType == PatchType.TrianglePaver)
			{
				return 0.3f;
			}
			return 0f;
		}
		return md.StretchRate;
	}

	private static OperationResult BuildConceptualMeshTopology(this Topology topo, out ConceptualMesh cmesh, ConceptualMeshCache cache, ProgressReporter reporter = null)
	{
		OperationResult operationResult = new OperationResult();
		MeshProfiler.Instance.StartTask(MeshProfiler.MeshTask.BuildConceptualMesh);
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		cmesh = new ConceptualMesh(cache);
		for (int i = 0; i < topo.Nodes.Count; i++)
		{
			cmesh.AddNode(topo.Nodes[i].Point);
		}
		reporter.ReportProgress(5);
		for (int j = 0; j < topo.Arcs.Count; j++)
		{
			Arc arc = topo.Arcs[j];
			cmesh.AddFace(arc.StartNode, arc.EndNode, (arc.RegionA < 0) ? (-1) : arc.RegionA, (arc.RegionB < 0) ? (-1) : arc.RegionB);
			cmesh.FaceRawLine[j] = topo.GetArcPolyline(j);
		}
		reporter.ReportProgress(10);
		int ordinal = 0;
		for (int k = 0; k < topo.CompletedRegions.Count; k++)
		{
			MeshRegionMetadata meshRegionMetadata = topo.GetRegionData(k, operationResult);
			meshRegionMetadata.ControlPointIndex = topo.CompletedRegions[k].ControlPointIndex;
			meshRegionMetadata.StretchRate = RegionStretchRate(meshRegionMetadata);
			Region region = topo.CompletedRegions[k];
			int addCell = cmesh.AddCell();
			RegionRing outerRing = region.OuterRing;
			for (int l = 0; l < outerRing.Arcs.Count; l++)
			{
				ArcWithDirection arcWithDirection = outerRing.Arcs[l];
				cmesh.CellAddFace(addCell, arcWithDirection.ArcID, (!arcWithDirection.IsForward) ? ((byte)1) : ((byte)0));
			}
			_ = cmesh.CellFaces[addCell];
			if (meshRegionMetadata.PatchType == PatchType.Quad || meshRegionMetadata.PatchType == PatchType.OneD)
			{
				meshRegionMetadata = topo.UpdateCorners(k, meshRegionMetadata);
				for (int m = 0; m < 4; m++)
				{
					int cornerRingNode = meshRegionMetadata.CornerRingNode(m);
					if (cornerRingNode >= cmesh.CellFaces[addCell].Count)
					{
						throw new ArgumentOutOfRangeException($"Corner node {cornerRingNode} is greater than number of region arcs {cmesh.CellFaces[addCell].Count}");
					}
				}
			}
			meshRegionMetadata.ComputedConstantSize = float.NaN;
			cmesh.CellRegionMetadata[addCell] = meshRegionMetadata;
			if (meshRegionMetadata.SizeFunction != SizeFunction.Constant)
			{
				throw new NotSupportedException("TIN size function not yet supported here");
			}
			if (meshRegionMetadata.PatchType != PatchType.Empty)
			{
				ordinal++;
			}
		}
		if (ordinal == 0)
		{
			operationResult.AddFailure(MessageCodes.GetErrorNoRegionsToMesh());
		}
		reporter.ReportProgress(15);
		return operationResult;
	}

	private static void TranscribeDiscretizationData(this Topology topo, ref ConceptualMesh cmesh, OperationResult res)
	{
		for (int i = 0; i < topo.Arcs.Count; i++)
		{
			cmesh.FaceNodeString[i] = null;
			ArcMetadata arcData = topo.GetArcData(i);
			DiscretizeLine discretizeLine;
			if (arcData.SizeFunction == LinearSizeFunction.CountWithBias)
			{
				if (arcData.Count < 1)
				{
					discretizeLine = DiscretizeLine.Unset();
				}
				else
				{
					if (arcData.Bias <= 0f)
					{
						res.AddMessage(TopologyMetadataMessageCodes.GetBadArcMetadata_Bias(i));
						arcData.Bias = 1f;
					}
					discretizeLine = DiscretizeLine.ByCount(arcData.Count + 1, arcData.Bias);
				}
			}
			else if (arcData.SizeFunction == LinearSizeFunction.Constant)
			{
				if (arcData.ConstantSize <= 0f)
				{
					discretizeLine = DiscretizeLine.Unset();
				}
				else if (arcData.Bias <= 0f)
				{
					res.AddMessage(TopologyMetadataMessageCodes.GetBadArcMetadata_Bias(i));
					arcData.Bias = 1f;
				}
				double length = cmesh.FaceRawLine[i].Length;
				discretizeLine = DiscretizeLine.BySize(arcData.ActualPrimaryCellSize, arcData.Bias, length);
			}
			else
			{
				if (arcData.SizeFunction != LinearSizeFunction.Unset)
				{
					throw new NotSupportedException(arcData.SizeFunction.ToString() + " not recognized.");
				}
				discretizeLine = DiscretizeLine.Unset();
			}
			discretizeLine.ConstrainMode = arcData.ConstrainStretching;
			discretizeLine.LaplaceBCMode = arcData.LaplaceBC;
			discretizeLine.straightDistances = arcData.DistanceStraightLine;
			discretizeLine.areaWeight = arcData.AreaWeight;
			discretizeLine.discretizeMagnetism = arcData.DiscretizeMagnetism;
			discretizeLine.followExactly = arcData.FollowExactly;
			cmesh.FaceDiscretization[i] = discretizeLine;
		}
	}

	private static OperationResult BuildConceptualMeshDiscretization(this Topology topo, ref ConceptualMesh cmesh, ProgressReporter reporter = null)
	{
		OperationResult operationResult = new OperationResult();
		topo.TranscribeDiscretizationData(ref cmesh, operationResult);
		if (!operationResult.Result)
		{
			return operationResult;
		}
		if (reporter.CancelRequested)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetUserOperationCancelled("Building Conceptual Mesh"));
			return operationResult;
		}
		operationResult.MergeIn(InferCellSizes.Apply(cmesh, reporter));
		if (!operationResult.Result)
		{
			return operationResult;
		}
		reporter.ReportProgress(20);
		reporter?.ReportMessage("Local arc discretization");
		Dictionary<int, double> determineRegionPriority = DetermineRegionPriority(cmesh);
		for (int i = 0; i < topo.Arcs.Count; i++)
		{
			PatchType patchType = PatchType.Empty;
			PatchType patchType2 = PatchType.Empty;
			int a = cmesh.FaceCells[i].A;
			if (a >= 0)
			{
				patchType = cmesh.CellRegionMetadata[a].PatchType;
			}
			int b = cmesh.FaceCells[i].B;
			if (b >= 0)
			{
				patchType2 = cmesh.CellRegionMetadata[b].PatchType;
			}
			if (cmesh.FaceDiscretization[i].Mode == SizeFunctionMode.Unset)
			{
				continue;
			}
			if (cmesh.FaceDiscretization[i].ConstrainMode == ConstrainStretchingOptions.Constrained)
			{
				cmesh.SetArcNodesFromSmooth(i, FaceDiscretizationState.FIXED);
			}
			else if (patchType == PatchType.Quad && patchType2 == PatchType.Quad)
			{
				if (determineRegionPriority[a] == determineRegionPriority[b])
				{
					cmesh.SetArcNodesFromSmooth(i, FaceDiscretizationState.FIXED);
				}
			}
			else if (patchType == PatchType.OneD && patchType2 == PatchType.OneD)
			{
				cmesh.FaceDiscretization[i] = new DiscretizeLine(2);
				cmesh.SetArcNodesFromSmooth(i, FaceDiscretizationState.FIXED);
			}
		}
		if (reporter.CancelRequested)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetUserOperationCancelled("Building Conceptual Mesh"));
			return operationResult;
		}
		reporter.ReportProgress(30);
		reporter?.ReportMessage("Quad region discretization");
		MeshProfiler.Instance.StartTask(MeshProfiler.MeshTask.ComputeLaplaceAll);
		int count = topo.CompletedRegions.Count;
		ConceptualMesh localCMesh = cmesh;
		Parallel.For(0, count, (int regionIdx) =>
		{
			new OperationResult();
			MeshRegionMetadata regionData2 = topo.GetRegionData(regionIdx);
			if (!regionData2.IsEmpty && (regionData2.PatchType == PatchType.Quad || regionData2.PatchType == PatchType.OneD))
			{
				try
				{
					Thread.CurrentThread.Name = "Quad Region Discretizing: " + regionIdx;
					localCMesh.GetQuadTransform(regionIdx, reporter);
				}
				catch (Exception)
				{
				}
			}
		});
		MeshProfiler.Instance.StopTask(MeshProfiler.MeshTask.ComputeLaplaceAll);
		if (reporter.CancelRequested)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetUserOperationCancelled("Building Conceptual Mesh"));
			return operationResult;
		}
		foreach (KeyValuePair<int, double> localValue in from kvp in determineRegionPriority
			select (kvp) into kvp
			orderby 0.0 - kvp.Value
			select kvp)
		{
			int key = localValue.Key;
			MeshRegionMetadata regionData = topo.GetRegionData(key);
			if (regionData.IsEmpty || (regionData.PatchType != PatchType.Quad && regionData.PatchType != PatchType.OneD))
			{
				continue;
			}
			MeshProfiler.Instance.StartTask(MeshProfiler.MeshTask.DiscretizeRegion);
			try
			{
				DiscretizeRegion.Apply(cmesh, key);
			}
			catch (Exception ex)
			{
				int controlPointIndex = topo.CompletedRegions[key].ControlPointIndex;
				operationResult.AddFailure(MessageCodes.GetErrorDiscretizingQuadPatch(controlPointIndex, key, ex.Message));
				if ((ex is DynamicMeshException || ex is ArgumentException) ? true : false)
				{
					foreach (HalfEdge cellFace in cmesh.CellFaces[key])
					{
						if (cmesh.FaceNodeString[cellFace.fIdx] == null)
						{
							cmesh.SetArcNodesFromSmooth(cellFace.fIdx, cmesh.FaceDiscretization[cellFace.fIdx]);
						}
					}
				}
				else
				{
					_logger.LogWarning(ex, "Unexpected error during mesh generation.");
				}
			}
			MeshProfiler.Instance.StopTask(MeshProfiler.MeshTask.DiscretizeRegion);
		}
		if (!operationResult.Result)
		{
			return operationResult;
		}
		if (reporter.CancelRequested)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetUserOperationCancelled("Building Conceptual Mesh"));
			return operationResult;
		}
		cmesh.UpdateIslandBreaklineFaceCells();
		reporter.ReportProgress(70);
		reporter?.ReportMessage("Computing global size function");
		TinSizeFunction tinSizeFunction = null;
		try
		{
			MeshProfiler.Instance.StartTask(MeshProfiler.MeshTask.GlobalSizeFunction);
			tinSizeFunction = cmesh.GetGlobalSizeFunction(operationResult);
			MeshProfiler.Instance.StopTask(MeshProfiler.MeshTask.GlobalSizeFunction);
		}
		catch (StructuredException ex2)
		{
			operationResult.AddMessages(ex2.Messages);
			operationResult.Result = false;
			return operationResult;
		}
		catch (Exception ex3)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetExceptionOccurred("Constructing global size function", ex3));
			return operationResult;
		}
		DiscretizeLine discretizeLine = new DiscretizeLine(tinSizeFunction);
		for (int intervalIndex = 0; intervalIndex < topo.Arcs.Count; intervalIndex++)
		{
			if (cmesh.FaceNodeState[intervalIndex] == FaceDiscretizationState.DISCRETIZED)
			{
				continue;
			}
			PatchType patchType3 = CellPatchType(cmesh, cmesh.FaceCells[intervalIndex].A);
			PatchType patchType4 = CellPatchType(cmesh, cmesh.FaceCells[intervalIndex].B);
			if ((patchType3 == PatchType.OneD && patchType4 == PatchType.Empty) || (patchType4 == PatchType.OneD && patchType3 == PatchType.Empty))
			{
				if (cmesh.FaceNodeString[intervalIndex] == null)
				{
					cmesh.FaceNodeString[intervalIndex] = cmesh.FaceSimpleGeometry(intervalIndex);
				}
				cmesh.FaceNodeState[intervalIndex] = FaceDiscretizationState.DISCRETIZED;
			}
			else if (cmesh.FaceNodeString[intervalIndex] == null)
			{
				if (discretizeLine != null)
				{
					DiscretizeLine discretizeLine2 = cmesh.FaceDiscretization[intervalIndex].Clone();
					discretizeLine2.SetSizeFunction(tinSizeFunction);
					cmesh.FaceDiscretization[intervalIndex] = discretizeLine2;
				}
				cmesh.SetArcNodesFromSmooth(intervalIndex, cmesh.FaceDiscretization[intervalIndex]);
			}
			else if (cmesh.FaceNodeState[intervalIndex] == FaceDiscretizationState.DISCRETIZED_SUBSET)
			{
				cmesh.SetArcNodesFromSmooth(intervalIndex, cmesh.FaceDiscretization[intervalIndex], update: true, includeExisting: true);
				cmesh.FaceNodeState[intervalIndex] = FaceDiscretizationState.DISCRETIZED;
			}
		}
		reporter.ReportProgress(95);
		reporter?.ReportMessage("Draft discretization complete, checking for conflicts.");
		for (int intervalIndex2 = 0; intervalIndex2 < cmesh.CellCount; intervalIndex2++)
		{
			Polygon polygon = cmesh.CellNodePolygon(intervalIndex2);
			double area = 0.0;
			bool condition = true;
			if (polygon.PointCount <= 2)
			{
				condition = false;
			}
			else
			{
				area = polygon.Area;
				if (area <= 0.0)
				{
					condition = false;
				}
				else if (area / polygon.Perimeter < 1E-06)
				{
					condition = false;
				}
			}
			if (!condition)
			{
				int controlPointIndex2 = cmesh.CellRegionMetadata[intervalIndex2].ControlPointIndex;
				operationResult.AddFailure(ConceptualMesh.MessageCodes.GetInvalidRegion(controlPointIndex2, intervalIndex2, $"Area={area}"));
			}
		}
		if (operationResult.Result)
		{
			OperationResult other = cmesh.RepairDiscretizationConflicts(reporter);
			operationResult.MergeIn(other);
		}
		reporter.ReportProgress(100);
		reporter?.ReportMessage("Discretization complete");
		MeshProfiler.Instance.StopTask(MeshProfiler.MeshTask.BuildConceptualMesh);
		return operationResult;
		static PatchType CellPatchType(ConceptualMesh conceptualMesh, int cIdx)
		{
			if (cIdx >= 0)
			{
				return conceptualMesh.CellRegionMetadata[cIdx].PatchType;
			}
			return PatchType.Empty;
		}
	}

	public static OperationResult BuildMesh(this Topology topo, ConceptualMesh cmesh, out SlidingMesh[] computedMeshes, int[] computeOnlyTheseRegions = null)
	{
		return topo.BuildMeshInternal(cmesh, out computedMeshes, computeOnlyTheseRegions);
	}

	private static OperationResult BuildMeshInternal(this Topology topo, ConceptualMesh cmesh, out SlidingMesh[] computedMeshes, int[] computeOnlyTheseRegions = null)
	{
		OperationResult operationResult = new OperationResult();
		if (computeOnlyTheseRegions == null)
		{
			computedMeshes = new SlidingMesh[topo.CompletedRegions.Count];
		}
		else
		{
			computedMeshes = new SlidingMesh[computeOnlyTheseRegions.Length];
		}
		for (int i = 0; i < computedMeshes.Length; i++)
		{
			int computeOnlyTheseRegion = ((computeOnlyTheseRegions == null) ? i : computeOnlyTheseRegions[i]);
			_ = topo.CompletedRegions[computeOnlyTheseRegion].OuterRing;
			MeshRegionMetadata regionData = topo.GetRegionData(computeOnlyTheseRegion);
			if (regionData.IsEmpty)
			{
				continue;
			}
			OperationResult operationResult2 = topo.TryGetRegionGenerator(cmesh, computeOnlyTheseRegion, out var generator);
			if (!operationResult2 || generator == null)
			{
				operationResult.MergeIn(operationResult2);
				continue;
			}
			int controlPointIndex = cmesh.CellRegionMetadata[computeOnlyTheseRegion].ControlPointIndex;
			bool checkMesh = false;
			try
			{
				SlidingMesh slidingMesh = generator.Generate();
				checkMesh = slidingMesh.CheckMesh(out var _);
				if (!checkMesh)
				{
					operationResult.AddFailure(MessageCodes.GetUnknownMeshingError(controlPointIndex, computeOnlyTheseRegion));
				}
				computedMeshes[i] = slidingMesh;
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Mesh generation error for region {RegionIdx}.", computeOnlyTheseRegion);
				operationResult.AddFailure(GeneralMessageCodes.GetExceptionOccurred("Creating Mesh", ex));
				operationResult.AddFailure(MessageCodes.GetUnknownMeshingError(controlPointIndex, computeOnlyTheseRegion));
				if (ContinueOnBadMeshgen)
				{
					continue;
				}
				return operationResult;
			}
			try
			{
				PostprocessRegion(computedMeshes[i], cmesh, regionData, cmesh.GetGlobalSizeFunction(operationResult));
			}
			catch (Exception ex2)
			{
				_logger.LogWarning(ex2, "Mesh post-processing error for region {RegionIdx}.", computeOnlyTheseRegion);
				operationResult.AddFailure(GeneralMessageCodes.GetExceptionOccurred("Post-processing Mesh", ex2));
				operationResult.AddFailure(MessageCodes.GetUnknownMeshPostProcessingError(controlPointIndex, computeOnlyTheseRegion));
				computedMeshes[i] = null;
				if (ContinueOnBadMeshgen)
				{
					continue;
				}
				return null;
			}
			if (!computedMeshes[i].CheckMesh(out var _) & checkMesh)
			{
				operationResult.AddFailure(MessageCodes.GetUnknownMeshPostProcessingError(controlPointIndex, computeOnlyTheseRegion));
			}
		}
		return operationResult;
	}

	private static OperationResult BuildMeshInternal_Parallel(this Topology topo, ConceptualMesh cmesh, SlidingMesh[] computedMeshes, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		int count = 0;
		for (int i = 0; i < computedMeshes.Length; i++)
		{
			if (computedMeshes[i] == null)
			{
				count++;
			}
		}
		_ = 1f / (float)count;
		int[] todo = new int[count];
		count = 0;
		for (int j = 0; j < computedMeshes.Length; j++)
		{
			if (computedMeshes[j] == null)
			{
				todo[count] = j;
				count++;
			}
		}
		if (count == 0)
		{
			return true;
		}
		if (reporter.CancelRequested)
		{
			return OperationResult.Fail(GeneralMessageCodes.GetUserOperationCancelled("Generating Mesh"));
		}
		double regionPercentOfWhole = 1.0 / (double)count;
		int[] individualRegionProgress = new int[count];
		OperationResult[] individualResults = new OperationResult[count];
		Parallel.For(0, count, (int ii) =>
		{
			OperationResult operationResult2 = new OperationResult();
			individualResults[ii] = operationResult2;
			int todo2 = todo[ii];
			ProgressReporter regionReporter = new ProgressReporter();
			regionReporter.ProgressReported += (ProgressReporter sender, int prog) =>
			{
				if (reporter.CancelRequested)
				{
					regionReporter.RequestCancel();
				}
				else if (individualRegionProgress[ii] != prog)
				{
					individualRegionProgress[ii] = prog;
					double computed = 0.0;
					for (int k = 0; k < individualRegionProgress.Length; k++)
					{
						computed += (double)individualRegionProgress[k] * regionPercentOfWhole;
					}
					reporter.ReportProgress((int)computed);
				}
			};
			_ = topo.CompletedRegions[todo2].OuterRing;
			MeshRegionMetadata regionData = topo.GetRegionData(todo2);
			if (regionData.IsEmpty)
			{
				return;
			}
			int controlPointIndex = cmesh.CellRegionMetadata[todo2].ControlPointIndex;
			MeshProfiler.Instance.StartGenerateTask(cmesh.CellRegionMetadata[todo2].PatchType);
			try
			{
				OperationResult operationResult3 = topo.TryGetRegionGenerator(cmesh, todo2, out var generator);
				operationResult2.MergeIn(operationResult3);
				if (!operationResult3)
				{
					return;
				}
				SlidingMesh slidingMesh = generator.Generate(null, regionReporter.WithProgressModifier(0, 0.15f), operationResult2);
				if (reporter.CancelRequested || !operationResult2)
				{
					return;
				}
				slidingMesh.CheckMesh(out var _);
				regionReporter.ReportProgress(16);
				if (reporter.CancelRequested)
				{
					return;
				}
				computedMeshes[todo2] = slidingMesh;
			}
			catch (StructuredException ex)
			{
				_logger.LogWarning(ex, "Mesh generation error for region {RegionIdx}.", todo2);
				operationResult2.AddFailures(ex.Messages);
				return;
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Mesh generation error for region {RegionIdx}.", todo2);
				operationResult2.AddFailure(MessageCodes.GetUnknownMeshingError(controlPointIndex, todo2));
				return;
			}
			finally
			{
				MeshProfiler.Instance.StopGenerateTask(cmesh.CellRegionMetadata[todo2].PatchType);
			}
			try
			{
				PostprocessRegion(computedMeshes[todo2], cmesh, regionData, cmesh.GetGlobalSizeFunction(operationResult2), regionReporter.WithProgressModifier(16, 0.84f));
			}
			catch (StructuredException ex2)
			{
				_logger.LogWarning(ex2, "Mesh post-processing error for region {RegionIdx}.", todo2);
				_logger.LogWarning(ex2.Message);
				operationResult2.AddFailures(ex2.Messages);
				computedMeshes[todo2] = null;
			}
			catch (Exception exception2)
			{
				_logger.LogWarning(exception2, "Mesh post-processing error for region {RegionIdx}.", todo2);
				operationResult2.AddFailure(MessageCodes.GetUnknownMeshPostProcessingError(controlPointIndex, todo2));
				computedMeshes[todo2] = null;
			}
		});
		OperationResult operationResult = new OperationResult();
		for (int intervalIndex = 0; intervalIndex < individualResults.Length; intervalIndex++)
		{
			if (individualResults[intervalIndex] != null)
			{
				operationResult.MergeIn(individualResults[intervalIndex]);
			}
		}
		if (reporter.CancelRequested)
		{
			operationResult.AddDedupedFailure(GeneralMessageCodes.GetUserOperationCancelled("Generating Mesh"));
		}
		return operationResult;
	}

	public static Dictionary<int, double> DetermineRegionPriority(ConceptualMesh cmesh)
	{
		PriorityQueue<int, double> priorityQueue = new PriorityQueue<int, double>();
		List<double> arcScores = new List<double>(cmesh.FaceCount);
		for (int i = 0; i < cmesh.FaceCount; i++)
		{
			double unity;
			if (cmesh.FaceCells[i].A == cmesh.FaceCells[i].B)
			{
				unity = -1.0;
			}
			else
			{
				PatchType patchType = CellPatchType(cmesh.FaceCells[i].A);
				PatchType patchType2 = CellPatchType(cmesh.FaceCells[i].B);
				if (patchType == PatchType.Quad && !IsFaceOfIsland(i, 0))
				{
					unity = ((patchType2 != PatchType.Quad || IsFaceOfIsland(i, 1)) ? 0.0 : double.MaxValue);
				}
				else
				{
					unity = ((patchType2 != PatchType.Quad || IsFaceOfIsland(i, 1)) ? (-1.0) : 0.0);
				}
				if (unity == 0.0)
				{
					priorityQueue.Enqueue(i, unity);
				}
			}
			arcScores.Add(unity);
		}
		int element;
		double priority;
		while (priorityQueue.Count > 0 && priorityQueue.TryDequeue(out element, out priority))
		{
			if (arcScores[element] < priority)
			{
				continue;
			}
			for (int j = 0; j < 2; j++)
			{
				int cIdx = cmesh.FaceCells[element][j];
				if (CellPatchType(cIdx) != PatchType.Quad || IsFaceOfIsland(element, (byte)j))
				{
					continue;
				}
				List<PatchSide> patchSides = cmesh.PatchSides(cIdx);
				int k = 0;
				bool condition = false;
				for (; k < 4; k++)
				{
					if (condition)
					{
						break;
					}
					foreach (HalfEdge sidePiece in patchSides[k].SidePieces)
					{
						if (sidePiece.fIdx == element)
						{
							condition = true;
							break;
						}
					}
					if (condition)
					{
						break;
					}
				}
				double approxLocalCountPatchSidesTerm = 0.5 * (patchSides[k].ApproxLocalCount() + patchSides[(k + 2) % 4].ApproxLocalCount());
				double approxLocalCountPatchSidesTerm2 = 0.5 * (patchSides[(k + 1) % 4].ApproxLocalCount() + patchSides[(k + 3) % 4].ApproxLocalCount());
				double arcScores2 = ((approxLocalCountPatchSidesTerm > approxLocalCountPatchSidesTerm2) ? 1.0 : 0.0);
				foreach (HalfEdge sidePiece2 in patchSides[(k + 2) % 4].SidePieces)
				{
					if (arcScores[sidePiece2.fIdx] > arcScores2)
					{
						arcScores[sidePiece2.fIdx] = arcScores2;
						priorityQueue.Enqueue(sidePiece2.fIdx, arcScores[sidePiece2.fIdx]);
					}
				}
			}
		}
		Dictionary<int, double> doubleByInt = new Dictionary<int, double>();
		foreach (int cellIterator in cmesh.CellIterator())
		{
			if (cmesh.CellRegionMetadata[cellIterator].PatchType == PatchType.OneD)
			{
				if (!OneDJunction.IsJunction(cmesh, cellIterator))
				{
					doubleByInt.Add(cellIterator, 1000.0);
				}
			}
			else
			{
				if (cmesh.CellRegionMetadata[cellIterator].PatchType != PatchType.Quad)
				{
					continue;
				}
				List<PatchSide> patchSidesList = cmesh.PatchSides(cellIterator);
				double[] doubleValues = new double[4];
				for (int l = 0; l < 4; l++)
				{
					doubleValues[l] = patchSidesList[l].SidePieces.Select((HalfEdge he) => arcScores[he.fIdx]).Average();
				}
				double computed = Math.Min(doubleValues[0], doubleValues[2]);
				double computed2 = Math.Min(doubleValues[3], doubleValues[1]);
				double computed3 = Math.Max(computed, computed2);
				doubleByInt.Add(cellIterator, computed3);
			}
		}
		return doubleByInt;
		PatchType CellPatchType(int ordinal)
		{
			if (ordinal < 0)
			{
				return PatchType.Empty;
			}
			return cmesh.CellRegionMetadata[ordinal].PatchType;
		}
		bool IsFaceOfIsland(int fIdx, byte orient)
		{
			int faceCell = cmesh.FaceCells[fIdx][orient];
			if (faceCell < 0)
			{
				return false;
			}
			return !cmesh.CellFaces[faceCell].Contains(new HalfEdge(fIdx, orient));
		}
	}

	public static MeshRegionMetadata UpdateCorners(this Topology topo, int regionIdx, MeshRegionMetadata md)
	{
		if (md.CornerRingNodeSW >= 0 && md.CornerRingNodeSE >= 0 && md.CornerRingNodeNE >= 0 && md.CornerRingNodeNW >= 0)
		{
			return md;
		}
		if (md.CornerRingNodeSW >= 0 || md.CornerRingNodeSE >= 0 || md.CornerRingNodeNE >= 0 || md.CornerRingNodeNW >= 0)
		{
			throw new ArgumentException("No support for partially specified corners in quad patch");
		}
		PatchType patchType = md.PatchType;
		if (patchType != PatchType.Quad && patchType != PatchType.OneD)
		{
			return md;
		}
		List<ArcWithDirection> arcs = topo.CompletedRegions[regionIdx].OuterRing.Arcs;
		ComputeCornerTurns(topo, arcs, md, out var turns, out var totalLengths);
		int[] range = Enumerable.Range(0, arcs.Count).ToArray();
		Array.Sort(turns.ToArray(), range);
		int count = Math.Min(7, range.Length);
		int[] take = range.Take(count).ToArray();
		take.Sort();
		uint ordinal = 0u;
		double maxValue = double.MaxValue;
		int[] intValues = new int[4];
		int intervalIndex;
		for (uint ordinal2 = 0u; ordinal2 < 1 << count; ordinal2++)
		{
			if (BitOperations.PopCount(ordinal2) != 4)
			{
				continue;
			}
			intervalIndex = 0;
			double computed = 0.0;
			for (int i = 0; i < count; i++)
			{
				if ((ordinal2 & (1 << i)) != 0L)
				{
					intValues[intervalIndex] = take[i];
					intervalIndex++;
					computed += Math.Exp(2.0 * Math.Max(0.0, turns[take[i]] + Math.PI / 2.0));
				}
			}
			double totalLengthsTerm = totalLengths.Last();
			double totalLengthStep = totalLengths[intValues[1]] - totalLengths[intValues[0]];
			double totalLengthStep2 = totalLengths[intValues[2]] - totalLengths[intValues[1]];
			double totalLengthStep3 = totalLengths[intValues[3]] - totalLengths[intValues[2]];
			double computed2 = totalLengthsTerm - totalLengthStep - totalLengthStep2 - totalLengthStep3;
			double computed3 = 4.0 * Math.Abs(totalLengthStep3 - totalLengthStep) / totalLengthsTerm;
			double computed4 = 4.0 * Math.Abs(totalLengthStep2 - computed2) / totalLengthsTerm;
			double maxValue2 = computed + 2.0 * (computed3 + computed4);
			if (maxValue2 < maxValue)
			{
				ordinal = ordinal2;
				maxValue = maxValue2;
			}
		}
		intervalIndex = 0;
		for (int j = 0; j < count; j++)
		{
			if ((ordinal & (1 << j)) != 0L)
			{
				intValues[intervalIndex] = take[j];
				intervalIndex++;
			}
		}
		double totalLengthsTerm2 = totalLengths[intValues[1]] - totalLengths[intValues[0]] + totalLengths[intValues[3]] - totalLengths[intValues[2]];
		double totalLengthsTerm3 = totalLengths.Last() - totalLengthsTerm2;
		if (totalLengthsTerm2 < totalLengthsTerm3)
		{
			intValues.RotateBackward(1);
		}
		if (md.PatchType == PatchType.OneD)
		{
			Span<int> intSpan = stackalloc int[4];
			for (int k = 0; k < 4; k++)
			{
				int ordinal3 = intValues[k];
				int count2 = (intValues[(k + 1) % 4] - ordinal3 + arcs.Count) % arcs.Count;
				for (int l = ordinal3; l < count2; l++)
				{
					int halfEdgeToOppositeRegion = topo.HalfEdgeToOppositeRegion(arcs[l % arcs.Count]);
					if (halfEdgeToOppositeRegion >= 0 && topo.GetRegionData(halfEdgeToOppositeRegion).PatchType == PatchType.OneD)
					{
						intSpan[k]++;
					}
				}
			}
			if (intSpan[0] + intSpan[2] > intSpan[1] + intSpan[3])
			{
				intValues.RotateBackward(1);
			}
		}
		md.SetCornerRingNodes(intValues);
		return md;
	}

	private static int HalfEdgeToOppositeRegion(this Topology topology, ArcWithDirection he)
	{
		Arc arc = topology.Arcs[he.ArcID];
		if (!he.IsForward)
		{
			return arc.RegionA;
		}
		return arc.RegionB;
	}

	private static List<double> ComputeCornerTurns(Topology topo, List<ArcWithDirection> faces, MeshRegionMetadata md, out List<double> turns, out List<double> totalLengths)
	{
		turns = new List<double>(faces.Count);
		totalLengths = new List<double>(faces.Count + 1) { 0.0 };
		List<double> localTotalLengths = totalLengths;
		double distanceToSum = 0.0;
		for (int i = 0; i < faces.Count; i++)
		{
			Arc arc = topo.Arcs[faces[i].ArcID];
			Point point = topo.Nodes[arc.StartNode].Point;
			Point point2 = topo.Nodes[arc.EndNode].Point;
			if (arc.InternalPointCount == 0)
			{
				distanceToSum += point.DistanceTo(point2);
			}
			else
			{
				foreach (Point internalPoint in arc.InternalPoints)
				{
					distanceToSum += point.DistanceTo(internalPoint);
					point = internalPoint;
				}
				distanceToSum += point.DistanceTo(point2);
			}
			totalLengths.Add(distanceToSum);
		}
		for (int j = 0; j < faces.Count; j++)
		{
			ArcWithDirection he = faces[(j == 0) ? (faces.Count - 1) : (j - 1)];
			ArcWithDirection he2 = faces[j];
			if (md.PatchType == PatchType.OneD)
			{
				int halfEdgeToOppositeRegion = topo.HalfEdgeToOppositeRegion(he);
				int halfEdgeToOppositeRegion2 = topo.HalfEdgeToOppositeRegion(he2);
				if (halfEdgeToOppositeRegion == halfEdgeToOppositeRegion2 && halfEdgeToOppositeRegion >= 0 && topo.GetRegionData(halfEdgeToOppositeRegion).PatchType == PatchType.OneD)
				{
					turns.Add(10.0);
					continue;
				}
			}
			_ = topo.Arcs[he2.ArcID];
			if (j == md.CornerRingNodeSW || j == md.CornerRingNodeSE || j == md.CornerRingNodeNE || j == md.CornerRingNodeNW)
			{
				turns.Add(-10.0);
				continue;
			}
			double facesTerm = Length((j + faces.Count - 1) % faces.Count);
			double computed = Length(j);
			double computed2 = 0.01 * facesTerm;
			double computed3 = 0.01 * computed;
			double computed4 = 0.2;
			if (computed2 < computed4 * computed3)
			{
				if (computed3 > facesTerm / computed4)
				{
					computed3 = facesTerm / computed4;
					computed2 = facesTerm;
				}
				else
				{
					double computed5 = Math.Sqrt(computed4 * computed3 / computed2);
					computed2 *= computed5;
					computed3 /= computed5;
				}
			}
			else if (computed3 < computed4 * computed2)
			{
				if (computed2 > computed / computed4)
				{
					computed2 = computed / computed4;
					computed3 = computed;
				}
				else
				{
					double computed6 = Math.Sqrt(computed4 * computed2 / computed3);
					computed3 *= computed6;
					computed2 /= computed6;
				}
			}
			double endBearing = EndBearing(topo, he.ArcID, !he.IsForward, computed2 / facesTerm);
			double piTerm = EndBearing(topo, he2.ArcID, he2.IsForward, computed3 / computed) + Math.PI - endBearing;
			piTerm = (piTerm + Math.PI * 3.0) % (Math.PI * 2.0) - Math.PI;
			turns.Add(0.0 - piTerm);
		}
		return turns;
		static double EndBearing(Topology topology, int arcID, bool start, double fraction)
		{
			Arc arc2 = topology.Arcs[arcID];
			Point point3 = topology.Nodes[start ? arc2.StartNode : arc2.EndNode].Point;
			Point ptA;
			if (arc2.InternalPointCount == 0)
			{
				ptA = topology.Nodes[start ? arc2.EndNode : arc2.StartNode].Point;
			}
			else
			{
				PointCollection internalPoints = arc2.InternalPoints;
				if (fraction == 0.0)
				{
					ptA = (start ? internalPoints.First() : internalPoints.Last());
				}
				else
				{
					Polyline polyline = new Polyline(internalPoints.Count + 2);
					polyline.Add(topology.Nodes[arc2.StartNode].Point);
					polyline.AddRange(internalPoints);
					polyline.Add(topology.Nodes[arc2.EndNode].Point);
					ptA = polyline.PointAtFraction(start ? fraction : (1.0 - fraction)).Point;
				}
			}
			return new Segment(ptA, point3).Bearing;
		}
		double Length(int ordinal)
		{
			return localTotalLengths[ordinal + 1] - localTotalLengths[ordinal];
		}
	}

	public static ISizeFunction GetRegionSizeFunction(this Topology topo, int regionId, bool secondary, ConceptualMeshCache cache)
	{
		if (topo.BuildConceptualMesh(out var cmesh, cache).Result)
		{
			topo.TryGetRegionGenerator(cmesh, regionId, out var generator);
			if (generator != null)
			{
				return generator.GetRegionSizeFunction(secondary);
			}
		}
		return null;
	}

	public static OperationResult TryGetRegionGenerator(this Topology topo, ConceptualMesh cmesh, int regionId, out IRegionGenerator generator)
	{
		OperationResult operationResult = new OperationResult();
		MeshRegionMetadata md = cmesh.CellRegionMetadata[regionId];
		if (md.IsEmpty)
		{
			generator = null;
			return operationResult;
		}
		switch (md.PatchType)
		{
		case PatchType.Quad:
			if (cmesh.CellFaces[regionId].Count < 4)
			{
				generator = null;
				operationResult.AddFailure(MessageCodes.GetQuadPatchRequires4Arcs(md.ControlPointIndex, regionId));
				return operationResult;
			}
			generator = new HybridQuadPatch(cmesh, regionId)
			{
				sizeTolerance = md.SizeTolerance
			};
			return true;
		case PatchType.OneD:
			if (OneDJunction.IsJunction(cmesh, regionId))
			{
				generator = new OneDJunction(cmesh, regionId);
				return operationResult;
			}
			if (cmesh.CellFaces[regionId].Count >= 4)
			{
				generator = new OneDReach(cmesh, regionId);
				return operationResult;
			}
			generator = null;
			operationResult.AddFailure(MessageCodes.GetQuadPatchRequires4Arcs(md.ControlPointIndex, regionId));
			return operationResult;
		case PatchType.TrianglePaver:
			if (md.SizeFunction == SizeFunction.Constant)
			{
				TrianglePaver trianglePaver = new TrianglePaver(cmesh, regionId);
				if (float.IsNaN(md.ActualPrimaryCellSize) || (double)md.ActualPrimaryCellSize <= 0.0)
				{
					trianglePaver.stretchRate = 0f;
					trianglePaver.targetSizeFunction = null;
				}
				else
				{
					trianglePaver.targetSizeFunction = new ConstantSizeFunction(md.ActualPrimaryCellSize);
					trianglePaver.stretchRate = RegionStretchRate(md);
				}
				trianglePaver.sizeFunction = cmesh.GetGlobalSizeFunction(operationResult);
				generator = trianglePaver;
				return operationResult;
			}
			throw new NotSupportedException();
		case PatchType.Cartesian:
		{
			if (float.IsNaN(md.ActualPrimaryCellSize) || md.ActualPrimaryCellSize <= 0f)
			{
				InferCellSizes.InferCartesianSize(cmesh, regionId, cmesh.GetGlobalSizeFunction(operationResult));
				md = cmesh.CellRegionMetadata[regionId];
			}
			if (float.IsNaN(md.ActualPrimaryCellSize) || md.ActualPrimaryCellSize <= 0f)
			{
				generator = null;
				return OperationResult.Fail(MessageCodes.GetBadConstantSizeFunction(md.ControlPointIndex, regionId, md.ActualPrimaryCellSize));
			}
			CartesianPatch cartesianPatch = new CartesianPatch(cmesh, regionId, cmesh.GetGlobalSizeFunction(operationResult))
			{
				targetSizeU = md.ActualPrimaryCellSize,
				targetSizeV = md.ActualSecondaryConstantSize,
				rotateDegCCW = (float.IsNaN(md.RotationDegrees) ? 0.0 : ((double)md.RotationDegrees)),
				stretchRateFromBoundary = RegionStretchRate(md)
			};
			generator = cartesianPatch;
			return operationResult;
		}
		default:
			throw new NotSupportedException();
		}
	}

	public static void PostprocessRegion(SlidingMesh mesh, ConceptualMesh cmesh, MeshRegionMetadata md, ISizeFunction sizeFunction, ProgressReporter reporter = null)
	{
		MeshProfiler.Instance.StartTask(MeshProfiler.MeshTask.PostprocessRegion);
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		mesh.SetMultipointFaces(md.Multipoint);
		if (md.RegionRecipe != null)
		{
			int count = md.RegionRecipe.Steps.Count;
			float scaledCount = 1f / (float)count;
			for (int i = 0; i < count; i++)
			{
				md.RegionRecipe.Steps[i].Apply(mesh, md, sizeFunction, reporter.WithProgressModifier((int)((float)i * scaledCount * 100f), scaledCount));
				if (reporter.CancelRequested)
				{
					return;
				}
			}
		}
		mesh.SetMultipointFaces(md.Multipoint);
		MeshProfiler.Instance.StopTask(MeshProfiler.MeshTask.PostprocessRegion);
	}

	private static OperationResult PostprocessGlobal(ref SlidingMesh mesh, ConceptualMesh cmesh, ISizeFunction sizeFunction, ProgressReporter reporter = null)
	{
		MeshProfiler.Instance.StartTask(MeshProfiler.MeshTask.PostprocessGlobal);
		OperationResult operationResult = new OperationResult();
		try
		{
			reporter.ReportMessage("Repairing conflicts");
			RepairMultipointConflicts.Process(mesh, cmesh, sizeFunction, operationResult);
		}
		catch (StructuredException ex)
		{
			operationResult.AddMessages(ex.Messages);
			operationResult.AddFailure(new Message("Error running the global post-processor."));
		}
		catch (ColinearTriangleException ex2)
		{
			operationResult.AddFailure(ex2.Message);
			operationResult.AddFailure(new Message("Examine geometry errors near {Coordinate} - {Coordinate} - {Coordinate}", ex2.PointA, ex2.PointB, ex2.PointC));
			operationResult.AddFailure(new Message("Error running the global post-processor."));
		}
		catch (Exception ex3)
		{
			_logger.LogWarning(ex3, "Mesh post-processing error for global exact arcs.");
			operationResult.AddFailure(GeneralMessageCodes.GetExceptionOccurred("Post-processing global exact arcs", ex3));
		}
		string input = "\r\n#MergeFaces(collapse=true,merge=true,threshold=0.3, count=2)\r\n#Merge(threshold=0.6)\r\n#Smooth(count=5)";
		try
		{
			if (TopologyRecipe.TryParse(input, out var recipe, true, null))
			{
				reporter.ReportMessage("Global postprocessing");
				foreach (TopologyStep step in recipe.Steps)
				{
					step.Apply(mesh, MeshRegionMetadata.NullMetadata(), sizeFunction, reporter);
				}
			}
			else
			{
				operationResult.AddFailure(new Message("Unable to parse global recipe."));
			}
		}
		catch (Exception ex4)
		{
			_logger.LogWarning(ex4, "Mesh post-processing error for global mesh.");
			operationResult.AddFailure(GeneralMessageCodes.GetExceptionOccurred("Post-processing global mesh.", ex4));
		}
		try
		{
			bool condition = false;
			for (int i = 0; i < mesh.CellCount; i++)
			{
				if (mesh.CellDeleted[i])
				{
					condition = true;
					break;
				}
			}
			if (!condition)
			{
				for (int j = 0; j < mesh.FaceCount; j++)
				{
					if (mesh.FaceDeleted[j])
					{
						condition = true;
						break;
					}
				}
			}
			if (!condition)
			{
				for (int k = 0; k < mesh.NodeCount; k++)
				{
					if (mesh.NodeDeleted[k])
					{
						condition = true;
						break;
					}
				}
			}
			if (condition)
			{
				SlidingMesh slidingMesh = new SlidingMesh();
				slidingMesh.MergeMesh(mesh);
				mesh = slidingMesh;
			}
		}
		catch (Exception ex5)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetExceptionOccurred("Merging/reindexing mesh", ex5));
		}
		mesh.EnforcePerimeterFaceInvariant();
		MeshProfiler.Instance.StopTask(MeshProfiler.MeshTask.PostprocessGlobal);
		return operationResult;
	}

	public static bool IsUserDefinedColumn(string columnName, IReadOnlyList<TableDefault> schema)
	{
		if (string.IsNullOrWhiteSpace(columnName))
		{
			return false;
		}
		foreach (TableDefault schema2 in schema)
		{
			if (string.Equals(schema2.Name, columnName, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}
		return true;
	}

	public static bool IsUserDefinedArcColumn(string columnName)
	{
		return IsUserDefinedColumn(columnName, ArcDefaults);
	}

	public static bool IsUserDefinedControlPointColumn(string columnName)
	{
		return IsUserDefinedColumn(columnName, ControlPointDefaults);
	}

	public static void EnsureMeshMetadataColumns(this Topology topo)
	{
		topo.HealAssignControlPointMetadata(topo.ControlPointMetadata);
		topo.GlobalMetadata.EnsureDefaultColumns(GlobalMetadataDefaults);
		topo.HealAssignArcMetadata(topo.ArcMetadata);
	}

	public static Topology CloneWithMeshGenerationHooks(this Topology topology, Topology cloneInto = null)
	{
		Topology topology2 = topology.Clone(cloneInto);
		topology2.EnsureMeshMetadataColumns();
		return topology2;
	}

	public static void AssignTableRowDefaults(TableRow tr, IReadOnlyList<TableDefault> defaults)
	{
		foreach (TableDefault @default in defaults)
		{
			tr[@default.Name] = @default.DefaultValue;
		}
	}

	public static bool IsDefaultRow(TableRow tr, IReadOnlyList<TableDefault> defaults)
	{
		foreach (TableDefault @default in defaults)
		{
			if (!tr.Table.TryGetColumnIndex(@default.Name, out var idx))
			{
				return false;
			}
			if (!tr[idx].Equals(@default.DefaultValue))
			{
				return false;
			}
		}
		return true;
	}

	public static void EnsureDefaultColumns(this Table table, IReadOnlyList<TableDefault> defaults)
	{
		foreach (TableDefault @default in defaults)
		{
			table.TryEnsureColumn(@default.Name, @default.Type, DuplicateColumnBehavior.Replace, out var _);
		}
	}

	public static void ApplyDefaultsToNullValues(Table table, IReadOnlyList<TableDefault> defaults)
	{
		for (int i = 0; i < table.Rows.Count; i++)
		{
			TableRow tableRow = table.Rows[i];
			foreach (TableDefault @default in defaults)
			{
				if (tableRow[@default.Name] == null)
				{
					tableRow[@default.Name] = @default.DefaultValue;
				}
			}
		}
	}

	public static void RestoreSchemaEnumColumns(this Table table, IReadOnlyList<TableDefault> schema)
	{
		foreach (TableDefault schema2 in schema)
		{
			if (!schema2.Type.IsEnum || !table.TryGetColumnIndex(schema2.Name, out var idx) || table.Columns[idx].Type != typeof(int))
			{
				continue;
			}
			table.ChangeIntColumnToEnum(schema2.Name, schema2.Type);
			for (int i = 0; i < table.Rows.Count; i++)
			{
				object row = table.Rows[i][idx];
				if (row == null || !Enum.IsDefined(schema2.Type, row))
				{
					table.Rows[i][idx] = schema2.DefaultValue;
				}
			}
		}
	}

	private static void MigrateIntColumnToEnumColumn<TEnum>(Table table, string columnName) where TEnum : struct, Enum
	{
		int columnIndex = table.GetColumnIndex(columnName);
		if (columnIndex != -1 && table.Columns[columnIndex].Type == typeof(int))
		{
			table.ChangeIntColumnToEnum<TEnum>(columnName);
		}
	}

	private static void MigrateBoolColumnToConstrainStretchingEnum(Table table, string columnName)
	{
		int columnIndex = table.GetColumnIndex(columnName);
		if (columnIndex == -1)
		{
			return;
		}
		TableColumn tableColumn = table.Columns[columnIndex];
		if (tableColumn.Type != typeof(bool) && tableColumn.Type != typeof(bool?))
		{
			return;
		}
		table.ChangeBoolColumnToEnum(columnName, (bool? boolValue) =>
		{
			if (!boolValue.HasValue)
			{
				return ConstrainStretchingOptions.Default;
			}
			return (!boolValue.Value) ? ConstrainStretchingOptions.Constrained : ConstrainStretchingOptions.Floating;
		});
	}

	private static void MigrateLaplaceBCColumn(Table table)
	{
		int idx = table.GetColumnIndex("LaplaceBC");
		if (idx >= 0)
		{
			Type type = table.Columns[idx].Type;
			if (type == typeof(int))
			{
				table.ChangeIntColumnToEnum<LaplaceBCOptions>("LaplaceBC");
			}
			else if (type == typeof(string))
			{
				table.ChangeStringColumnToEnum("LaplaceBC", (string stringValue) => Enum.TryParse<LaplaceBCOptions>(stringValue, ignoreCase: true, out var result) ? result : LaplaceBCOptions.Default);
			}
			return;
		}
		table.TryEnsureColumn(LaplaceBCColumn.Name, LaplaceBCColumn.Type, DuplicateColumnBehavior.Replace, out idx);
		bool columnIndex = table.TryGetColumnIndex("ConstrainStretching", out var idx2);
		for (int intervalIndex = 0; intervalIndex < table.Rows.Count; intervalIndex++)
		{
			ConstrainStretchingOptions constrainStretchingOptions = (columnIndex ? (table.Rows[intervalIndex][idx2] as ConstrainStretchingOptions?).GetValueOrDefault() : ConstrainStretchingOptions.Default);
			TableRow tableRow = table.Rows[intervalIndex];
			int index = idx;
			tableRow[index] = constrainStretchingOptions switch
			{
				ConstrainStretchingOptions.Constrained => LaplaceBCOptions.Dirichlet, 
				ConstrainStretchingOptions.Floating => LaplaceBCOptions.ZeroFlux, 
				_ => LaplaceBCOptions.Default, 
			};
		}
	}

	public static void HealAssignArcMetadata(this Topology topo, Table t)
	{
		MigrateIntColumnToEnumColumn<LinearSizeFunction>(t, ArcSizeFunctionColumn);
		MigrateBoolColumnToConstrainStretchingEnum(t, "ConstrainStretching");
		MigrateIntColumnToEnumColumn<ConstrainStretchingOptions>(t, "ConstrainStretching");
		MigrateLaplaceBCColumn(t);
		t.EnsureDefaultColumns(ArcDefaults);
		InitTable(t, topo.Arcs.Count, ArcDefaults, "Arc");
		ApplyDefaultsToNullValues(t, ArcDefaults);
		topo.SetArcMetadataTable(t);
	}

	private static void InitTable(Table t, int expectedRowCount, IReadOnlyList<TableDefault> defaultRow, string elementType)
	{
		t.InitValueTypesToDefault = true;
		t.OnNewRow = (TableRow tr) =>
		{
			AssignTableRowDefaults(tr, defaultRow);
		};
		t.IsDefault = (TableRow tr) => IsDefaultRow(tr, defaultRow);
		if (t.Rows.Count < expectedRowCount)
		{
			_logger.LogWarning("Incorrect row count for '{ElementType}', appending. {ExpectedRowCount} vs {ActualRowCount}", elementType, expectedRowCount, t.Rows.Count);
			for (int rowCount = t.Rows.Count; rowCount < expectedRowCount; rowCount++)
			{
				t.AddNewRow();
			}
		}
		else if (t.Rows.Count > expectedRowCount)
		{
			_logger.LogWarning("Incorrect row count for '{ElementType}', trimming. {ExpectedRowCount} vs {ActualRowCount}", elementType, expectedRowCount, t.Rows.Count);
			t.RemoveRowRange(expectedRowCount, t.Rows.Count - expectedRowCount);
		}
	}

	public static void HealAssignControlPointMetadata(this Topology topo, Table t)
	{
		MigrateIntColumnToEnumColumn<SizeFunction>(t, ControlPointSizeFunctionColumn);
		MigrateIntColumnToEnumColumn<PatchType>(t, PatchTypeColumn);
		t.EnsureDefaultColumns(ControlPointDefaults);
		InitTable(t, topo.RegionControlPoints.Count, ControlPointDefaults, "Control Point");
		ApplyDefaultsToNullValues(t, ControlPointDefaults);
		topo.SetControlPointMetadataTable(t);
	}

	public static bool IsWellKnownColumn(string columnName)
	{
		if (_wellKnownColumnNames == null)
		{
			_wellKnownColumnNames = new HashSet<string>();
			foreach (TableDefault arcDefault in ArcDefaults)
			{
				_wellKnownColumnNames.Add(arcDefault.Name);
			}
			foreach (TableDefault controlPointDefault in ControlPointDefaults)
			{
				_wellKnownColumnNames.Add(controlPointDefault.Name);
			}
			foreach (TableDefault globalMetadataDefault in GlobalMetadataDefaults)
			{
				_wellKnownColumnNames.Add(globalMetadataDefault.Name);
			}
		}
		return _wellKnownColumnNames.Contains(columnName);
	}

	public static void SetGlobalData(this Topology topo, PatchType patchType, SizeFunction sf, float cellSize, float stretchRate = -1f, float multipointPrecision = 0f, float sizeTolerance = 1f)
	{
		TableRow tableRow = topo.GlobalMetadata[0];
		tableRow[PatchTypeColumn] = patchType;
		tableRow[ControlPointSizeFunctionColumn] = sf;
		tableRow[ConstantSizeColumn] = cellSize;
		tableRow[SecondaryConstantSizeColumn] = cellSize;
		tableRow[StretchRateColumn] = ((stretchRate >= 0f) ? stretchRate : float.NaN);
		tableRow[MultipointColumn] = Math.Max(multipointPrecision, 0f);
		tableRow[SizeToleranceColumn] = sizeTolerance;
	}

	public static void SetControlPointData(this Topology topo, int ctrlPtIdx, PatchType pt, SizeFunction sf, float cellSize, float stretchRate = -1f, float secondaryCellSize = -1f, float rotationDegrees = float.NaN, string recipeSource = null, float multipointPrecision = -1f, float sizeTolerance = -1f)
	{
		if (float.IsNaN(stretchRate) || stretchRate < 0f)
		{
			stretchRate = float.NaN;
		}
		if (secondaryCellSize < 0f)
		{
			secondaryCellSize = cellSize;
		}
		if (float.IsNaN(rotationDegrees))
		{
			rotationDegrees = 0f;
		}
		if (recipeSource == null)
		{
			recipeSource = "Default()";
		}
		bool multipoint = true;
		if (multipointPrecision >= 0f)
		{
			multipoint = multipointPrecision > 0f;
		}
		if (sizeTolerance < 0f)
		{
			sizeTolerance = 1f;
		}
		MeshRegionMetadata meshRegionMetadata = new MeshRegionMetadata(pt, sf, cellSize, stretchRate);
		meshRegionMetadata.UserSecondaryConstantSize = secondaryCellSize;
		meshRegionMetadata.RotationDegrees = rotationDegrees;
		meshRegionMetadata.Multipoint = multipoint;
		meshRegionMetadata.SizeTolerance = sizeTolerance;
		MeshRegionMetadata md = meshRegionMetadata;
		md.RegionRecipe.SetSource(recipeSource);
		topo.SetControlPointData(ctrlPtIdx, md);
	}

	public static void SetControlPointData(this Topology topo, int ctrlPtIdx, MeshRegionMetadata md)
	{
		TableRow tableRow = topo.ControlPointMetadata[ctrlPtIdx];
		tableRow[PatchTypeColumn] = md.PatchType;
		tableRow[ControlPointSizeFunctionColumn] = md.SizeFunction;
		tableRow[ConstantSizeColumn] = md.UserConstantSize;
		tableRow[SecondaryConstantSizeColumn] = md.UserSecondaryConstantSize;
		tableRow[RotationDegreesColumn] = md.RotationDegrees;
		tableRow[RecipeColumn] = md.RegionRecipe?.Source;
		tableRow[StretchRateColumn] = md.StretchRate;
		tableRow[MultipointColumn] = (md.Multipoint ? 1f : 0f);
		tableRow[TinResolutionColumn] = md.TINResolution;
		tableRow[SizeToleranceColumn] = md.SizeTolerance;
	}

	public static void SetArcData(this Topology topo, int arcIdx, LinearSizeFunction sf, float constSz, int count, float bias)
	{
		topo.SetArcData(arcIdx, sf, constSz, count, bias, ConstrainStretchingOptions.Default, distanceStraightLine: true, 0f, 0.1f, followExactly: true);
	}

	public static void SetArcData(this Topology topo, int arcIdx, LinearSizeFunction sf, float constSz, int count, float bias, bool constrainStretching)
	{
		topo.SetArcData(arcIdx, sf, constSz, count, bias, constrainStretching ? ConstrainStretchingOptions.Constrained : ConstrainStretchingOptions.Floating, distanceStraightLine: true, 0f, 0.1f, followExactly: true);
	}

	public static void SetArcData(this Topology topo, int arcIdx, LinearSizeFunction sf, float constSz, int count, float bias, ConstrainStretchingOptions constrainStretching)
	{
		topo.SetArcData(arcIdx, sf, constSz, count, bias, constrainStretching, distanceStraightLine: true, 0f, 0.1f, followExactly: true);
	}

	public static void SetArcData(this Topology topo, int arcIdx, LinearSizeFunction sf, float constSz, int count, float bias, ConstrainStretchingOptions constrainStretching, bool distanceStraightLine, float areaWeight, float discretizeMagnetism)
	{
		topo.SetArcData(arcIdx, sf, constSz, count, bias, constrainStretching, distanceStraightLine, areaWeight, discretizeMagnetism, followExactly: true);
	}

	public static void SetArcData(this Topology topo, int arcIdx, LinearSizeFunction sf, float constSz, int count, float bias, bool constrainStretching, bool distanceStraightLine, float areaWeight, float discretizeMagnetism)
	{
		topo.SetArcData(arcIdx, sf, constSz, count, bias, (!constrainStretching) ? ConstrainStretchingOptions.Constrained : ConstrainStretchingOptions.Floating, distanceStraightLine, areaWeight, discretizeMagnetism, followExactly: true);
	}

	public static void SetArcData(this Topology topo, int arcIdx, LinearSizeFunction sf, float constSz, int count, float bias, bool constrainStretching, bool distanceStraightLine, float areaWeight, float discretizeMagnetism, bool followExactly)
	{
		topo.SetArcData(arcIdx, sf, constSz, count, bias, (!constrainStretching) ? ConstrainStretchingOptions.Constrained : ConstrainStretchingOptions.Floating, distanceStraightLine, areaWeight, discretizeMagnetism, followExactly);
	}

	public static void SetArcAngles(this Topology topo, int idx, float left, float right)
	{
		TableRow tableRow = topo.ArcMetadata[idx];
		tableRow[ArcAngleLeftColumn] = left;
		tableRow[ArcAngleRightColumn] = right;
	}

	public static void SetArcData(this Topology topo, int arcIdx, LinearSizeFunction sf, float constSz, int count, float bias, ConstrainStretchingOptions constrainStretching, bool distanceStraightLine, float areaWeight, float discretizeMagnetism, bool followExactly)
	{
		topo.SetArcData(arcIdx, sf, constSz, count, bias, constrainStretching, LaplaceBCOptions.Default, distanceStraightLine, areaWeight, discretizeMagnetism, followExactly);
	}

	public static void SetArcData(this Topology topo, int arcIdx, LinearSizeFunction sf, float constSz, int count, float bias, ConstrainStretchingOptions constrainStretching, LaplaceBCOptions laplaceBC, bool distanceStraightLine, float areaWeight, float discretizeMagnetism, bool followExactly)
	{
		if (sf == LinearSizeFunction.Constant && float.IsNaN(constSz))
		{
			sf = LinearSizeFunction.Unset;
		}
		TableRow tableRow = topo.ArcMetadata[arcIdx];
		tableRow[ArcSizeFunctionColumn] = sf;
		tableRow[ConstantSizeColumn] = constSz;
		tableRow[ArcPointCountColumn] = count;
		tableRow[ArcBiasColumn] = bias;
		tableRow[ConstrainStretchingColumn] = constrainStretching;
		tableRow[LaplaceBCColumn] = laplaceBC;
		tableRow[DistanceStraightLineColumn] = distanceStraightLine;
		tableRow[AreaWeightColumn] = areaWeight;
		tableRow[DiscretizeMagnetismColumn] = discretizeMagnetism;
		tableRow[FollowExactlyColumn] = followExactly;
	}

	public static void SetArcData(this Topology topo, int arcIdx, ArcMetadata metaData)
	{
		TableRow tableRow = topo.ArcMetadata[arcIdx];
		tableRow[ArcSizeFunctionColumn] = metaData.SizeFunction;
		tableRow[ConstantSizeColumn] = metaData.ConstantSize;
		tableRow[ArcPointCountColumn] = metaData.Count;
		tableRow[ArcBiasColumn] = metaData.Bias;
		tableRow[ConstrainStretchingColumn] = metaData.ConstrainStretching;
		tableRow[LaplaceBCColumn] = metaData.LaplaceBC;
		tableRow[DistanceStraightLineColumn] = metaData.DistanceStraightLine;
		tableRow[AreaWeightColumn] = metaData.AreaWeight;
		tableRow[DiscretizeMagnetismColumn] = metaData.DiscretizeMagnetism;
		tableRow[FollowExactlyColumn] = metaData.FollowExactly;
		tableRow[ArcAngleLeftColumn] = metaData.ArcAngleLeft;
		tableRow[ArcAngleRightColumn] = metaData.ArcAngleRight;
	}

	public static void SetArcConstantSize(this Topology topo, int arcIdx, float constSz)
	{
		TableRow tableRow = topo.ArcMetadata[arcIdx];
		tableRow[ArcSizeFunctionColumn] = LinearSizeFunction.Constant;
		tableRow[ConstantSizeColumn] = constSz;
		tableRow[ArcBiasColumn] = 1f;
	}

	public static bool HasArcMeshgenData(this Topology topo)
	{
		if (topo.ArcMetadata.GetColumnIndex(ArcSizeFunctionColumn) >= 0 && topo.ArcMetadata.GetColumnIndex(ConstantSizeColumn) >= 0 && topo.ArcMetadata.GetColumnIndex(ArcPointCountColumn) >= 0 && topo.ArcMetadata.GetColumnIndex(ArcBiasColumn) >= 0 && topo.ArcMetadata.GetColumnIndex(ConstrainStretchingColumn) >= 0 && topo.ArcMetadata.GetColumnIndex(LaplaceBCColumn) >= 0 && topo.ArcMetadata.GetColumnIndex(DistanceStraightLineColumn) >= 0 && topo.ArcMetadata.GetColumnIndex(AreaWeightColumn) >= 0 && topo.ArcMetadata.GetColumnIndex(DiscretizeMagnetismColumn) >= 0)
		{
			return topo.ArcMetadata.GetColumnIndex(FollowExactlyColumn) >= 0;
		}
		return false;
	}

	public static ArcMetadata[] GetAllArcMetadataCloned(this Topology topo)
	{
		int count = topo.Arcs.Count;
		ArcMetadata[] arcMetadatas = new ArcMetadata[count];
		for (int i = 0; i < count; i++)
		{
			arcMetadatas[i] = topo.GetArcData(i);
		}
		return arcMetadatas;
	}

	public static ArcMetadata GetArcMetadata(this Topology topo, int arcIdx)
	{
		return topo.GetArcData(arcIdx);
	}

	public static ArcMetadata GetArcData(this Topology topo, int arcIdx)
	{
		TableRow tableRow = topo.ArcMetadata[arcIdx];
		return new ArcMetadata
		{
			SizeFunction = (tableRow[ArcSizeFunctionColumn] as LinearSizeFunction?).GetValueOrDefault(),
			ConstantSize = ((tableRow[ConstantSizeColumn] as float?) ?? float.NaN),
			Count = (tableRow[ArcPointCountColumn] as int?).GetValueOrDefault(),
			Bias = ((tableRow[ArcBiasColumn] as float?) ?? 1f),
			ConstrainStretching = (tableRow[ConstrainStretchingColumn] as ConstrainStretchingOptions?).GetValueOrDefault(),
			LaplaceBC = (tableRow[LaplaceBCColumn] as LaplaceBCOptions?).GetValueOrDefault(),
			DistanceStraightLine = ((tableRow[DistanceStraightLineColumn] as bool?) ?? true),
			AreaWeight = (tableRow[AreaWeightColumn] as float?).GetValueOrDefault(),
			DiscretizeMagnetism = ((tableRow[DiscretizeMagnetismColumn] as float?) ?? 0.1f),
			FollowExactly = ((tableRow[FollowExactlyColumn] as bool?) ?? true),
			ArcAngleLeft = (tableRow[ArcAngleLeftColumn] as float?).GetValueOrDefault(),
			ArcAngleRight = (tableRow[ArcAngleRightColumn] as float?).GetValueOrDefault()
		};
	}

	public static ArcMetadata GetDefaultArcData()
	{
		return new ArcMetadata
		{
			SizeFunction = LinearSizeFunction.Constant,
			ConstantSize = ArcMetadata.DefaultConstantSize,
			Count = 8,
			Bias = 1f,
			ConstrainStretching = ConstrainStretchingOptions.Default,
			LaplaceBC = LaplaceBCOptions.Default,
			DistanceStraightLine = true,
			AreaWeight = 0f,
			DiscretizeMagnetism = 0.1f,
			FollowExactly = false,
			ArcAngleLeft = 0f,
			ArcAngleRight = 0f
		};
	}

	public static MeshRegionMetadata GetGlobalData(this Topology topo)
	{
		TableRow tableRow = topo.GlobalMetadata[0];
		MeshRegionMetadata result = new MeshRegionMetadata();
		if (tableRow[PatchTypeColumn] is PatchType patchType)
		{
			result.PatchType = patchType;
		}
		if (tableRow[ControlPointSizeFunctionColumn] is SizeFunction sizeFunction)
		{
			result.SizeFunction = sizeFunction;
		}
		if (tableRow[ConstantSizeColumn] is float userConstantSize)
		{
			result.UserConstantSize = userConstantSize;
		}
		if (tableRow[SecondaryConstantSizeColumn] is float userSecondaryConstantSize)
		{
			result.UserSecondaryConstantSize = userSecondaryConstantSize;
		}
		if (tableRow[MultipointColumn] is float computed)
		{
			result.Multipoint = computed > 0f;
		}
		if (tableRow[TinResolutionColumn] is float tINResolution)
		{
			result.TINResolution = tINResolution;
		}
		return result;
	}

	public static string GetControlPointRecipeString(this Topology topo, int ctrlPtIdx)
	{
		return topo.ControlPointMetadata[ctrlPtIdx][RecipeColumn] as string;
	}

	public static void SetControlPointRecipeString(this Topology topo, int ctrlPtIdx, string recipe)
	{
		topo.ControlPointMetadata[ctrlPtIdx][RecipeColumn] = recipe;
	}

	public static void SetControlPointTINResolution(this Topology topo, int ctrlPtIdx, float tinResolution)
	{
		topo.ControlPointMetadata[ctrlPtIdx][TinResolutionColumn] = tinResolution;
	}

	public static MeshRegionMetadata GetControlPointData(this Topology topo, int ctrlPtIdx, OperationResult res = null)
	{
		TableRow tableRow = topo.ControlPointMetadata[ctrlPtIdx];
		MeshRegionMetadata result = new MeshRegionMetadata();
		if (tableRow[ConstantSizeColumn] is float userConstantSize)
		{
			result.UserConstantSize = userConstantSize;
		}
		if (tableRow[PatchTypeColumn] is PatchType patchType)
		{
			result.PatchType = patchType;
		}
		if (tableRow[ControlPointSizeFunctionColumn] is SizeFunction sizeFunction)
		{
			result.SizeFunction = sizeFunction;
		}
		if (tableRow[RecipeColumn] is string s)
		{
			result.RegionRecipe = ParseRecipe(s);
		}
		if (tableRow[SizeToleranceColumn] is float sizeTolerance)
		{
			result.SizeTolerance = sizeTolerance;
		}
		if (tableRow[SecondaryConstantSizeColumn] is float userSecondaryConstantSize)
		{
			result.UserSecondaryConstantSize = userSecondaryConstantSize;
		}
		if (tableRow[RotationDegreesColumn] is float rotationDegrees)
		{
			result.RotationDegrees = rotationDegrees;
		}
		if (tableRow[StretchRateColumn] is float stretchRate)
		{
			result.StretchRate = stretchRate;
		}
		if (tableRow[MultipointColumn] is float computed)
		{
			result.Multipoint = computed > 0f;
		}
		if (tableRow[TinResolutionColumn] is float tINResolution)
		{
			result.TINResolution = tINResolution;
		}
		return result;
	}

	public static TopologyRecipe ParseRecipe(string s, OperationResult res = null)
	{
		TopologyRecipe.TryParse(s, out var recipe, global: false, res);
		return recipe;
	}

	public static bool HasRegionMeshgenData(this Topology topo)
	{
		if (topo.ControlPointMetadata.GetColumnIndex(PatchTypeColumn) >= 0 && topo.ControlPointMetadata.GetColumnIndex(ControlPointSizeFunctionColumn) >= 0 && topo.ControlPointMetadata.GetColumnIndex(ConstantSizeColumn) >= 0)
		{
			return topo.ControlPointMetadata.GetColumnIndex(StretchRateColumn) >= 0;
		}
		return false;
	}

	public static MeshRegionMetadata GetRegionData(this Topology topo, int rgnIdx, OperationResult res = null)
	{
		int controlPointIndex = topo.CompletedRegions[rgnIdx].ControlPointIndex;
		if (controlPointIndex < 0)
		{
			return topo.GetGlobalData();
		}
		return topo.GetControlPointData(controlPointIndex, res);
	}

	public static void SetRegionData(this Topology topo, int rgnIdx, MeshRegionMetadata mrm)
	{
		int controlPointIndex = topo.CompletedRegions[rgnIdx].ControlPointIndex;
		if (controlPointIndex < 0)
		{
			throw new NotSupportedException("Cannot set mesh metadata with no underlying control point");
		}
		topo.SetControlPointData(controlPointIndex, mrm);
	}

	public static void StreamOutKnownMeshMetadata(this Topology topo, BinaryWriter bw)
	{
		bool hasArcMeshgenData = topo.HasArcMeshgenData();
		for (int i = 0; i < topo.Arcs.Count; i++)
		{
			ArcMetadata arcMetadata = ((!hasArcMeshgenData) ? GetDefaultArcData() : topo.GetArcData(i));
			bw.Write((int)arcMetadata.SizeFunction);
			bw.Write(arcMetadata.ConstantSize);
			bw.Write(arcMetadata.Count);
			bw.Write(arcMetadata.Bias);
			bw.Write((int)arcMetadata.ConstrainStretching);
			bw.Write((int)arcMetadata.LaplaceBC);
			bw.Write(arcMetadata.DistanceStraightLine);
			bw.Write(arcMetadata.AreaWeight);
			bw.Write(arcMetadata.DiscretizeMagnetism);
			bw.Write(arcMetadata.FollowExactly);
		}
		if (topo.HasRegionMeshgenData())
		{
			for (int j = 0; j < topo.CompletedRegions.Count; j++)
			{
				topo.GetRegionData(j).StreamOut(bw);
			}
			return;
		}
		MeshRegionMetadata meshRegionMetadata = MeshRegionMetadata.NullMetadata();
		for (int k = 0; k < topo.CompletedRegions.Count; k++)
		{
			meshRegionMetadata.StreamOut(bw);
		}
	}

	public static void StreamInKnownMeshMetadata(this Topology topo, BinaryReader br)
	{
		topo.EnsureMeshMetadataColumns();
		for (int i = 0; i < topo.Arcs.Count; i++)
		{
			int sf = br.ReadInt32();
			float constSz = br.ReadSingle();
			int count = br.ReadInt32();
			float bias = br.ReadSingle();
			ConstrainStretchingOptions constrainStretching = (ConstrainStretchingOptions)br.ReadInt32();
			LaplaceBCOptions laplaceBC = (LaplaceBCOptions)br.ReadInt32();
			bool distanceStraightLine = br.ReadBoolean();
			float areaWeight = br.ReadSingle();
			float discretizeMagnetism = br.ReadSingle();
			bool followExactly = br.ReadBoolean();
			topo.SetArcData(i, (LinearSizeFunction)sf, constSz, count, bias, constrainStretching, laplaceBC, distanceStraightLine, areaWeight, discretizeMagnetism, followExactly);
		}
		for (int j = 0; j < topo.CompletedRegions.Count; j++)
		{
			MeshRegionMetadata meshRegionMetadata = new MeshRegionMetadata();
			meshRegionMetadata.StreamIn(br);
			if (!MeshRegionMetadata.IsNull(meshRegionMetadata))
			{
				topo.SetRegionData(j, meshRegionMetadata);
			}
		}
	}
}
