using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geospatial.Vectors.Topology;
using Utility.Logging;
using Utility.Matrices;
using Utility.Progress;
using Utility.Systems;

namespace Geospatial.Vectors.MeshTopology;

public class InferCellSizes
{
	private struct AdjacentVar
	{
		public readonly int row;

		public readonly double weightLHS;

		public readonly double cellSize;

		public readonly double weightRHS;

		private AdjacentVar(int row, double cellSize, double weightLHS, double weightRHS)
		{
			this.row = row;
			this.weightLHS = weightLHS;
			this.weightRHS = weightRHS;
			this.cellSize = cellSize;
		}

		public static AdjacentVar Row(int row, double weight, double scale = 1.0)
		{
			return new AdjacentVar(row, double.NaN, weight, weight * scale);
		}

		public static AdjacentVar Constant(double cellSize, double weight, double scale = 1.0)
		{
			return new AdjacentVar(-1, cellSize, weight, weight * scale);
		}

		public AdjacentVar Add(double weight, double scale = 1.0)
		{
			return new AdjacentVar(row, cellSize, weightLHS + weight, weightRHS + weight * scale);
		}
	}

	private struct SizeVar
	{
		public readonly HalfEdge he;

		public readonly int cIdx;

		public readonly bool cellPrimary;

		public readonly double cellSize;

		public readonly bool isComputed;

		public List<AdjacentVar> adjacent;

		private SizeVar(double cellSize, bool isComputed, HalfEdge he, int cIdx, bool cellPrimary)
		{
			this.cellPrimary = true;
			this.isComputed = false;
			adjacent = new List<AdjacentVar>();
			this.he = he;
			this.cellSize = cellSize;
			this.cIdx = cIdx;
			this.cellPrimary = cellPrimary;
			this.isComputed = isComputed;
		}

		private SizeVar(double cellSize, bool isComputed, HalfEdge he, int cIdx, bool cellPrimary, List<AdjacentVar> adjacent)
			: this(cellSize, isComputed, he, cIdx, cellPrimary)
		{
			this.adjacent = adjacent;
		}

		public static SizeVar FaceSizeVar(double cellSize, HalfEdge he)
		{
			return new SizeVar(cellSize, double.IsNaN(cellSize), he, -1, cellPrimary: true);
		}

		public static SizeVar CellSizeVar(double cellSize, int cIdx, bool cellPrimary)
		{
			return new SizeVar(cellSize, double.IsNaN(cellSize), HalfEdge.NullHalfEdge, cIdx, cellPrimary);
		}

		public void AddWeight(int rowSource, double weight, double scale = 1.0)
		{
			for (int i = 0; i < adjacent.Count; i++)
			{
				if (adjacent[i].row == rowSource)
				{
					adjacent[i] = adjacent[i].Add(weight, scale);
					return;
				}
			}
			adjacent.Add(AdjacentVar.Row(rowSource, weight, scale));
		}

		public void AddConstant(double size, double weight, double scale = 1.0)
		{
			adjacent.Add(AdjacentVar.Constant(size, weight, scale));
		}

		public SizeVar Modify(double cellSize)
		{
			return new SizeVar(cellSize, isComputed, he, cIdx, cellPrimary, adjacent);
		}

		public override string ToString()
		{
			if (cIdx >= 0)
			{
				return $"rgn {cIdx} " + (cellPrimary ? "pri" : "sec");
			}
			return $"arc {he.fIdx}/{he.orient}";
		}

		public static string Summary(List<SizeVar> rows, int row)
		{
			string message = $"Target: row={row} {rows[row],8}  {rows[row].cellSize:F1}\n";
			foreach (AdjacentVar adjacent in rows[row].adjacent)
			{
				message += $"  row={adjacent.row,2}\t{adjacent.weightLHS:F6}*{adjacent.weightRHS:F3} {rows[adjacent.row],10} {rows[adjacent.row].cellSize:F1}\n";
			}
			return message;
		}
	}

	private enum NonquadRegionUpdate
	{
		None,
		Interpolate,
		Maximum
	}

	private class InterpolationParams
	{
		public string regionType;

		public bool readQuadSizes;

		public bool readNonquadSizes;

		public bool setQuadArcs;

		public bool setQuadRegions;

		public bool setNonquadArcs;

		public NonquadRegionUpdate nonquadRegions;

		public double weightIntraArc = 0.2;

		public double weightIntraSide = 0.2;

		public double weightOppositeSide = 1.0;

		public double weightNodeAdjacent = 0.005;

		public double weightNodeAdjacentWithRegion = 0.05;

		public double weightInterSide = 1.0;

		public double weightQuadRegion = 1.0;

		public double weightNonQuadRegion = 0.1;

		public double weightInterQuad = 10.0;
	}

	public static class MessageCodes
	{
		public static readonly MessageClass NoCellSizes = new MessageClass("InferCellSizes_NoCellSizes", MessageSeverity.Error, "No cell sizes are set. Set a cell size on at least one region or arc.");

		public static readonly MessageClass InsufficientCellSizes = new MessageClass("InferCellSizes_InsufficientCellSizes", MessageSeverity.Error, "Not enough cell size information to interpolate blank values.");

		public static readonly MessageClass InvalidInterpolatedSize = new MessageClass("InferCellSizes_InvalidInterpolatedSize", MessageSeverity.Error, "During size interpolation, arc {ArcIndex} has invalid size.\nEnsure that cell size is set on an element of the conceptual mesh that is reachable from this arc");

		public static readonly MessageClass MaxIterations = new MessageClass("InferCellSizes_MaxIterations", MessageSeverity.Warning, "Size interpolation on {regionType} pass did not fully converge after {maxIterations} iterations\nAdd cell size to more arcs and regions to improve convergence");

		public static Message GetNoCellSizes()
		{
			return NoCellSizes.Create();
		}

		public static Message GetInsufficientCellSizes()
		{
			return InsufficientCellSizes.Create();
		}

		public static Message GetInsufficientCellSizes(string regionType)
		{
			string altMessage = "During " + regionType + " pass, not enough cell size information to interpolate blank values.";
			return new Message(InsufficientCellSizes, altMessage);
		}

