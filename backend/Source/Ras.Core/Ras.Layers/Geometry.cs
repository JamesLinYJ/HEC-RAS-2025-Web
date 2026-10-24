using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Geospatial.IO.Vectors;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Geospatial.Vectors.Topology;
using H5Assist;
using H5Assist.ORM;
using Microsoft.Extensions.Logging;
using Ras.Engine;
using Ras.Engine.Structures.Culverts;
using Ras.Engine.Structures.Weirs;
using Ras.Hydraulics;
using Ras.Hydraulics.Culverts;
using Ras.Mapping;
using Utility;
using Utility.Extensions;
using Utility.Hashing;
using Utility.Logging;
using Utility.ORM;
using Utility.Progress;

namespace Ras.Layers;

public class Geometry : CoreLayerBase, IResultSubLayer
{
	public static class MessageCodes
	{
		public static readonly MessageClass NoProjectAssociation = new MessageClass("Geom_NoProjectAssociation", MessageSeverity.Error, "Geometry {GeometryName} has no Project association.");

		public static readonly MessageClass NoTerrainAssociation = new MessageClass("Geom_NoTerrainAssociation", MessageSeverity.Error, "Geometry {GeometryName} has no Terrain association.");

		public static readonly MessageClass NoNValueAssociation = new MessageClass("Geom_NoNValueAssociation", MessageSeverity.Error, "Geometry {GeometryName} has no NValue layer association.");

		public static readonly MessageClass NoConceptualMesh = new MessageClass("Geom_NoConceptualMesh", MessageSeverity.Error, "Geometry {GeometryName} has no conceptual mesh.");

		public static Message GetNoProjectAssociation(string geomName)
		{
			return NoProjectAssociation.Create(geomName);
		}

		public static Message GetNoTerrainAssociation(string geomName)
		{
			return NoTerrainAssociation.Create(geomName);
		}

		public static Message GetNoNValueAssociation(string geomName)
		{
			return NoNValueAssociation.Create(geomName);
		}

		public static Message GetNoConceptualMesh(string geomName)
		{
			return NoConceptualMesh.Create(geomName);
		}
	}

	private new static readonly ILogger _logger = LogHelper.GetLogger("Geometry");

	public const string Extension = ".h5";

	public const string ExtensionSearchPattern = "*.h5";

	public const string HDF5Group = "/Geometry";

	public static readonly string FileVersion = Project.DevFileVersion;

	public const string PrepareSummaryGroupName = "Prepare Summary";

	public const string PrepareLogDatasetName = "Prepare Log";

	private int[] _lastBackupComponentHashes;

	public static readonly char[] IllegalGeometryNameCharacters = Path.GetInvalidFileNameChars();

	private static readonly string[] BackupHashLayerNames = new string[4] { "CulvertBarrelLayer", "BarrelPropertiesLayer", "OpeningPropertiesLayer", "ConceptualMeshLayer" };

	private static string PrepareSummaryGroup => H5Reader.PathCombine("/Geometry", "Prepare Summary");

	private static string PrepareLogDatasetPrefix => H5Reader.PathCombine(PrepareSummaryGroup, "Prepare Log");

	[Metadata(PathRelativeTarget = "Prepare Summary")]
	public GeometrySummary PrepareSummary { get; set; }

	public bool IsResult => Result != null;

	public Result Result { get; set; }

	public override Project Project
	{
		get
		{
			if (IsResult)
			{
				return Result.Project;
			}
			return base.Project;
		}
		set
		{
			if (IsResult)
			{
				if (Result.Project != value)
				{
					throw new Exception("Cannot set Project on a layer that belongs to a result - set Result.Project instead.");
				}
			}
			else
			{
				base.Project = value;
			}
		}
	}

	public override bool NameMustMatchFilename => !IsResult;

	public Terrain AssociatedTerrain
	{
		get
		{
			if (IsResult)
			{
				return Result.AssociatedTerrain;
			}
			if (Project == null)
			{
				return null;
			}
			GeometryAssociation geometryAssociation = GeometryAssociation;
			if (geometryAssociation == null)
			{
				_logger.LogWarning("Geometry Association null for Geometry '{Name}', likely initialization error.", Name);
				return null;
			}
			if (geometryAssociation.Terrain == null)
			{
				return null;
			}
			return geometryAssociation.Terrain;
		}
	}