		public static Message GetInvalidInterpolatedArcSize(string regionType, int fIdx, double size)
		{
			string altMessage = $"Arc {{{"ArcIndex"}}} has invalid size {size}.\nEnsure that a connected, {regionType} element of the mesh has cell size set";
			return new Message(InvalidInterpolatedSize, altMessage, fIdx);
		}

		public static Message GetInvalidInterpolatedRegionSize(string regionType, int ctrlPtIdx, bool isPrimary, double size)
		{
			string isPrimaryTerm = (isPrimary ? "primary" : "secondary");
			string altMessage = $"Region {{{"ControlPointIndex"}}} has invalid size {isPrimaryTerm} {size}.\nEnsure that a connected, {regionType} element of the mesh has cell size set";
			return new Message(InvalidInterpolatedSize, altMessage, ctrlPtIdx);
		}

		public static Message GetMaxIterations(int maxIterations, double errorMax, string regionType)
		{
			return MaxIterations.Create(regionType, maxIterations);
		}
	}

	public static bool coalesceQuadNonquad = true;

	public static bool quadsUseCount = false;

	private InferCellSizes()
	{
	}

	public IEnumerable<T> Chain<T>(IEnumerable<T> A, IEnumerable<T> B)
	{
		foreach (T localValue in A)
		{
			yield return localValue;
		}
		foreach (T localValue2 in B)
		{
			yield return localValue2;
		}
	}

	public static OperationResult Apply(ConceptualMesh cmesh, ProgressReporter reporter)
	{
		return new InferCellSizes().ApplyInstance(cmesh, reporter);
	}

	private OperationResult ApplyInstance(ConceptualMesh cmesh, ProgressReporter reporter)
	{
		OperationResult operationResult = new OperationResult();
		if (!HasAnyCellSizes(cmesh))
		{
			operationResult.AddFailure(MessageCodes.GetNoCellSizes());
			return operationResult;
		}
		try
		{
			InferArcConstrainModes(cmesh);
			InterpolationParams interpolationParams = new InterpolationParams
			{
				regionType = "quad",
				setQuadArcs = true,
				setQuadRegions = true,
				setNonquadArcs = false,
				readQuadSizes = true,
				readNonquadSizes = false
			};
			OperationResult other = InferSizes(cmesh, interpolationParams, out var sizeVars, out var heToRow);
			InterpolationParams interpolationParams2 = new InterpolationParams
			{
				regionType = "non-quad",
				setQuadArcs = false,
				setNonquadArcs = true,
				nonquadRegions = NonquadRegionUpdate.Maximum,
				readQuadSizes = false,
				readNonquadSizes = true
			};
			PushRegionsToArcs(cmesh, interpolationParams2);
			OperationResult other2 = InferSizes(cmesh, interpolationParams2, out var sizeVars2, out var _);
			UpdateConceptualMeshSizes(cmesh, sizeVars, interpolationParams, sizeVars2, interpolationParams2, heToRow, operationResult);
			operationResult.MergeIn(other);
			operationResult.MergeIn(other2);
			for (int i = 0; i < cmesh.FaceCount; i++)
			{
				OverrideFreeCartesianFace(cmesh, i);
			}
		}
		catch (StructuredException ex)
		{
			operationResult.AddFailures(ex.Messages);
		}
		return operationResult;
	}

	private static bool HasAnyCellSizes(ConceptualMesh cmesh)
	{
		for (int i = 0; i < cmesh.FaceCount; i++)
		{
			if (cmesh.FaceDiscretization[i].ValidSize())
			{
				return true;
			}
		}
		for (int j = 0; j < cmesh.CellCount; j++)
		{
			MeshRegionMetadata meshRegionMetadata = cmesh.CellRegionMetadata[j];
			if (meshRegionMetadata.HasPrimaryCellSize || meshRegionMetadata.HasSecondaryCellSize)
			{
				return true;
			}
		}
		return false;
	}

	private List<PatchSide> GetQuadSides(ConceptualMesh cmesh, int cIdx)
	{
		return cmesh.PatchSides(cIdx);
	}

	private static bool OverrideFreeCartesianFace(ConceptualMesh cmesh, int fIdx)
	{
		DiscretizeLine discretizeLine = cmesh.FaceDiscretization[fIdx];
		if (discretizeLine.ConstrainMode == ConstrainStretchingOptions.Constrained)
		{
			return false;
		}
		PatchType patchType = ((cmesh.FaceCells[fIdx].A >= 0) ? cmesh.CellRegionMetadata[cmesh.FaceCells[fIdx].A].PatchType : PatchType.Empty);
		PatchType patchType2 = ((cmesh.FaceCells[fIdx].B >= 0) ? cmesh.CellRegionMetadata[cmesh.FaceCells[fIdx].B].PatchType : PatchType.Empty);
		int unity = -1;
		if (patchType == PatchType.Cartesian && patchType2 == PatchType.Empty)
		{
			unity = cmesh.FaceCells[fIdx].A;
		}
		else
		{
			if (patchType2 != PatchType.Cartesian || patchType != PatchType.Empty)
			{
				return false;
			}
			unity = cmesh.FaceCells[fIdx].B;
		}
		MeshRegionMetadata meshRegionMetadata = cmesh.CellRegionMetadata[unity];
		if (float.IsNaN(meshRegionMetadata.UserConstantSize))
		{
			return false;
		}
		discretizeLine.count = 0;
		discretizeLine.bias = 1.0;
		discretizeLine.lengthScale = Math.Sqrt(meshRegionMetadata.ActualPrimaryCellSize * meshRegionMetadata.ActualSecondaryConstantSize);
		return true;
	}