	public NValueLayer AssociatedNValueLayer
	{
		get
		{
			if (IsResult)
			{
				return Result.AssociatedNValueLayer;
			}
			if (Project == null)
			{
				return null;
			}
			GeometryAssociation geometryAssociation = GeometryAssociation;
			if (geometryAssociation == null)
			{
				_logger.LogWarning("Geometry Association null for Geometry '{Name}', likely initialization error.", Name);
				return null;
			}
			if (geometryAssociation.NValueLayer == null)
			{
				return null;
			}
			return geometryAssociation.NValueLayer;
		}
	}

	public GeometryAssociation GeometryAssociation => AssertGetProject().GetGeometryAssociation(this);

	public Extent Extent => Extent.Union(new List<Extent> { FlowAreaLayer.Extent, ConceptualMeshLayer.Extent });

	public Extent HydraulicExtent => Extent.Union(new List<Extent> { FlowAreaLayer.Extent });

	public Mesh Mesh => FlowAreaLayer.FirstOrDefault()?.Mesh;

	public MeshPropertyTables HydraulicPropertyTables => FlowAreaLayer.FirstOrDefault()?.PropertyTables;

	private List<IGeometryLayer> GeometryLayers { get; } = new List<IGeometryLayer>();

	public ConceptualMeshLayer ConceptualMeshLayer { get; private set; }

	public FlowAreaLayer FlowAreaLayer { get; private set; }

	public OpeningPropertiesLayer OpeningPropertiesLayer { get; private set; }

	public BarrelPropertiesLayer BarrelPropertiesLayer { get; private set; }

	public CulvertBarrelLayer CulvertBarrelLayer { get; private set; }

	protected override string GetHDF5Group()
	{
		return "/Geometry";
	}

	public override string GetCurrentFileFormatVersion()
	{
		return FileVersion;
	}

	public static string MakeFilename(string projectDir, string geomName)
	{
		return Project.MakeFilename<Geometry>(projectDir, geomName, ".h5");
	}

	public void DebugSetResult(Result res)
	{
		Result = res;
	}

	public List<MeshTableSignatureInfo> LoadTableSignatures()
	{
		List<MeshTableSignatureInfo> retn = new List<MeshTableSignatureInfo>();
		string faGroup = "/Geometry/2D Flow Areas";
		UseReader((H5Reader hr) =>
		{
			if (!hr.ExistsAsGroup(faGroup))
			{
				return;
			}
			foreach (string groupName in hr.GetGroupNames(faGroup))
			{
				string meshGroup = faGroup + "/" + groupName;
				bool containsTables = MeshReader.ContainsTables(hr, meshGroup, out var _);
				MeshPropertyTableSignature signature = null;
				if (containsTables)
				{
					MeshPropertyTableSignature.TryLoad(hr, MeshReader.TableSignatureGroupName(meshGroup), out signature);
				}
				retn.Add(new MeshTableSignatureInfo(groupName, containsTables, signature));
			}
		});
		return retn;
	}

	private Geometry(InstantiationMode mode)
		: base(mode)
	{
		ConstructHydraulicLayers();
	}

	public Geometry(string filename, Project project = null)
		: base(filename, project)
	{
	}

	internal Geometry(Result res)
		: base(InstantiationMode.ReadFromDisk)
	{
		Result = res;
		Filename = res.Filename;
		_h5Wrapper = res.H5Wrapper;
		InitializeData();
	}

	public static Geometry CreateNew(string filename)
	{
		if (string.IsNullOrEmpty(filename))
		{
			throw new ArgumentNullException("filename");
		}
		if (File.Exists(filename))
		{
			throw new Exception(filename + " must not exist.");
		}
		Geometry geometry = new Geometry(InstantiationMode.CreateNewFile);
		geometry.InitializeAndSaveAsNew(filename);
		return geometry;
	}