	public void InferArcConstrainModes(ConceptualMesh cmesh)
	{
		for (int i = 0; i < 2; i++)
		{
			ConstrainStretchingOptions constrainStretchingOptions = ((i == 0) ? ConstrainStretchingOptions.Constrained : ConstrainStretchingOptions.Floating);
			LaplaceBCOptions laplaceBCMode = ((i != 0) ? LaplaceBCOptions.ZeroFlux : LaplaceBCOptions.Dirichlet);
			for (int j = 0; j < cmesh.CellCount; j++)
			{
				if (cmesh.CellRegionMetadata[j].PatchType != PatchType.Quad)
				{
					continue;
				}
				List<PatchSide> quadSides = GetQuadSides(cmesh, j);
				for (int k = 1 - i; k < quadSides.Count; k += 2)
				{
					foreach (HalfEdge sidePiece in quadSides[k].SidePieces)
					{
						DiscretizeLine discretizeLine = cmesh.FaceDiscretization[sidePiece.fIdx];
						if (discretizeLine.ConstrainMode == ConstrainStretchingOptions.Default)
						{
							discretizeLine.ConstrainMode = ((discretizeLine.Mode == SizeFunctionMode.Unset) ? ConstrainStretchingOptions.Floating : constrainStretchingOptions);
						}
						if (discretizeLine.LaplaceBCMode == LaplaceBCOptions.Default)
						{
							discretizeLine.LaplaceBCMode = laplaceBCMode;
						}
					}
				}
			}
		}
		for (int l = 0; l < cmesh.FaceCount; l++)
		{
			DiscretizeLine discretizeLine2 = cmesh.FaceDiscretization[l];
			if (discretizeLine2.ConstrainMode == ConstrainStretchingOptions.Default)
			{
				discretizeLine2.ConstrainMode = ((discretizeLine2.Mode != SizeFunctionMode.Unset) ? ConstrainStretchingOptions.Constrained : ConstrainStretchingOptions.Floating);
			}
		}
	}

	private bool IsQuadArc(ConceptualMesh cmesh, int fIdx)
	{
		if (fIdx >= 0)
		{
			if (cmesh.FaceCells[fIdx].A < 0 || cmesh.CellRegionMetadata[cmesh.FaceCells[fIdx].A].PatchType != PatchType.Quad)
			{
				if (cmesh.FaceCells[fIdx].B >= 0)
				{
					return cmesh.CellRegionMetadata[cmesh.FaceCells[fIdx].B].PatchType == PatchType.Quad;
				}
				return false;
			}
			return true;
		}
		return false;
	}

	private bool IsNonEmptyArc(ConceptualMesh cmesh, int fIdx)
	{
		if (fIdx >= 0)
		{
			if (cmesh.FaceCells[fIdx].A < 0 || cmesh.CellRegionMetadata[cmesh.FaceCells[fIdx].A].PatchType == PatchType.Empty)
			{
				if (cmesh.FaceCells[fIdx].B >= 0)
				{
					return cmesh.CellRegionMetadata[cmesh.FaceCells[fIdx].B].PatchType != PatchType.Empty;
				}
				return false;
			}
			return true;
		}
		return false;
	}

	private void PushRegionsToArcs(ConceptualMesh cmesh, InterpolationParams interpParams)
	{
		foreach (int cellIterator in cmesh.CellIterator())
		{
			MeshRegionMetadata meshRegionMetadata = cmesh.CellRegionMetadata[cellIterator];
			if (!meshRegionMetadata.HasPrimaryCellSize || meshRegionMetadata.PatchType == PatchType.Empty || meshRegionMetadata.PatchType == PatchType.OneD || (meshRegionMetadata.PatchType == PatchType.Quad && !interpParams.readQuadSizes) || ((meshRegionMetadata.PatchType == PatchType.TrianglePaver || meshRegionMetadata.PatchType == PatchType.Cartesian) && !interpParams.readNonquadSizes))
			{
				continue;
			}
			if (meshRegionMetadata.PatchType == PatchType.Quad)
			{
				throw new NotImplementedException("A priori pushing quad region size to blank arcs is not implemented");
			}
			foreach (HalfEdge cellFace in cmesh.CellFaces[cellIterator])
			{
				if (cmesh.HalfEdgeToOppositeCell(cellFace) < 0)
				{
					DiscretizeLine discretizeLine = cmesh.FaceDiscretization[cellFace.fIdx];
					if (discretizeLine.Mode == SizeFunctionMode.Unset)
					{
						discretizeLine.SetSizes(meshRegionMetadata.ActualPrimaryCellSize, meshRegionMetadata.ActualPrimaryCellSize, 10f * meshRegionMetadata.ActualPrimaryCellSize);
						discretizeLine.ConstrainMode = ConstrainStretchingOptions.Floating;
					}
				}
			}
		}
	}