	public static Geometry CreateInMemory(string name = "")
	{
		return new Geometry(InstantiationMode.InMemory)
		{
			_name = name
		};
	}

	public static Geometry Load(NativeResourceWrapper<H5Reader> reader, string name = "")
	{
		Geometry geometry = new Geometry(InstantiationMode.LoadFromDataProvider);
		geometry._h5Wrapper = reader;
		geometry.InitializeData();
		if (!string.IsNullOrEmpty(name))
		{
			geometry._name = name;
		}
		return geometry;
	}

	private void ConstructHydraulicLayers()
	{
		List<IGeometryLayer> geometryLayers = GeometryLayers;
		ConceptualMeshLayer conceptualMeshLayer = (ConceptualMeshLayer = new ConceptualMeshLayer(this));
		geometryLayers.Add(conceptualMeshLayer);
		List<IGeometryLayer> geometryLayers2 = GeometryLayers;
		FlowAreaLayer flowAreaLayer = (FlowAreaLayer = new FlowAreaLayer(this));
		geometryLayers2.Add(flowAreaLayer);
		List<IGeometryLayer> geometryLayers3 = GeometryLayers;
		CulvertBarrelLayer culvertBarrelLayer = (CulvertBarrelLayer = new CulvertBarrelLayer(this));
		geometryLayers3.Add(culvertBarrelLayer);
		List<IGeometryLayer> geometryLayers4 = GeometryLayers;
		OpeningPropertiesLayer openingPropertiesLayer = (OpeningPropertiesLayer = new OpeningPropertiesLayer(this));
		geometryLayers4.Add(openingPropertiesLayer);
		List<IGeometryLayer> geometryLayers5 = GeometryLayers;
		BarrelPropertiesLayer barrelPropertiesLayer = (BarrelPropertiesLayer = new BarrelPropertiesLayer(this));
		geometryLayers5.Add(barrelPropertiesLayer);
	}

	protected override void PreInitializeDataInternal()
	{
		ConstructHydraulicLayers();
	}

	protected override void LoadInternal(H5Reader hr)
	{
		foreach (IGeometryLayer geometryLayer in GeometryLayers)
		{
			geometryLayer.LoadMetadata(hr);
		}
		IReadOnlyList<MessageLogEntry> entries;
		if (!hr.ExistsAsGroup(PrepareSummaryGroup))
		{
			PrepareSummary = null;
		}
		else if (PrepareSummary != null && hr.TryReadMessageLog(PrepareLogDatasetPrefix, out entries))
		{
			PrepareSummary.LogEntries = entries;
		}
	}

	protected override bool NeedsSaveInternal()
	{
		foreach (IGeometryLayer geometryLayer in GeometryLayers)
		{
			if (geometryLayer.NeedsSave())
			{
				_logger.LogDebug("{GeometryName}: {LayerName} needs to be saved (short-circuiting).", Name, geometryLayer.GetType().Name);
				return true;
			}
		}
		return false;
	}

	protected override void SaveInternal(H5Writer hw)
	{
		bool roughlyEquals = hw.Filename.RoughlyEquals(Filename);
		foreach (IGeometryLayer geometryLayer in GeometryLayers)
		{
			geometryLayer.Save(hw, !roughlyEquals);
		}
		hw.WriteMessageLog(PrepareLogDatasetPrefix, PrepareSummary?.LogEntries);
	}

	public void SaveGeometryBackup(string backupFile)
	{
		if (File.Exists(backupFile))
		{
			throw new ArgumentException("Backup file exists");
		}
		using Geometry geometry = CreateInMemory();
		geometry.ConceptualMeshLayer = ConceptualMeshLayer;
		using H5Writer h5Writer = new H5Writer(backupFile);
		Stopwatch stopwatch = Stopwatch.StartNew();
		string hDF5Group = GetHDF5Group();
		H5Metadata.Write(geometry, h5Writer, hDF5Group);
		ConceptualMeshLayer.Save(h5Writer, forceSaveIfFeaturesUnloaded: true, refreshLoadHashes: false);
		CulvertBarrelLayer.Save(h5Writer, forceSaveIfFeaturesUnloaded: true, refreshLoadHashes: false);
		BarrelPropertiesLayer.Save(h5Writer, forceSaveIfFeaturesUnloaded: true, refreshLoadHashes: false);
		OpeningPropertiesLayer.Save(h5Writer, forceSaveIfFeaturesUnloaded: true, refreshLoadHashes: false);
		Project?.SaveProjectMetadataToH5Header(h5Writer);
		stopwatch.Stop();
		_logger.LogInformation("Saved {Filename} in ({Duration} ms)", Path.GetFileName(Filename), stopwatch.ElapsedMilliseconds);
	}