	private OperationResult InferSizes(ConceptualMesh cmesh, InterpolationParams interpParams, out List<SizeVar> sizeVars, out Dictionary<HalfEdge, int> heToRow)
	{
		OperationResult operationResult = new OperationResult();
		sizeVars = new List<SizeVar>();
		heToRow = new Dictionary<HalfEdge, int>();
		Dictionary<(int, bool), int> intByValueTuple = new Dictionary<(int, bool), int>();
		List<SizeVar> localSizeVars = sizeVars;
		int ordinal = 0;
		for (int i = 0; i < cmesh.FaceCount; i++)
		{
			bool isQuadArc = IsQuadArc(cmesh, i);
			bool readQuadSizes = (isQuadArc ? interpParams.readQuadSizes : interpParams.readNonquadSizes);
			bool setQuadArcs = (isQuadArc ? interpParams.setQuadArcs : interpParams.setNonquadArcs);
			DiscretizeLine discretizeLine = cmesh.FaceDiscretization[i];
			double startSize;
			double endSize;
			if (discretizeLine.ValidSize() & readQuadSizes)
			{
				discretizeLine.StartEndSize(out startSize, out endSize, cmesh.FaceSmoothLine[i]);
			}
			else
			{
				startSize = (endSize = double.NaN);
				if (coalesceQuadNonquad | setQuadArcs)
				{
					ordinal += 2;
				}
			}
			SizeVar faceSizeVar = SizeVar.FaceSizeVar(startSize, new HalfEdge(i, 0));
			SizeVar faceSizeVar2 = SizeVar.FaceSizeVar(endSize, new HalfEdge(i, 1));
			int count = sizeVars.Count;
			int heToRow2 = count + 1;
			heToRow[faceSizeVar.he] = count;
			heToRow[faceSizeVar2.he] = heToRow2;
			double weight = interpParams.weightIntraArc / cmesh.FaceSmoothLine[i].Length;
			sizeVars.Add(faceSizeVar);
			sizeVars.Add(faceSizeVar2);
			AddSymWeights(count, heToRow2, weight);
		}
		foreach (int cellIterator in cmesh.CellIterator())
		{
			MeshRegionMetadata meshRegionMetadata = cmesh.CellRegionMetadata[cellIterator];
			if (meshRegionMetadata.PatchType == PatchType.Quad)
			{
				SizeVar sizeVar = SizeVar.CellSizeVar(interpParams.readQuadSizes ? ((double)meshRegionMetadata.UserConstantSize) : double.NaN, cellIterator, cellPrimary: true);
				SizeVar sizeVar2 = SizeVar.CellSizeVar(interpParams.readQuadSizes ? ((double)meshRegionMetadata.UserSecondaryConstantSize) : double.NaN, cellIterator, cellPrimary: false);
				int count2 = sizeVars.Count;
				intByValueTuple[cellRowKey(sizeVar)] = count2;
				sizeVars.Add(sizeVar);
				int count3 = sizeVars.Count;
				intByValueTuple[cellRowKey(sizeVar2)] = count3;
				sizeVars.Add(sizeVar2);
				if (coalesceQuadNonquad || interpParams.setQuadRegions)
				{
					if (!meshRegionMetadata.HasPrimaryCellSize)
					{
						ordinal++;
					}
					if (!meshRegionMetadata.HasSecondaryCellSize)
					{
						ordinal++;
					}
				}
				List<PatchSide> quadSides = GetQuadSides(cmesh, cellIterator);
				for (int j = 0; j < 4; j++)
				{
					PatchSide patchSide = quadSides[j];
					PatchSide patchSide2 = quadSides[(j + 2) % 4];
					double weight2;
					for (int k = 0; k < patchSide.SidePieces.Count - 1; k++)
					{
						HalfEdge key = patchSide.SidePieces[k].Flipped();
						HalfEdge key2 = patchSide.SidePieces[k + 1];
						int rowA = heToRow[key];
						int rowB = heToRow[key2];
						weight2 = interpParams.weightIntraSide / (cmesh.FaceSmoothLine[key.fIdx].Length + cmesh.FaceSmoothLine[key2.fIdx].Length);
						AddSymWeights(rowA, rowB, weight2);
					}
					int rowB2 = ((j % 2 == 0) ? count2 : count3);
					weight2 = interpParams.weightQuadRegion / patchSide.Smooth.Length;
					double sidePiecesPatchSideTerm;
					if (quadsUseCount)
					{
						sidePiecesPatchSideTerm = ((patchSide.SidePieces.Count != 1 || patchSide2.SidePieces.Count != 1) ? double.NaN : (2.0 * patchSide.Smooth.Length / (patchSide.Smooth.Length + patchSide2.Smooth.Length)));
					}
					else
					{
						sidePiecesPatchSideTerm = 1.0;
					}
					if (double.IsFinite(sidePiecesPatchSideTerm))
					{
						for (int l = 0; l < patchSide.SidePieces.Count; l++)
						{
							int rowA2 = heToRow[patchSide.SidePieces[l]];
							int rowA3 = heToRow[patchSide.SidePieces[l].Flipped()];
							AddSymWeights(rowA2, rowB2, weight2, sidePiecesPatchSideTerm);
							AddSymWeights(rowA3, rowB2, weight2, sidePiecesPatchSideTerm);
						}
					}
				}
				for (int m = 0; m < 4; m++)
				{
					int index = (m + 1) % 4;
					int index2 = (m + 2) % 4;
					HalfEdge key3 = quadSides[m].SidePieces.Last().Flipped();
					HalfEdge key4 = quadSides[index2].SidePieces.First();
					if (!quadsUseCount)
					{
						double weight3 = interpParams.weightOppositeSide / quadSides[index].Smooth.Length;
						int rowA4 = heToRow[key3];
						int rowB3 = heToRow[key4];
						AddSymWeights(rowA4, rowB3, weight3);
					}
					else if (quadSides[m].SidePieces.Count == 1 && quadSides[index2].SidePieces.Count == 1)
					{
						double weight4 = interpParams.weightOppositeSide / quadSides[index].Smooth.Length;
						double scale = quadSides[m].Smooth.Length / quadSides[index2].Smooth.Length;
						int rowA5 = heToRow[key3];
						int rowB4 = heToRow[key4];
						AddSymWeights(rowA5, rowB4, weight4, scale);
					}
				}
				for (int n = 0; n < 4; n++)
				{
					PatchSide patchSide3 = quadSides[n];
					int halfEdgeToOppositeCellTerm = -1;
					for (int intervalIndex = 0; intervalIndex < patchSide3.SidePieces.Count; intervalIndex++)
					{
						HalfEdge he = patchSide3.SidePieces[intervalIndex];
						int halfEdgeToOppositeCell = cmesh.HalfEdgeToOppositeCell(he);
						if (halfEdgeToOppositeCell < 0 || halfEdgeToOppositeCell > cellIterator || halfEdgeToOppositeCell == halfEdgeToOppositeCellTerm || cmesh.CellRegionMetadata[halfEdgeToOppositeCell].PatchType != PatchType.Quad)
						{
							continue;
						}
						List<PatchSide> quadSides2 = GetQuadSides(cmesh, halfEdgeToOppositeCell);
						int face = PatchSide.FindFace(quadSides2, he.fIdx);
						if (face < 0)
						{
							continue;
						}
						halfEdgeToOppositeCellTerm = halfEdgeToOppositeCell;
						HalfEdge key5 = quadSides[(n + 3) % 4].SidePieces.Last().Flipped();
						HalfEdge key6 = quadSides[(n + 1) % 4].SidePieces.First();
						HalfEdge key7 = quadSides2[(face + 3) % 4].SidePieces.Last().Flipped();
						HalfEdge key8 = quadSides2[(face + 1) % 4].SidePieces.First();
						int rowA6 = heToRow[key5];
						int rowA7 = heToRow[key6];
						int rowB5 = heToRow[key7];
						int rowB6 = heToRow[key8];
						double lengthDifference = cmesh.FaceSmoothLine[key5.fIdx].Length + cmesh.FaceSmoothLine[key8.fIdx].Length;
						double lengthDifference2 = cmesh.FaceSmoothLine[key6.fIdx].Length + cmesh.FaceSmoothLine[key7.fIdx].Length;
						AddSymWeights(rowA6, rowB6, interpParams.weightInterSide / lengthDifference2);
						AddSymWeights(rowA7, rowB5, interpParams.weightInterSide / lengthDifference);
						for (int intervalIndex2 = 0; intervalIndex2 < 2; intervalIndex2++)
						{
							double lengthDifference3 = quadSides[intervalIndex2].Smooth.Length + quadSides[2 + intervalIndex2].Smooth.Length;
							double lengthDifference4 = quadSides2[intervalIndex2].Smooth.Length + quadSides2[2 + intervalIndex2].Smooth.Length;
							double computed = lengthDifference3 + lengthDifference4;
							double weight5 = 4.0 * interpParams.weightInterQuad / computed;
							double scale2 = 1.0;
							if (intervalIndex2 == 1 && quadsUseCount)
							{
								scale2 = lengthDifference3 / lengthDifference4;
							}
							AddSymWeights(intByValueTuple[(cellIterator, intervalIndex2 == 0)], intByValueTuple[(halfEdgeToOppositeCell, intervalIndex2 == 0)], weight5, scale2);
						}
					}
				}
			}
			else if ((meshRegionMetadata.PatchType == PatchType.TrianglePaver || meshRegionMetadata.PatchType == PatchType.Cartesian) && interpParams.readNonquadSizes && interpParams.weightNonQuadRegion > 0.0 && float.IsFinite(meshRegionMetadata.ActualPrimaryCellSize) && meshRegionMetadata.ActualPrimaryCellSize > 0f)
			{
				float actualPrimaryCellSize = meshRegionMetadata.ActualPrimaryCellSize;
				double weight6 = interpParams.weightNonQuadRegion / Math.Sqrt(cmesh.CellSmoothArea(cellIterator));
				foreach (HalfEdge cellFace in cmesh.CellFaces[cellIterator])
				{
					int index3 = heToRow[cellFace];
					int index4 = heToRow[cellFace.Flipped()];
					sizeVars[index3].AddConstant(actualPrimaryCellSize, weight6);
					sizeVars[index4].AddConstant(actualPrimaryCellSize, weight6);
				}
			}
			foreach (int chain in Chain(cmesh.CellIslandFaces(cellIterator), cmesh.CellBreaklines(cellIterator)))
			{
				double maxValue = double.MaxValue;
				HalfEdge key9 = HalfEdge.NullHalfEdge;
				foreach (HalfEdge cellFace2 in cmesh.CellFaces[cellIterator])
				{
					double distanceToPoint = cmesh.FaceSmoothLine[chain].DistanceToPoint(cmesh.NodePoints[cmesh.HalfEdgeNodeBack(cellFace2)]);
					if (distanceToPoint < maxValue)
					{
						maxValue = distanceToPoint;
						key9 = cellFace2;
					}
				}
				maxValue += 0.5 * cmesh.FaceSmoothLine[chain].Length;
				sizeVars[heToRow[new HalfEdge(chain, 0)]].AddWeight(heToRow[key9], 0.1 / maxValue);
				sizeVars[heToRow[key9]].AddWeight(heToRow[new HalfEdge(chain, 0)], 0.001 / maxValue);
			}
		}
		foreach (int nodeIterator in cmesh.NodeIterator())
		{
			List<HalfEdge> nodeToSortedHalfEdges = cmesh.NodeToSortedHalfEdges(nodeIterator);
			if (nodeToSortedHalfEdges.Count < 2)
			{
				continue;
			}
			for (int intervalIndex3 = 0; intervalIndex3 < nodeToSortedHalfEdges.Count; intervalIndex3++)
			{
				HalfEdge halfEdge = nodeToSortedHalfEdges[intervalIndex3];
				HalfEdge key10 = nodeToSortedHalfEdges[(intervalIndex3 + 1) % nodeToSortedHalfEdges.Count];
				double lengthDifference5 = cmesh.FaceSmoothLine[halfEdge.fIdx].Length + cmesh.FaceSmoothLine[key10.fIdx].Length;
				int halfEdgeToCell = cmesh.HalfEdgeToCell(halfEdge);
				if (halfEdgeToCell >= 0 && cmesh.CellRegionMetadata[halfEdgeToCell].PatchType != PatchType.Empty)
				{
					AddSymWeights(heToRow[halfEdge], heToRow[key10], interpParams.weightNodeAdjacentWithRegion / lengthDifference5);
				}
				else
				{
					AddSymWeights(heToRow[halfEdge], heToRow[key10], interpParams.weightNodeAdjacent / lengthDifference5);
				}
			}
		}
		if (ordinal > 0)
		{
			BuildAndSolveMatrix(sizeVars, cmesh, interpParams.regionType, operationResult);
		}
		return operationResult;
		void AddSymWeights(int ordinal2, int ordinal3, double weight7, double computed2 = 1.0)
		{
			localSizeVars[ordinal2].AddWeight(ordinal3, weight7, computed2);
			localSizeVars[ordinal3].AddWeight(ordinal2, weight7, 1.0 / computed2);
		}
		static (int, bool) cellRowKey(SizeVar sv)
		{
			return (sv.cIdx, sv.cellPrimary);
		}
	}

	private void BuildAndSolveMatrix(List<SizeVar> sizeVars, ConceptualMesh cmesh, string regionType, OperationResult res)
	{
		int countOffsetSum = 0;
		foreach (SizeVar sizeVar3 in sizeVars)
		{
			countOffsetSum += 1 + sizeVar3.adjacent.Count;
		}
		SparseMatrix<double> sparseMatrix = new SparseMatrix<double>();
		int count = sizeVars.Count;
		double[] doubleValues = new double[count];
		double[] doubleValuesArray = new double[count];
		sparseMatrix.Initialize(count, countOffsetSum);
		List<int> intList = new List<int>();
		List<double> doubleList = new List<double>();
		for (int i = 0; i < count; i++)
		{
			intList.Clear();
			doubleList.Clear();
			SizeVar sizeVar = sizeVars[i];
			if (double.IsFinite(sizeVar.cellSize))
			{
				intList.Add(i);
				doubleList.Add(1.0);
				doubleValues[i] = sizeVar.cellSize;
				doubleValuesArray[i] = doubleValues[i];
			}
			else
			{
				intList.Add(i);
				doubleList.Add(1.0);
				doubleValues[i] = 0.0;
				doubleValuesArray[i] = 0.0;
				double weightLHSSum = 0.0;
				foreach (AdjacentVar adjacent in sizeVar.adjacent)
				{
					weightLHSSum += adjacent.weightLHS;
				}
				foreach (AdjacentVar adjacent2 in sizeVar.adjacent)
				{
					if (adjacent2.row >= 0)
					{
						intList.Add(adjacent2.row);
						doubleList.Add((0.0 - adjacent2.weightRHS) / weightLHSSum);
					}
					else
					{
						doubleValues[i] += adjacent2.weightRHS * adjacent2.cellSize / weightLHSSum;
					}
				}
			}
			sparseMatrix.BuildRow(i, intList.ToArray(), doubleList.ToArray());
		}
		sparseMatrix.Pack();
		sparseMatrix.Sort();
		Eliminate<double>.Compute(sparseMatrix, doubleValues, out var indexMap, out var _, out var AFree, out var rhsFree);
		if (sparseMatrix.RowCount == AFree.RowCount && rhsFree.Max() == 0.0)
		{
			if (!coalesceQuadNonquad)
			{
				res.AddMessage(MessageCodes.GetInsufficientCellSizes(regionType));
			}
			return;
		}
		double[] doubleValues2 = new double[rhsFree.Length];
		for (int j = 0; j < sparseMatrix.RowCount; j++)
		{
			if (indexMap[j] >= 0)
			{
				doubleValues2[indexMap[j]] = doubleValuesArray[j];
			}
		}
		IMatrixSolver<double> matrixSolver = MatrixSolverFactory.Create(new SolverRequest(new SolverPolicy(SolverKind.Pardiso, new SolverKind[] { SolverKind.Pgmres }), new LinearSystemInfo(IsSparse: true, IsSymmetric: false, IsPositiveDefinite: false, sparseMatrix.RowCount), new SolverOptionsBundle
		{
			PardisoOpts = new PardisoOptions(11, IsExtractUpperTriangular: false),
			PgmresOpts = new PgmresOptions(20, new MatrixConvergence<double>
			{
				MaxIterations = Math.Max(20, sparseMatrix.RowCount),
				ToleranceRMSE = 1E-10
			})
		}));
		OperationResult operationResult = matrixSolver.PrepareAndSolve(AFree, doubleValues2, rhsFree, new ILU<double>());
		operationResult.MergeIn(matrixSolver.Destroy(sparseMatrix));
		if (!operationResult)
		{
			res.AddFailure(MessageCodes.GetInsufficientCellSizes(regionType));
			res.MergeIn(operationResult);
			return;
		}
		for (int k = 0; k < count; k++)
		{
			SizeVar sizeVar2 = sizeVars[k];
			if (double.IsFinite(sizeVar2.cellSize) || indexMap[k] < 0)
			{
				continue;
			}
			if (doubleValues2[indexMap[k]] < 0.0)
			{
				if (sizeVar2.cIdx >= 0)
				{
					res.AddFailure(MessageCodes.GetInvalidInterpolatedRegionSize(regionType, sizeVar2.cIdx, sizeVar2.cellPrimary, doubleValues2[indexMap[k]]));
				}
				else
				{
					res.AddFailure(MessageCodes.GetInvalidInterpolatedArcSize(regionType, sizeVar2.he.fIdx, doubleValues2[indexMap[k]]));
				}
			}
			sizeVars[k] = sizeVar2.Modify(doubleValues2[indexMap[k]]);
		}
	}