	public int GetHashVersionForBackup()
	{
		int dataHash = CulvertBarrelLayer.GetDataHash();
		int dataHash2 = BarrelPropertiesLayer.GetDataHash();
		int dataHash3 = OpeningPropertiesLayer.GetDataHash();
		int dataHash4 = ConceptualMeshLayer.GetDataHash();
		ReportBackupHashChanges(dataHash, dataHash2, dataHash3, dataHash4);
		return StableHash.Combine(StableHash.Combine(StableHash.Combine(dataHash, dataHash2), dataHash3), dataHash4);
	}

	private void ReportBackupHashChanges(int culvert, int barrel, int opening, int mesh)
	{
		int[] lastBackupComponentHashes = new int[4] { culvert, barrel, opening, mesh };
		if (_lastBackupComponentHashes == null)
		{
			_lastBackupComponentHashes = lastBackupComponentHashes;
			return;
		}
		List<string> stringList = new List<string>();
		for (int i = 0; i < lastBackupComponentHashes.Length; i++)
		{
			if (lastBackupComponentHashes[i] != _lastBackupComponentHashes[i])
			{
				stringList.Add(BackupHashLayerNames[i]);
			}
		}
		_lastBackupComponentHashes = lastBackupComponentHashes;
		if (stringList.Count != 0)
		{
			_logger.LogInformation("Backup hash for {Geometry} changed in: {Layers}. Conceptual mesh loaded: {IsLoaded}.", Name, string.Join(", ", stringList), ConceptualMeshLayer.IsDataLoaded);
		}
	}

	public Geometry Clone(string newName, bool fixNameIfExists)
	{
		if (IsInMemory)
		{
			return CloneInMemory();
		}
		string directoryName = Path.GetDirectoryName(Filename);
		string path = Path.Combine(directoryName, newName + ".h5");
		if (File.Exists(path))
		{
			if (!fixNameIfExists)
			{
				throw new Exception(path + " already exists.");
			}
			int unity = 1;
			while (true)
			{
				path = Path.Combine(directoryName, newName + "(" + unity + ").h5");
				if (!File.Exists(path))
				{
					break;
				}
				unity++;
			}
		}
		File.Copy(Filename, path);
		Geometry geometry = new Geometry(path);
		Project?.AddGeometry(geometry);
		return geometry;
	}

	public Geometry CloneInMemory()
	{
		Geometry geometry = CreateInMemory();
		geometry.ConceptualMeshLayer.Topology = ConceptualMeshLayer.Topology.Clone();
		FlowAreaLayer.CloneTo(geometry.FlowAreaLayer);
		return geometry;
	}

	public Geometry SoftClone()
	{
		throw new NotImplementedException();
	}

	public void EvictHydraulicLayersFromMemory()
	{
		foreach (IGeometryLayer geometryLayer in GeometryLayers)
		{
			geometryLayer.EvictMemory();
		}
	}

	public static Geometry DebugSaveTopologyAsNewGeometry(string filename, Topology topo, Project projMetadata = null)
	{
		Geometry geometry = CreateNew(filename);
		geometry.Project = projMetadata;
		geometry.ConceptualMeshLayer.Topology = topo;
		geometry.Save();
		return geometry;
	}