	private void UpdateConceptualMeshSizes(ConceptualMesh cmesh, List<SizeVar> quadSizeVars, InterpolationParams quadInterpParams, List<SizeVar> nonquadSizeVars, InterpolationParams nonquadInterpParams, Dictionary<HalfEdge, int> heToRow, OperationResult res)
	{
		Span<double> doubleSpan = stackalloc double[2];
		if (quadSizeVars.Count != nonquadSizeVars.Count)
		{
			throw new ArgumentException("Size interpolation: List of sizes should match");
		}
		int count = quadSizeVars.Count;
		int ordinal = 0;
		int unity = 1;
		double cellSize22 = 0.0;
		if (coalesceQuadNonquad)
		{
			for (int i = 0; i < quadSizeVars.Count; i++)
			{
				double cellSize = quadSizeVars[i].cellSize;
				if (double.IsFinite(cellSize) && cellSize > 0.0)
				{
					cellSize22 = Math.Max(cellSize22, cellSize);
				}
			}
		}
		for (int j = 0; j < count; j++)
		{
			SizeVar sizeVar = quadSizeVars[j];
			SizeVar sizeVar2 = nonquadSizeVars[j];
			if (!sizeVar.isComputed || !sizeVar2.isComputed)
			{
				continue;
			}
			int fIdx = sizeVar.he.fIdx;
			if (fIdx < 0)
			{
				if (sizeVar.cIdx < 0)
				{
					throw new Exception("Internal error in size interpolation - expected region index");
				}
				MeshRegionMetadata cellRegionMetadata = cmesh.CellRegionMetadata[sizeVar.cIdx];
				double cellSizeSizeVarTerm;
				if (cellRegionMetadata.PatchType == PatchType.Quad)
				{
					if (!quadInterpParams.setQuadRegions)
					{
						continue;
					}
					cellSizeSizeVarTerm = sizeVar.cellSize;
					if (coalesceQuadNonquad && (!double.IsFinite(cellSizeSizeVarTerm) || cellSizeSizeVarTerm <= 0.0) && double.IsFinite(sizeVar2.cellSize))
					{
						cellSizeSizeVarTerm = sizeVar2.cellSize;
					}
				}
				else
				{
					if (nonquadInterpParams.nonquadRegions != NonquadRegionUpdate.Interpolate)
					{
						continue;
					}
					cellSizeSizeVarTerm = sizeVar2.cellSize;
					if (coalesceQuadNonquad && (!double.IsFinite(cellSizeSizeVarTerm) || cellSizeSizeVarTerm <= 0.0))
					{
						cellSizeSizeVarTerm = cellSize22;
					}
				}
				if (double.IsNaN(cellSizeSizeVarTerm))
				{
					res.AddFailure(MessageCodes.GetInsufficientCellSizes());
					return;
				}
				if (sizeVar.cellPrimary)
				{
					cellRegionMetadata.UserConstantSize = (float)cellSizeSizeVarTerm;
				}
				else
				{
					cellRegionMetadata.UserSecondaryConstantSize = (float)cellSizeSizeVarTerm;
				}
				cmesh.CellRegionMetadata[sizeVar.cIdx] = cellRegionMetadata;
			}
			else
			{
				if (sizeVar.he.orient == 1 || !IsNonEmptyArc(cmesh, fIdx))
				{
					continue;
				}
				if (IsQuadArc(cmesh, fIdx))
				{
					if (!quadInterpParams.setQuadArcs)
					{
						continue;
					}
				}
				else if (cmesh.FaceCells[fIdx].A == cmesh.FaceCells[fIdx].B || !nonquadInterpParams.setNonquadArcs)
				{
					continue;
				}
				for (byte b = 0; b < 2; b++)
				{
					int index = heToRow[new HalfEdge(fIdx, b)];
					double cellSizeQuadSizeVarsTerm;
					if (IsQuadArc(cmesh, fIdx))
					{
						cellSizeQuadSizeVarsTerm = quadSizeVars[index].cellSize;
						if (coalesceQuadNonquad && (!double.IsFinite(cellSizeQuadSizeVarsTerm) || cellSizeQuadSizeVarsTerm <= 0.0))
						{
							cellSizeQuadSizeVarsTerm = nonquadSizeVars[index].cellSize;
						}
					}
					else
					{
						cellSizeQuadSizeVarsTerm = nonquadSizeVars[index].cellSize;
						if (coalesceQuadNonquad && (!double.IsFinite(cellSizeQuadSizeVarsTerm) || cellSizeQuadSizeVarsTerm <= 0.0))
						{
							cellSizeQuadSizeVarsTerm = cellSize22;
						}
					}
					if (b == 0 && (!double.IsFinite(cellSizeQuadSizeVarsTerm) || cellSizeQuadSizeVarsTerm <= 0.0))
					{
						if (ordinal < unity)
						{
							res.AddFailure(MessageCodes.GetInvalidInterpolatedArcSize(quadInterpParams.regionType, fIdx, cellSizeQuadSizeVarsTerm));
						}
						ordinal++;
					}
					doubleSpan[b] = cellSizeQuadSizeVarsTerm;
				}
				cmesh.FaceDiscretization[fIdx].SetSizes(doubleSpan[0], doubleSpan[1], cmesh.FaceSmoothLine[fIdx].Length);
			}
		}
		if (ordinal > 0)
		{
			if (ordinal > unity)
			{
				res.AddFailure(new Message($"{ordinal - unity} more arcs had bad sizes were not reported"));
			}
		}
		else
		{
			if (nonquadInterpParams.nonquadRegions != NonquadRegionUpdate.Maximum)
			{
				return;
			}
			foreach (int cellIterator in cmesh.CellIterator())
			{
				MeshRegionMetadata cellRegionMetadata2 = cmesh.CellRegionMetadata[cellIterator];
				if (cellRegionMetadata2.HasPrimaryCellSize || cellRegionMetadata2.PatchType == PatchType.Quad || cellRegionMetadata2.PatchType == PatchType.OneD)
				{
					continue;
				}
				double computedConstantSize = 0.0;
				foreach (HalfEdge cellFace in cmesh.CellFaces[cellIterator])
				{
					for (int k = 0; k < 2; k++)
					{
						int index2 = heToRow[cellFace];
						double cellSize2 = nonquadSizeVars[index2].cellSize;
						if (!double.IsFinite(cellSize2) || cellSize2 <= 0.0)
						{
							cellSize2 = cellSize22;
						}
						computedConstantSize = Math.Max(computedConstantSize, cellSize2);
					}
				}
				cellRegionMetadata2.ComputedConstantSize = (float)computedConstantSize;
				cmesh.CellRegionMetadata[cellIterator] = cellRegionMetadata2;
			}
		}
	}