	public void ClipInto(Polygon polygon, Geometry target, GeometryClipOptions? opts = null)
	{
		if (!opts.HasValue)
		{
			opts = GeometryClipOptions.Default();
		}
		GeometryClipOptions geometryClipOptions = opts.Value;
		TopologyClipOptions topologyClipOptions = TopologyClipOptions.Default();
		topologyClipOptions.FullyContainedGeometry = geometryClipOptions.FullyContainedGeometry;
		Topology topology = ConceptualMeshLayer.Topology;
		if (topology != null)
		{
			Topology topology2 = topology.Clip(polygon, topologyClipOptions);
			target.ConceptualMeshLayer.Topology = topology2;
		}
		CulvertBarrelLayer.ClipInto(polygon, target.CulvertBarrelLayer, geometryClipOptions);
		CopyUsedBarrelDependencies(target);
	}

	private void CopyUsedBarrelDependencies(Geometry target)
	{
		if (target.CulvertBarrelLayer.Count != 0)
		{
			HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			HashSet<string> seen2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < target.CulvertBarrelLayer.Count; i++)
			{
				CulvertBarrel culvertBarrel = target.CulvertBarrelLayer[i];
				TryCopyBarrelProperties(culvertBarrel.BarrelPropertyName, target, seen);
				TryCopyOpeningProperties(culvertBarrel.UpstreamOpeningName, target, seen2);
				TryCopyOpeningProperties(culvertBarrel.DownstreamOpeningName, target, seen2);
			}
		}
	}

	private void TryCopyBarrelProperties(string name, Geometry target, HashSet<string> seen)
	{
		if (!string.IsNullOrEmpty(name) && seen.Add(name) && target.BarrelPropertiesLayer.TryFindByName(name) == null)
		{
			BarrelProperties barrelProperties = BarrelPropertiesLayer.TryFindByName(name);
			if (barrelProperties != null)
			{
				target.BarrelPropertiesLayer.Add((BarrelProperties)barrelProperties.Clone());
			}
		}
	}

	private void TryCopyOpeningProperties(string name, Geometry target, HashSet<string> seen)
	{
		if (!string.IsNullOrEmpty(name) && seen.Add(name) && target.OpeningPropertiesLayer.TryFindByName(name) == null)
		{
			OpeningProperties openingProperties = OpeningPropertiesLayer.TryFindByName(name);
			if (openingProperties != null)
			{
				target.OpeningPropertiesLayer.Add((OpeningProperties)openingProperties.Clone());
			}
		}
	}

	public GeometryMapPoints RasterizeForMapping(RasterDefinition raster, GeometrySamplingParameters parameters = null)
	{
		return new GeometryMapPoints(this, raster);
	}

	private static void FindConsecutiveIntersections(IList<Polyline> plines, IList<LineCollectionIntersection> intersections, Action<LineCollectionIntersection, LineCollectionIntersection, double> intersectionAction)
	{
		int count = plines.Count;
		List<LineCollectionIntersection>[] lineCollectionIntersectionListArray = new List<LineCollectionIntersection>[count];
		for (int i = 0; i < count; i++)
		{
			lineCollectionIntersectionListArray[i] = new List<LineCollectionIntersection>();
		}
		foreach (LineCollectionIntersection intersection in intersections)
		{
			lineCollectionIntersectionListArray[intersection.APolylineIndex].Add(intersection);
		}
		for (int j = 0; j < count; j++)
		{
			Polyline polyline = plines[j];
			double[] cumulativeSegmentLengths = polyline.CumulativeSegmentLengths();
			List<LineCollectionIntersection> orderBy = lineCollectionIntersectionListArray[j].OrderBy((LineCollectionIntersection rivInt) => rivInt.Intersection.AIntersection, PointOnPolyline.Comparer()).ToList();
			for (int intervalIndex = 0; intervalIndex < orderBy.Count - 1; intervalIndex++)
			{
				LineCollectionIntersection orderBy2 = orderBy[intervalIndex];
				LineCollectionIntersection orderBy3 = orderBy[intervalIndex + 1];
				PointOnPolyline aIntersection = orderBy2.Intersection.AIntersection;
				PointOnPolyline aIntersection2 = orderBy3.Intersection.AIntersection;
				double distanceAlongFast = polyline.DistanceAlongFast(aIntersection, ref cumulativeSegmentLengths);
				double distanceAlongFastPolylineTerm = polyline.DistanceAlongFast(aIntersection2, ref cumulativeSegmentLengths) - distanceAlongFast;
				intersectionAction(orderBy2, orderBy3, distanceAlongFastPolylineTerm);
			}
		}
	}

	public IResample<float> ApproximateTerrainFromTables()
	{
		if (FlowAreaLayer.Count != 1)
		{
			throw new Exception("Needs exactly one flow area.");
		}
		FlowArea flowArea = FlowAreaLayer[0];
		MeshPropertyTables propertyTables = flowArea.PropertyTables;
		if (propertyTables == null)
		{
			throw new Exception("Flow area doesn't have property tables.");
		}
		return new TinResampler(flowArea.Mesh.ExportAsTIN(propertyTables), renderZ: true);
	}

	public OperationResult CanCompute()
	{
		OperationResult operationResult = true;
		if (AssociatedTerrain == null)
		{
			operationResult.AddFailure(MessageCodes.NoTerrainAssociation.Create(Name));
		}
		if (AssociatedNValueLayer == null)
		{
			operationResult.AddFailure(MessageCodes.NoNValueAssociation.Create(Name));
		}
		OperationResult operationResult2 = CulvertBarrelLayer.CanCompute();
		if (!operationResult2)
		{
			operationResult.AddFailure(operationResult2);
		}
		OperationResult operationResult3 = BarrelPropertiesLayer.CanCompute();
		if (!operationResult3)
		{
			operationResult.AddFailure(operationResult3);
		}
		OperationResult operationResult4 = OpeningPropertiesLayer.CanCompute();
		if (!operationResult4)
		{
			operationResult.AddFailure(operationResult4);
		}
		return operationResult;
	}

	public OperationResult PrepareForCompute(ProgressReporter reporter = null)
	{
		if (reporter == null || !reporter.IsMutable)
		{
			reporter = new ProgressReporter();
		}
		GeometrySummary geometrySummary = new GeometrySummary();
		geometrySummary.StartTime = DateTime.Now;
		geometrySummary.SoftwareVersion = Project.SoftwareVersion;
		PrepareSummary = geometrySummary;
		Stopwatch stopwatch = Stopwatch.StartNew();
		using MessageLogCollector messageLogCollector = new MessageLogCollector(reporter);
		reporter.ReportMessage("Preparing Geometry...");
		OperationResult operationResult = true;
		Stopwatch stopwatch2 = Stopwatch.StartNew();
		OperationResult operationResult2 = FlowAreaLayer.ComputePropertyTables(forceRecompute: false, reporter.SubTask("Computing Property Tables", 0f, 1f));
		stopwatch2.Stop();
		if (!operationResult2)
		{
			operationResult.AddFailure(operationResult2);
		}
		OperationResult operationResult3 = CulvertBarrelLayer.PrepareForCompute();
		if (!operationResult3)
		{
			operationResult.AddFailure(operationResult3);
		}
		geometrySummary.PropertyTableDuration = stopwatch2.Elapsed;
		geometrySummary.Success = operationResult.Result;
		if (!operationResult.Result)
		{
			geometrySummary.FailureReason = operationResult.GetConcatenatedMessages("; ");
		}
		geometrySummary.TotalDuration = stopwatch.Elapsed;
		geometrySummary.PrepareLog = messageLogCollector.Text;
		geometrySummary.LogEntries = messageLogCollector.Entries;
		Save();
		return operationResult;
	}

	public OperationResult InitializeSolver(Solver solver)
	{
		OperationResult operationResult = true;
		OperationResult operationResult2 = InitializeSolver_Culverts(solver);
		if (!operationResult2)
		{
			operationResult.AddFailure(operationResult2);
		}
		solver.HydraulicStructures.IdentifyStructureCellsAndFaces(Mesh);
		return operationResult;
	}

	public OperationResult InitializeComputeDriver(ComputeDriver driver)
	{
		driver.MeshTags = MeshTags.ComputeFromTopology(driver.GlobalMesh, ConceptualMeshLayer.Topology);
		OperationResult operationResult = true;
		OperationResult operationResult2 = InitializeDriver_Culverts(driver);
		if (!operationResult2)
		{
			operationResult.AddFailure(operationResult2);
		}
		OperationResult operationResult3 = InitializeDriver_Weirs(driver);
		if (!operationResult3)
		{
			operationResult.AddFailure(operationResult3);
		}
		driver.HydraulicStructures.IdentifyStructureCellsAndFaces(driver.GlobalMesh);
		return operationResult;
	}

	private OperationResult InitializeSolver_Culverts(Solver solver)
	{
		return InitializeDriver_Culverts(solver.ComputeDriver);
	}

	private OperationResult InitializeDriver_Culverts(ComputeDriver computeDriver)
	{
		if (CulvertBarrelLayer.Count == 0)
		{
			return true;
		}
		for (int i = 0; i < CulvertBarrelLayer.Count; i++)
		{
			CulvertBarrel culvertBarrel = CulvertBarrelLayer[i];
			Culvert culvert = new Culvert(culvertBarrel.Name, culvertBarrel.Polyline);
			CulvertPhysProps physProps = culvert.PhysProps;
			physProps.InvertUS = culvertBarrel.UpstreamInvert;
			physProps.InvertDS = culvertBarrel.DownstreamInvert;
			physProps.Length = culvertBarrel.Length;
			culvertBarrel.TryEvaluateAssociations(out var barrelProps, out var usOpeningProps, out var dsOpeningProps);
			if (barrelProps == null || usOpeningProps == null || dsOpeningProps == null)
			{
				return OperationResult.Fail("Barrel " + culvertBarrel.Name + " has missing property definitions (Upstream Opening, Downstream Opening, Barrel Properties");
			}
			physProps.Shape = EngineShape(barrelProps);
			physProps.FullRise = barrelProps.Rise;
			physProps.Span = barrelProps.Span;
			physProps.Mann = barrelProps.Mannings;
			physProps.MannBottom = barrelProps.ManningsBot;
			physProps.IsMannBottom = barrelProps.UseTwoPhaseMannings;
			physProps.DepthMannBottom = barrelProps.ManningsSwitchDepth;
			physProps.DepthBlocked = barrelProps.DepthBlocked;
			physProps.CoefEntranceLoss = usOpeningProps.KIn;
			physProps.CoefExitLoss = usOpeningProps.KOut;
			physProps.CoefEntranceLossReverse = dsOpeningProps.KIn;
			physProps.CoefExitLossReverse = dsOpeningProps.KOut;
			OperationResult operationResult = SetOpeningProps(physProps, usOpeningProps, isUpstream: true);
			if (!operationResult)
			{
				return operationResult;
			}
			operationResult = SetOpeningProps(physProps, dsOpeningProps, isUpstream: false);
			if (!operationResult)
			{
				return operationResult;
			}
			computeDriver.HydraulicStructures.AddHydraulicStructure(culvert);
		}
		return true;
	}

	private static OperationResult SetOpeningProps(CulvertPhysProps prps, OpeningProperties openingProps, bool isUpstream)
	{
		string isUpstreamTerm = (isUpstream ? "Upstream" : "Downstream");
		if (openingProps.OpeningType == BarrelOpeningType.Custom)
		{
			return OperationResult.Fail(isUpstreamTerm + " Opening uses Custom chart/scale, which is not yet supported.");
		}
		if (openingProps.OpeningType == BarrelOpeningType.None)
		{
			return OperationResult.Fail(isUpstreamTerm + " Opening uses None chart/scale, which is not yet supported.");
		}
		var (localValue, localValue2) = openingProps.TryGetChartScale();
		if (localValue == -1 || localValue2 == -1)
		{
			return OperationResult.Fail(isUpstreamTerm + " Opening Chart/Scale not recognized.");
		}
		var (eqnForm, k, m, c, y, slopeCorrection) = ChartScaleHelpers.TryParametrize(localValue, localValue2);
		if (isUpstream)
		{
			prps.InletCoefUS.EqnForm = eqnForm;
			prps.InletCoefUS.K = k;
			prps.InletCoefUS.M = m;
			prps.InletCoefUS.C = c;
			prps.InletCoefUS.Y = y;
			prps.InletCoefUS.SlopeCorrection = slopeCorrection;
		}
		else
		{
			prps.InletCoefDS.EqnForm = eqnForm;
			prps.InletCoefDS.K = k;
			prps.InletCoefDS.M = m;
			prps.InletCoefDS.C = c;
			prps.InletCoefDS.Y = y;
			prps.InletCoefDS.SlopeCorrection = slopeCorrection;
		}
		return true;
	}

	private static CulvertShape EngineShape(BarrelProperties props)
	{
		switch (props.Shape)
		{
		case BarrelShape.Box:
			return CulvertShape.Box;
		case BarrelShape.Circle:
			return CulvertShape.Circular;
		case BarrelShape.Ellipse:
			return CulvertShape.Ellipse;
		default:
			_ = 3;
			throw new NotSupportedException();
		}
	}

	private OperationResult InitializeDriver_Weirs(ComputeDriver computeDriver)
	{
		_ = computeDriver.MeshTags.Faces.GroupMembers;
		for (int i = 0; i < computeDriver.MeshTags.Faces.GroupMembers.Count; i++)
		{
			if (computeDriver.MeshTags.Faces.Tags[i].TryGetValue("weir_c", out var localValue))
			{
				double weirCoef = (double)localValue;
				if (weirCoef > 0.0)
				{
					Weir weir = new Weir("Weir_" + i, null);
					WeirPhysProps physProps = weir.PhysProps;
					physProps.WeirCoef = weirCoef;
					physProps.Shape = WeirCrestShape.SharpCrested;
					weir.Faces = computeDriver.MeshTags.Faces.GroupMembers[i].ToArray();
					computeDriver.HydraulicStructures.AddHydraulicStructure(weir);
				}
			}
		}
		return true;
	}

	public OperationResult RecomputeMesh(ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		FlowAreaLayer.Clear();
		Topology topology = ConceptualMeshLayer.Topology;
		if (topology == null)
		{
			return OperationResult.Fail(MessageCodes.GetNoConceptualMesh(Name));
		}
		OperationResult operationResult = topology.Complete();
		if (!operationResult)
		{
			return operationResult;
		}
		OperationResult operationResult2 = topology.BuildConceptualMesh(out var _, out var finalMesh, null, pr);
		if (!operationResult2)
		{
			return operationResult2;
		}
		FlowAreaLayer.Add(new FlowArea(finalMesh, "Mesh"));
		return true;
	}

	public Project AssertGetProject()
	{
		if (Project != null)
		{
			return Project;
		}
		throw new Exception("No Project is currently associated with this Geometry.");
	}

	public static bool IsValid(string fn)
	{
		try
		{
			if (!File.Exists(fn))
			{
				return false;
			}
			string extension = Path.GetExtension(fn);
			if (extension.RoughlyEquals(".h5") || extension.RoughlyEquals(".hdf"))
			{
				using (H5Reader h5r = new H5Reader(fn))
				{
					return IsValid(h5r);
				}
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	public static bool IsValid(H5Reader h5r)
	{
		try
		{
			if (h5r.ExistsAsGroup("/Geometry"))
			{
				return true;
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	public OperationResult CheckAssociations(out Project prj, out Terrain tr, out NValueLayer nv)
	{
		OperationResult operationResult = true;
		prj = Project;
		if (prj == null)
		{
			operationResult.AddFailure(MessageCodes.GetNoProjectAssociation(Name));
		}
		tr = AssociatedTerrain;
		if (tr == null)
		{
			operationResult.AddFailure(MessageCodes.GetNoTerrainAssociation(Name));
		}
		nv = AssociatedNValueLayer;
		if (nv == null)
		{
			operationResult.AddFailure(MessageCodes.GetNoNValueAssociation(Name));
		}
		return operationResult;
	}

	public bool IsDefault()
	{
		return IsEmpty();
	}

	public bool IsEmpty()
	{
		if (ConceptualMeshLayer.Topology.IsEmpty())
		{
			return CulvertBarrelLayer.Count == 0;
		}
		return false;
	}
}