	public void DumpMatrixProblem(SparseMatrix<double> A, double[] b, string label, double[] x, int[] indexMap, double[] rhsBound)
	{
		string filename = "C:\\Temp\\sparseA-" + label + ".bin";
		A.StreamOutToFile(filename);
		using (FileStream output = File.Create("C:\\Temp\\rhs-" + label + ".bin"))
		{
			using BinaryWriter binaryWriter = new BinaryWriter(output);
			foreach (double computed in b)
			{
				binaryWriter.Write(computed);
			}
		}
		double[,] doubleValues = new double[A.RowCount, A.RowCount];
		A.ToDense(doubleValues);
		List<List<object>> localValue = new List<List<object>>
		{
			new List<object>
			{
				"SET",
				"A_raw",
				ToSimple(doubleValues)
			},
			new List<object> { "SET", "b_raw", b },
			new List<object> { "SET", "x_raw", x },
			new List<object> { "SET", "indexMap_raw", indexMap },
			new List<object> { "SET", "rhsBound_raw", rhsBound },
			new List<object> { "PYTHON", "\r\nA=np.array(A_raw)\r\nb=np.array(b_raw)\r\nx=np.array(x_raw)\r\nif indexMap_raw is not None:\r\n    indexMap=np.array(indexMap_raw)\r\n    rhsBound=np.array(rhsBound_raw)\r\n    if len(rhsBound)>0:\r\n        xFull=np.where(indexMap>=0, x[indexMap.clip(0)], rhsBound[(~indexMap).clip(0)])\r\n    else:\r\n        xFull=x[indexMap]\r\nelse:\r\n    xFull=x\r\n" }
		};
		using (FileStream utf8Json = File.Create("C:\\Temp\\cmd.json"))
		{
			JsonSerializerOptions options = new JsonSerializerOptions
			{
				WriteIndented = true,
				NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
				Converters = { (JsonConverter)new TinJsonConverter() }
			};
			JsonSerializer.Serialize(utf8Json, localValue, options);
		}
		static List<List<double>> ToSimple(double[,] M)
		{
			List<List<double>> doubleListList = new List<List<double>>();
			for (int j = 0; j < M.GetLength(0); j++)
			{
				List<double> doubleList = new List<double>();
				for (int k = 0; k < M.GetLength(1); k++)
				{
					doubleList.Add(M[j, k]);
				}
				doubleListList.Add(doubleList);
			}
			return doubleListList;
		}
	}

	public static void InferCartesianSize(ConceptualMesh cmesh, int regionIdx, TinSizeFunction globalSizeFunction)
	{
		double faceNodeStringLengthSum = 0.0;
		double segmentCountSum = 0.0;
		foreach (HalfEdge cellFace in cmesh.CellFaces[regionIdx])
		{
			faceNodeStringLengthSum += cmesh.FaceNodeString[cellFace.fIdx].Length;
			segmentCountSum += (double)cmesh.FaceNodeString[cellFace.fIdx].SegmentCount;
		}
		float computedConstantSize = (float)(faceNodeStringLengthSum / segmentCountSum);
		MeshRegionMetadata cellRegionMetadata = cmesh.CellRegionMetadata[regionIdx];
		cellRegionMetadata.ComputedConstantSize = computedConstantSize;
		cmesh.CellRegionMetadata[regionIdx] = cellRegionMetadata;
	}

	public static void DumpToJson(Geospatial.Vectors.Topology.Topology topo, ConceptualMesh cmesh, SlidingMesh smesh)
	{
		List<string> stringList = new List<string>(topo.Arcs.Count);
		for (int i = 0; i < topo.Arcs.Count; i++)
		{
			ArcMetadata arcData = topo.GetArcData(i);
			string path = "N/A";
			string message = ((arcData.ConstrainStretching != ConstrainStretchingOptions.Default) ? $"{arcData.ConstrainStretching}" : $"{arcData.ConstrainStretching}=>{cmesh.FaceDiscretization[i].ConstrainMode}");
			if (arcData.SizeFunction != LinearSizeFunction.Constant)
			{
				if (arcData.SizeFunction != LinearSizeFunction.Unset)
				{
					if (arcData.SizeFunction != LinearSizeFunction.CountWithBias)
					{
						path = "not constant or count??";
					}
					else
					{
						path = ((!double.IsFinite(arcData.ConstantSize) || !(arcData.ConstantSize > 0f)) ? ("blank-count? " + message) : $"count={arcData.Count} size={arcData.ConstantSize} bias={arcData.Bias} {message}");
					}
				}
				else
				{
					path = "blank " + message;
				}
			}
			else
			{
				path = ((!double.IsFinite(arcData.ConstantSize) || !(arcData.ConstantSize > 0f)) ? ("blank " + message) : $"size={arcData.ConstantSize} bias={arcData.Bias} {message}");
			}
			stringList.Add(path);
		}
		List<string> stringListList = new List<string>(topo.CompletedRegions.Count);
		for (int j = 0; j < topo.CompletedRegions.Count; j++)
		{
			MeshRegionMetadata regionData = topo.GetRegionData(j);
			MeshRegionMetadata meshRegionMetadata = cmesh.CellRegionMetadata[j];
			string message2 = ((!float.IsNaN(regionData.UserConstantSize)) ? $"{regionData.PatchType} size={regionData.UserConstantSize}/{regionData.ActualSecondaryConstantSize} r={regionData.StretchRate}" : $"{regionData.PatchType} blank");
			if (regionData.PatchType == PatchType.Quad || regionData.PatchType == PatchType.Cartesian)
			{
				message2 += $"\n=>{meshRegionMetadata.ActualPrimaryCellSize:F1}/{meshRegionMetadata.ActualSecondaryConstantSize:F1}";
			}
			else if (regionData.PatchType == PatchType.TrianglePaver)
			{
				message2 += $"\n=>{meshRegionMetadata.ActualPrimaryCellSize:F1}";
			}
			stringListList.Add(message2);
		}
		using FileStream utf8Json = File.Create("C:\\Temp\\cmd.json");
		JsonSerializerOptions options = new JsonSerializerOptions
		{
			WriteIndented = true,
			NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
			Converters = { (JsonConverter)new TinJsonConverter() }
		};
		List<object> localValue = new List<object>
		{
			new List<object> { "SET", "g_raw", smesh },
			new List<object> { "SET", "cg_raw", cmesh },
			new List<object> { "SET", "arcMetadata", stringList },
			new List<object> { "SET", "regionMetadata", stringListList },
			new List<object> { "PYTHON", "\r\nif g_raw is not None:\r\n    g=json_to_mesh(g_raw)\r\nelse:\r\n    g=None\r\nfig,ax=plt.subplots(num=1,clear=1)\r\nax.set_adjustable('datalim')\r\nif g is not None:\r\n    g.plot_edges(ax = ax, color = 'k', alpha=0.2, subedges='smoothStringXY')\r\n    g.plot_cells(ax = ax, color = 'orange', subedges='smoothStringXY', alpha = 0.4, zorder = -1)\r\ncg=json_to_mesh(cg_raw)\r\ncg.plot_edges(lw=0.7,color='k',ax=ax,subedges='rawStringXY')\r\n\r\nax.set_position([0,0,1,1])\r\nax.axis('off')\r\ndef lbl(i, r):\r\n    return f'{i}: ' + arcMetadata[i].replace(' ', '\\n').replace('Default','')\r\n\r\ndef lbl_he(j, orient):\r\n    lengthScale = cg.edges['faceDiscretization'][j]['lengthScale']\r\n    bias = cg.edges['faceDiscretization'][j]['bias']\r\n    if np.isnan(float(bias)) or lengthScale<=0:\r\n        return 'na'\r\n    if bias == 1.0:\r\n        L = lengthScale\r\n    else:\r\n        arcLength = utils.dist_total(cg.edges['rawStringXY'][j])\r\n        locCount = 1 + arcLength / lengthScale\r\n        r = bias**(1.0 / (locCount - 2.0))\r\n        startSize = arcLength * (1-r)/(1-r**(locCount-1))\r\n        endSize = bias * startSize\r\n        if orient == 0:\r\n            L = startSize\r\n        else:\r\n            L = endSize\r\n    return f'{L:.3g}'\r\n\r\ncg.plot_edges(subedges='rawStringXY',visible=0,labeler=lbl, \r\n              text_kw=dict(color='b',va='center',ha='center'))\r\ncg.plot_halfedges(labeler=lbl_he, placement='tangential',\r\n                  text_kw=dict(va='center',ha='center'),\r\n                  color='orange', subedges='rawStringXY', offset=0.04)\r\n\r\ncg.plot_cells(visible=0,subedges='rawStringXY',centroid=True,\r\n              labeler=lambda i,r: regionMetadata[i].replace(' ','\\n').replace('Default',''),\r\n              text_kw=dict(color='g'))\r\ncg.plot_nodes()\r\n" }
		};
		JsonSerializer.Serialize(utf8Json, localValue, options);
	}
}
