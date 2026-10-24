using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Geospatial.Rasters;
using Geospatial.Vectors;
using H5Assist;
using Microsoft.Extensions.Logging;
using Ras.Engine;
using Ras.Hydraulics;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Meteorology;
using Ras.Layers.Payloads;
using Ras.Layers.Results;
using Ras.Mapping;
using Utility;
using Utility.Extensions;
using Utility.Files;
using Utility.Hashing;
using Utility.Logging;
using Utility.ORM;
using Utility.Progress;

namespace Ras.Layers;

public class BoundaryCondition : CoreLayerBase, IResultSubLayer
{
	public static class MessageCodes
	{
		public static readonly MessageClass BCDoesntCoverTimeWindow = new MessageClass("BC_DoesntCoverTimeWindow", MessageSeverity.Error, "Boundary Condition {BoundaryConditionName}'s time window doesn't cover the full time window.");

		public static readonly MessageClass BCLDoesntCoverTimeWindow = new MessageClass("BC_BCL_DoesntCoverTimeWindow", MessageSeverity.Error, "Boundary Condition {BoundaryConditionName}'s Boundary Condition Line {BoundaryConditionLineName} doesn't cover the full time window.");

		public static readonly MessageClass BCLBadPolyline = new MessageClass("BC_BCL_BadPolyline", MessageSeverity.Error, "Boundary Condition {BoundaryConditionName}'s Boundary Condition Line {BoundaryConditionLineName} has bad/no polyline coordinates.");

		public static readonly MessageClass NoInflowData = new MessageClass("BC_NoInflowData", MessageSeverity.Error, "Boundary Condition {BoundaryConditionName} has no inflow data. At least one boundary condition line or spatial boundary condition (e.g., precipitation) must be defined.");

		public static readonly MessageClass BCLNoBoundFaces = new MessageClass("BC_BCL_NoBoundFaces", MessageSeverity.Warning, "Boundary Condition {BoundaryConditionName}'s Boundary Condition Line {BoundaryConditionLineName} bound 0 mesh perimeter faces and will pass no flow. Move the line onto the 2D area perimeter.");

		public static readonly MessageClass BCLNoHydraulicPayload = new MessageClass("BC_BCL_NoHydraulicPayload", MessageSeverity.Warning, "Boundary Condition {BoundaryConditionName}'s Boundary Condition Line {BoundaryConditionLineName} has no enabled hydraulic payload; skipping at compute (observation-only).");

		public static Message GetBCDoesntCoverTimeWindow(string bcName)
		{
			return BCDoesntCoverTimeWindow.Create(bcName);
		}

		public static Message GetBCLDoesntCoverTimeWindow(string bcName, string lineName)
		{
			return BCLDoesntCoverTimeWindow.Create(bcName, lineName);
		}

		public static Message GetBCLBadPolyline(string bcName, string lineName)
		{
			return BCLBadPolyline.Create(bcName, lineName);
		}

		public static Message GetNoInflowData(string bcName)
		{
			return NoInflowData.Create(bcName);
		}

		public static Message GetBCLNoBoundFaces(string bcName, string lineName)
		{
			return BCLNoBoundFaces.Create(bcName, lineName);
		}

		public static Message GetBCLNoHydraulicPayload(string bcName, string lineName)
		{
			return BCLNoHydraulicPayload.Create(bcName, lineName);
		}
	}

	private new static readonly ILogger _logger = LogHelper.GetLogger("BoundaryCondition");

	public const string Extension = ".h5";

	public const string HDFGroupName = "/Boundary Conditions";

	public const string HDFMeteorologyGroup = "/Boundary Conditions/Meteorology";

	public static readonly string FileVersion = Project.DevFileVersion;

	public static readonly TimeSpan DefaultWarmupTime = TimeSpan.Zero;

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

	[HashContributor]
	[Metadata("Start Time")]
	public DateTime StartTime { get; set; } = Plan.DefaultStartTime;

	[HashContributor]
	[Metadata("End Time")]
	public DateTime EndTime { get; set; } = Plan.DefaultStartTime + TimeSpan.FromSeconds(43200.0);

	public TimeSpan Duration => EndTime - StartTime;

	[HashContributor]
	[Metadata("Warmup Time", LegacyKeyNames = new string[] { "Warmup Timestep" }, Optional = true, SetValueToDefaultIfKeyMissing = false)]
	public TimeSpan WarmupTime { get; set; } = DefaultWarmupTime;

	public MeteorologicStationLayer MetStations { get; private set; }

	public WindLayer Wind { get; private set; }

	public PrecipitationLayer Precipitation { get; private set; }

	public EvapotranspirationLayer Evapotranspiration { get; private set; }

	public AirDensityLayer AirDensity { get; private set; }

	public AirPressureLayer AirPressure { get; private set; }

	public AirTemperatureLayer AirTemperature { get; private set; }

	public HumidityLayer Humidity { get; private set; }

	public BoundaryConditionLineLayer BoundaryConditionLines { get; private set; }

	public InitialConditionsLayer InitialConditions { get; private set; }

	public IResample<float> InitialWaterSurface
	{
		get
		{
			return InitialConditions?.InitialWaterSurface;
		}
		set
		{
			InitialConditions.InitialWaterSurface = value;
		}
	}

	public IResample<float> InitialVelocity
	{
		get
		{
			return InitialConditions?.InitialVelocity;
		}
		set
		{
			InitialConditions.InitialVelocity = value;
		}
	}

	public IResample<float> InitialVelocityX
	{
		get
		{
			return InitialConditions?.InitialVelocityX;
		}
		set
		{
			InitialConditions.InitialVelocityX = value;
		}
	}

	public IResample<float> InitialVelocityY
	{
		get
		{
			return InitialConditions?.InitialVelocityY;
		}
		set
		{
			InitialConditions.InitialVelocityY = value;
		}
	}

	protected override string GetHDF5Group()
	{
		return "/Boundary Conditions";
	}

	public override string GetCurrentFileFormatVersion()
	{
		return FileVersion;
	}

	public void DebugSetResult(Result res)
	{
		Result = res;
	}

	private BoundaryCondition(InstantiationMode createMode)
		: base(createMode)
	{
		InitLayers();
	}

	public BoundaryCondition(string filename, Project project = null)
		: base(filename, project)
	{
		InitLayers();
	}

	internal BoundaryCondition(Result res)
		: base(InstantiationMode.ReadFromDisk)
	{
		Result = res;
		Filename = res.Filename;
		_h5Wrapper = res.H5Wrapper;
		InitializeData();
	}

	public static BoundaryCondition CreateNew(string filename)
	{
		CoreLayerBase.VerifyCanCreateNew(filename);
		BoundaryCondition boundaryCondition = new BoundaryCondition(InstantiationMode.CreateNewFile);
		boundaryCondition.InitializeAndSaveAsNew(filename);
		return boundaryCondition;
	}

	public static BoundaryCondition CreateInMemory(string name = "")
	{
		BoundaryCondition boundaryCondition = new BoundaryCondition(InstantiationMode.InMemory);
		boundaryCondition._name = name;
		boundaryCondition.ForceLoadChildLayers();
		return boundaryCondition;
	}

	protected override void PreInitializeDataInternal()
	{
		InitLayers();
	}

	private void InitLayers()
	{
		if (MetStations == null)
		{
			MeteorologicStationLayer meteorologicStationLayer = (MetStations = new MeteorologicStationLayer(this));
		}
		if (Wind == null)
		{
			WindLayer windLayer = (Wind = new WindLayer(this));
		}
		if (Precipitation == null)
		{
			PrecipitationLayer precipitationLayer = (Precipitation = new PrecipitationLayer(this));
		}
		if (Evapotranspiration == null)
		{
			EvapotranspirationLayer evapotranspirationLayer = (Evapotranspiration = new EvapotranspirationLayer(this));
		}
		if (AirDensity == null)
		{
			AirDensityLayer airDensityLayer = (AirDensity = new AirDensityLayer(this));
		}
		if (AirTemperature == null)
		{
			AirTemperatureLayer airTemperatureLayer = (AirTemperature = new AirTemperatureLayer(this));
		}
		if (AirPressure == null)
		{
			AirPressureLayer airPressureLayer = (AirPressure = new AirPressureLayer(this));
		}
		if (Humidity == null)
		{
			HumidityLayer humidityLayer = (Humidity = new HumidityLayer(this));
		}
		if (BoundaryConditionLines == null)
		{
			BoundaryConditionLineLayer boundaryConditionLineLayer = (BoundaryConditionLines = new BoundaryConditionLineLayer(this));
		}
		if (InitialConditions == null)
		{
			InitialConditionsLayer initialConditionsLayer = (InitialConditions = new InitialConditionsLayer(this));
		}
	}

	private List<SpatialBoundaryConditionBase> AllSpatialBoundaryConditions()
	{
		return new List<SpatialBoundaryConditionBase> { Precipitation, Wind };
	}

	protected override void ForceLoadChildLayers()
	{
		_ = BoundaryConditionLines.Count;
	}

	protected override void LoadInternal(H5Reader hr)
	{
		MetStations.Load(hr);
		Wind.Load(hr);
		Precipitation.Load(hr);
		Evapotranspiration.Load(hr);
		AirDensity.Load(hr);
		AirTemperature.Load(hr);
		AirPressure.Load(hr);
		Humidity.Load(hr);
		BoundaryConditionLines.LoadMetadata(hr);
		InitialConditions.LoadMetadata(hr);
	}

	protected override bool NeedsSaveInternal()
	{
		if (BoundaryConditionLines.NeedsSave())
		{
			return true;
		}
		if (InitialConditions.NeedsSave())
		{
			return true;
		}
		return false;
	}

	protected override void CopyDataToInternal(H5Writer hw)
	{
		base.CopyDataToInternal(hw);
		foreach (SpatialBoundaryConditionBase allSpatialBoundaryCondition in AllSpatialBoundaryConditions())
		{
			allSpatialBoundaryCondition.CopyDataToInternal(hw);
		}
		InitialConditions.CopyDataToInternal(hw);
	}

	public static BoundaryCondition Load(H5Reader h5r, string name = "")
	{
		BoundaryCondition boundaryCondition = new BoundaryCondition(InstantiationMode.LoadFromDataProvider);
		boundaryCondition._h5Wrapper = new NativeResourceWrapper<H5Reader>(h5r);
		boundaryCondition._name = name;
		boundaryCondition.InitializeData();
		return boundaryCondition;
	}

	protected override void SaveInternal(H5Writer hw)
	{
		bool forceSaveIfFeaturesUnloaded = true;
		if (InstantiationMode == InstantiationMode.CreateNewFile)
		{
			forceSaveIfFeaturesUnloaded = false;
		}
		MetStations.Save(hw);
		Wind.Save(hw);
		Precipitation.Save(hw);
		Evapotranspiration.Save(hw);
		AirDensity.Save(hw);
		AirTemperature.Save(hw);
		AirPressure.Save(hw);
		Humidity.Save(hw);
		BoundaryConditionLines.Save(hw, forceSaveIfFeaturesUnloaded);
		InitialConditions.Save(hw);
	}

	public OperationResult ValidateTimeWindow(DateTime startTime, TimeSpan duration)
	{
		OperationResult operationResult = true;
		DateTime dateTime = startTime + duration;
		if (StartTime > startTime || EndTime < dateTime)
		{
			operationResult.AddFailure(MessageCodes.GetBCDoesntCoverTimeWindow(Name));
			return operationResult;
		}
		for (int i = 0; i < BoundaryConditionLines.Count; i++)
		{
			BoundaryConditionLine boundaryConditionLine = BoundaryConditionLines[i];
			OperationResult operationResult2 = boundaryConditionLine.CoversTimeWindow(startTime, duration);
			if (!operationResult2)
			{
				operationResult.AddFailure(operationResult2);
				operationResult.AddFailure(new Message("Boundary Condition Line \"{BCL}\" hydraulic data doesn't cover the necessary time-window.", boundaryConditionLine.Name));
			}
		}
		return operationResult;
	}

	public OperationResult PrepareForCompute(ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		OperationResult operationResult = true;
		if (!HasAnyInflowData())
		{
			operationResult.AddFailure(MessageCodes.GetNoInflowData(Name));
			return operationResult;
		}
		reporter.ReportMessage("Preparing Boundary Conditions...");
		OperationResult operationResult2 = PrepareSpatialBoundaryConditions(reporter);
		if (!operationResult2.Result)
		{
			operationResult.Result = false;
			operationResult.AddMessages(operationResult2);
		}
		OperationResult operationResult3 = PrepareBoundaryConditionLines();
		if (!operationResult3.Result)
		{
			operationResult.Result = false;
			operationResult.AddMessages(operationResult3);
		}
		OperationResult operationResult4 = PrepareInitialConditions(reporter);
		if (!operationResult4.Result)
		{
			operationResult.Result = false;
			operationResult.AddMessages(operationResult4);
		}
		return operationResult;
	}

	private bool HasAnyInflowData()
	{
		if (InitialWaterSurface != null)
		{
			return true;
		}
		foreach (BoundaryConditionLine boundaryConditionLine in BoundaryConditionLines)
		{
			if ((bool)boundaryConditionLine.TryGetHydraulicPayload(out var pl) && pl != null && !(pl.Variable == "Normal Depth"))
			{
				return true;
			}
		}
		foreach (SpatialBoundaryConditionBase allSpatialBoundaryCondition in AllSpatialBoundaryConditions())
		{
			if (allSpatialBoundaryCondition.IsReallyEnabledForCompute())
			{
				return true;
			}
		}
		return false;
	}

	private OperationResult PrepareSpatialBoundaryConditions(ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		OperationResult operationResult = true;
		foreach (SpatialBoundaryConditionBase allSpatialBoundaryCondition in AllSpatialBoundaryConditions())
		{
			if (!allSpatialBoundaryCondition.IsReallyEnabledForCompute())
			{
				continue;
			}
			OperationResult operationResult2 = allSpatialBoundaryCondition.PrepareForCompute();
			if (operationResult2 == null || operationResult2.Result)
			{
				pr.ReportMessage(allSpatialBoundaryCondition.LayerTypeName + " layer prepared successfully.");
				if (Project.Projection == null)
				{
					pr.ReportMessage("Warning: Project has no projection defined. Spatial boundary conditions may not behave correctly.");
				}
			}
			else
			{
				operationResult.AddFailure(operationResult2);
			}
		}
		return operationResult;
	}

	private OperationResult PrepareBoundaryConditionLines()
	{
		OperationResult operationResult = true;
		foreach (BoundaryConditionLine boundaryConditionLine in BoundaryConditionLines)
		{
			Polyline polyline = boundaryConditionLine.Polyline;
			if (polyline == null || polyline.Points.Count == 0 || polyline.Length == 0.0)
			{
				operationResult.AddFailure(MessageCodes.GetBCLBadPolyline(Name, boundaryConditionLine.Name));
			}
			if (!boundaryConditionLine.HasEnabledHydraulicPayload())
			{
				operationResult.AddMessage(MessageCodes.GetBCLNoHydraulicPayload(Name, boundaryConditionLine.Name));
				continue;
			}
			OperationResult operationResult2 = boundaryConditionLine.TryGetHydraulicPayload(out var pl);
			if (!operationResult2)
			{
				operationResult.AddFailures(operationResult2.Messages);
			}
			else
			{
				PrepareHydraulicPayload(operationResult, boundaryConditionLine, pl);
			}
		}
		return operationResult;
	}

	private void PrepareHydraulicPayload(OperationResult or, BoundaryConditionLine bcl, PayloadBase hydrPayload)
	{
		ExternalConnectionCollection connectionSource = bcl.BoundaryCondition.Project?.ConnectionSource;
		if (hydrPayload is ConstantPayload)
		{
			return;
		}
		if (hydrPayload is TimeSeriesPayload timeSeriesPayload)
		{
			if (!timeSeriesPayload.CoversTimeWindow(StartTime, Duration, StartTime))
			{
				or.AddFailure(MessageCodes.GetBCLDoesntCoverTimeWindow(Name, bcl.Name));
			}
			return;
		}
		if (hydrPayload is FunctionalPayload)
		{
			throw new NotImplementedException();
		}
		if (hydrPayload is ExternalPayload externalPayload)
		{
			OperationResult operationResult = externalPayload.TryEvaluate(connectionSource, bcl.Polyline, out var payload);
			if (!operationResult)
			{
				or.AddFailure(operationResult);
				return;
			}
			payload.Name = (hydrPayload.Name ?? "Payload") + " (IMPORTED)";
			bcl.Payloads.Add(payload);
			bcl.SetHydraulicPayload(payload);
			PrepareHydraulicPayload(or, bcl, payload);
			return;
		}
		if (hydrPayload is SpatiallyVariableTimeSeriesPayload spatiallyVariableTimeSeriesPayload)
		{
			if (!TryGetSpatiallyVariableBlockCount(spatiallyVariableTimeSeriesPayload.Variable, out var blockCount))
			{
				or.AddFailure($"Boundary Condition Line {bcl.Name.InDoubleQuotes()} has unsupported spatially variable payload variable {spatiallyVariableTimeSeriesPayload.Variable.InDoubleQuotes()}.");
				return;
			}
			int stationCountTimesBlockCount = spatiallyVariableTimeSeriesPayload.StationCount * blockCount;
			for (int i = 0; i < spatiallyVariableTimeSeriesPayload.Values.Count; i++)
			{
				if (spatiallyVariableTimeSeriesPayload.Values[i].Length != stationCountTimesBlockCount)
				{
					or.AddFailure($"Boundary Condition Line {bcl.Name.InDoubleQuotes()} spatially variable row {i} has {spatiallyVariableTimeSeriesPayload.Values[i].Length} values, expected {stationCountTimesBlockCount}.");
					return;
				}
			}
			if (!spatiallyVariableTimeSeriesPayload.CoversTimeWindow(StartTime, Duration, StartTime))
			{
				or.AddFailure(MessageCodes.GetBCLDoesntCoverTimeWindow(Name, bcl.Name));
			}
			return;
		}
		throw new NotImplementedException();
	}

	private static bool TryGetSpatiallyVariableBlockCount(string variable, out int blockCount)
	{
		if (variable.RoughlyEquals("Stage"))
		{
			blockCount = 1;
			return true;
		}
		if (variable.RoughlyEquals("StageVelocityVelocity"))
		{
			blockCount = 3;
			return true;
		}
		blockCount = 0;
		return false;
	}

	private OperationResult PrepareInitialConditions(ProgressReporter pr = null)
	{
		return InitialConditions.PrepareForCompute(pr);
	}

	public OperationResult InitializeSolver(Solver solver)
	{
		if (!IsResult)
		{
			throw new Exception("This Boundary Condition needs to be part of a prepared result.");
		}
		OperationResult operationResult = IngestBoundaryConditionLinesToEngine(solver);
		if (!operationResult.Result)
		{
			return operationResult;
		}
		return IngestSpatialBoundaryConditionsToEngine(solver.ComputeDriver);
	}

	public OperationResult InitializeComputeDriver(ComputeDriver computeDriver)
	{
		if (!IsResult)
		{
			throw new Exception("This Boundary Condition needs to be part of a prepared result.");
		}
		OperationResult operationResult = IngestBoundaryConditionLinesToEngine(computeDriver);
		if (!operationResult.Result)
		{
			return operationResult;
		}
		return IngestSpatialBoundaryConditionsToEngine(computeDriver);
	}

	private OperationResult IngestBoundaryConditionLinesToEngine(Solver solver)
	{
		return IngestBoundaryConditionLinesToEngine(solver.BoundaryConditions);
	}

	private OperationResult IngestBoundaryConditionLinesToEngine(ComputeDriver computeDriver)
	{
		return IngestBoundaryConditionLinesToEngine(computeDriver.BoundaryConditions);
	}

	public List<ProfileFaceRange> GetPerimeterFacesForBoundaryConditionLine(Mesh mesh, BoundaryConditionLine bcl)
	{
		Polyline magnetizedPline;
		return mesh.PerimeterFacesAlongPolyline(bcl.Polyline, out magnetizedPline);
	}

	public List<ProfileCellRange> GetCellsForInternalBoundaryConditionLine(Mesh mesh, BoundaryConditionLine bcl)
	{
		return mesh.CellsUnderPolyline(bcl.Polyline);
	}

	public OperationResult TryGetBCLineFlowTimeSeries(BoundaryConditionLine bcl, out double[] values, out IReadOnlyList<DateTime> times)
	{
		values = null;
		times = null;
		if (!IsResult)
		{
			return OperationResult.Fail("BC layer for " + Name + " has no result; cannot read mesh flow time series.");
		}
		OperationResult operationResult = Result.ComputeFlowTimeSeries(bcl.Polyline, out var flowTimeSeries);
		if (!operationResult)
		{
			return operationResult;
		}
		values = flowTimeSeries;
		times = Result.GetPostProcessor().Times;
		return true;
	}

	public OperationResult TryGetBCLineStageTimeSeries(BoundaryConditionLine bcl, out double[] values, out IReadOnlyList<DateTime> times)
	{
		values = null;
		times = null;
		if (!IsResult)
		{
			return OperationResult.Fail("BC layer for " + Name + " has no result; cannot read mesh stage time series.");
		}
		Mesh mesh = Result.Geometry.FlowAreaLayer.FirstOrDefault()?.Mesh;
		if (mesh == null)
		{
			return OperationResult.Fail("No mesh available for result " + Result.Name + ".");
		}
		Polyline polyline = bcl.Polyline;
		if (polyline == null || polyline.PointCount < 2)
		{
			return OperationResult.Fail("BC Line " + bcl.Name + " has no polyline.");
		}
		double length = polyline.Length;
		if (length <= 0.0)
		{
			return OperationResult.Fail("BC Line " + bcl.Name + " has zero length.");
		}
		double[] cumulativeSegmentLengths = polyline.CumulativeSegmentLengths();
		Point[] points = new Point[100];
		for (int i = 0; i < 100; i++)
		{
			double dist = ((i == 99) ? length : (length * (double)i / 99.0));
			points[i] = polyline.PointAtDistanceFast(dist, cumulativeSegmentLengths).Point;
		}
		PostProcessor postProcessor = Result.GetPostProcessor();
		postProcessor.EnsureDefaultDatasets();
		int profileCount = postProcessor.ProfileCount;
		Span<float> ensureCellMinElevationTable = Result.Geometry.HydraulicPropertyTables.EnsureCellMinElevationTable().Span;
		float[] floatValues = new float[100];
		for (int j = 0; j < 100; j++)
		{
			int cell = mesh.GetCell(points[j]);
			floatValues[j] = ((cell >= 0) ? ensureCellMinElevationTable[cell] : float.MinValue);
		}
		GeometryMapPoints mapPts = new GeometryMapPoints(postProcessor.Geometry, points.AsMemory());
		Memory<float>[] floatSpanArray = new Memory<float>[100];
		for (int k = 0; k < 100; k++)
		{
			floatSpanArray[k] = new float[profileCount];
		}
		WaterSurfaceMapper.ComputeTimeSeries(postProcessor, mapPts, floatValues.AsMemory(), floatSpanArray);
		double[] doubleValues = new double[profileCount];
		for (int l = 0; l < profileCount; l++)
		{
			double computed = 0.0;
			int ordinal = 0;
			for (int m = 0; m < 100; m++)
			{
				float computed2 = floatSpanArray[m].Span[l];
				if (computed2 != -9999f)
				{
					computed += (double)computed2;
					ordinal++;
				}
			}
			doubleValues[l] = ((ordinal > 0) ? (computed / (double)ordinal) : double.NaN);
		}
		values = doubleValues;
		times = postProcessor.Times;
		return true;
	}

	public OperationResult TryIdentifyInternalExternal(int bcLine, out List<ProfileCellRange> ifInternal, out List<ProfileFaceRange> ifExternal)
	{
		return TryIdentifyInternalExternal(BoundaryConditionLines[bcLine].Polyline, out ifInternal, out ifExternal);
	}

	public OperationResult TryIdentifyInternalExternal(Polyline polyline, out List<ProfileCellRange> ifInternal, out List<ProfileFaceRange> ifExternal)
	{
		Result result = Result;
		Geometry geometry = result.Geometry;
		_ = result.Plan;
		FlowArea flowArea = geometry.FlowAreaLayer.FirstOrDefault();
		Mesh mesh = flowArea?.Mesh;
		if (mesh == null || mesh.Perimeter == null)
		{
			ifInternal = null;
			ifExternal = null;
			return OperationResult.Fail(new Message("Could not load or find Mesh {MeshName}.", flowArea.Name));
		}
		ComplexPolygon perimeter = mesh.ComputeComplexPerimeter();
		return TryIdentifyInternalExternal(perimeter, polyline, mesh, out ifInternal, out ifExternal);
	}

	private OperationResult TryIdentifyInternalExternal(ComplexPolygon perimeter, Polyline pline, Mesh mesh, out List<ProfileCellRange> ifInternal, out List<ProfileFaceRange> ifExternal)
	{
		ifInternal = null;
		ifExternal = null;
		if (pline == null || pline.PointCount == 0)
		{
			return OperationResult.Fail("BC line has no polyline.");
		}
		IList<Polygon> recurseGetAllPolygons = perimeter.OuterRing.RecurseGetAllPolygons();
		Polyline magnetizedPline;
		foreach (Polygon recurseGetAllPolygon in recurseGetAllPolygons)
		{
			if (!(recurseGetAllPolygon.Area >= 0.0) && recurseGetAllPolygon.ContainsOrIntersects(pline))
			{
				ifExternal = mesh.PerimeterFacesAlongPolyline(pline, out magnetizedPline);
				return true;
			}
		}
		foreach (Polygon recurseGetAllPolygon2 in recurseGetAllPolygons)
		{
			if (!(recurseGetAllPolygon2.Area <= 0.0) && recurseGetAllPolygon2.ContainsFuzzy(pline))
			{
				ifInternal = mesh.CellsUnderPolyline(pline);
				return true;
			}
		}
		ifExternal = mesh.PerimeterFacesAlongPolyline(pline, out magnetizedPline);
		return true;
	}

	internal OperationResult IngestBoundaryConditionLinesToEngine(BoundaryConditionCollection bcs)
	{
		OperationResult operationResult = true;
		Result result = Result;
		Geometry geometry = result.Geometry;
		Plan plan = result.Plan;
		FlowArea flowArea = geometry.FlowAreaLayer.FirstOrDefault();
		Mesh mesh = flowArea?.Mesh;
		if (mesh == null || mesh.Perimeter == null)
		{
			operationResult.AddFailure(new Message("Could not load or find Mesh {MeshName}.", flowArea.Name));
			return operationResult;
		}
		mesh.ComputeComplexPerimeter();
		for (int i = 0; i < BoundaryConditionLines.Count; i++)
		{
			BoundaryConditionLine boundaryConditionLine = BoundaryConditionLines[i];
			Polyline polyline = boundaryConditionLine.Polyline;
			if (!boundaryConditionLine.HasEnabledHydraulicPayload())
			{
				continue;
			}
			OperationResult operationResult2 = TryIdentifyInternalExternal(polyline, out var ifInternal, out var ifExternal);
			if (!operationResult2)
			{
				operationResult.AddFailure(new Message("Could not attach BC Line[{{FID}}] ({{Name}}) to mesh.", i, boundaryConditionLine.Name));
				operationResult.AddFailure(operationResult2);
			}
			else if (ifInternal != null)
			{
				OperationResult operationResult3 = ProcessInternalBCLine(bcs, plan, boundaryConditionLine, ifInternal);
				if (!operationResult3)
				{
					operationResult.AddFailure(operationResult3);
				}
			}
			else if (ifExternal != null)
			{
				OperationResult operationResult4 = ProcessExternalBCLine(bcs, plan, boundaryConditionLine, ifExternal, mesh);
				if (!operationResult4)
				{
					operationResult.AddFailure(operationResult4);
				}
			}
			else
			{
				operationResult.AddFailure(new Message("Could not attach BC Line[{{FID}}] ({{Name}}) to mesh.", i, boundaryConditionLine.Name));
			}
		}
		return operationResult;
	}

	private OperationResult ProcessInternalBCLine(BoundaryConditionCollection bcs, Plan pl, BoundaryConditionLine bcl, List<ProfileCellRange> cellRanges)
	{
		OperationResult operationResult = bcl.TryGetHydraulicPayload(out var pl2);
		if (!operationResult)
		{
			return operationResult;
		}
		if (pl2.Variable != "Flow")
		{
			return OperationResult.Fail(new Message("Internal BC Line {Name} has variable {Variable}; only Flow is supported for internal boundary conditions.", bcl.Name, pl2.Variable));
		}
		double totalLength = cellRanges.Sum((ProfileCellRange cr) => cr.StationEnd - cr.StationStart);
		int[] cells = cellRanges.Select((ProfileCellRange cr) => cr.Cell).ToArray();
		double[] cellFractions = cellRanges.Select((ProfileCellRange cr) => (cr.StationEnd - cr.StationStart) / totalLength).ToArray();
		if (pl2 is ConstantPayload constantPayload)
		{
			bcs.SetInternalFlow(cells, cellFractions, new double[2] { -1E+32, 1E+32 }, new double[2] { constantPayload.Value, constantPayload.Value }, bcl.Name);
		}
		else
		{
			if (!(pl2 is TimeSeriesPayload { Values: var values } timeSeriesPayload))
			{
				return OperationResult.Fail(new Message("Internal BC Line {Name} has an unsupported payload type.", bcl.Name));
			}
			double[] doubleValues = values.ToArray();
			double[] relativeTimeSeconds = timeSeriesPayload.TimeSeriesDefinition.GetRelativeTimeSeconds(doubleValues.Length, pl.StartTime);
			bcs.SetInternalFlow(cells, cellFractions, relativeTimeSeconds, doubleValues, bcl.Name);
		}
		return true;
	}

	internal OperationResult ProcessExternalBCLine(BoundaryConditionCollection bcs, Plan pl, BoundaryConditionLine bcl, List<ProfileFaceRange> perimFaces, Mesh mesh)
	{
		if (perimFaces.Count == 0)
		{
			return OperationResult.Fail(MessageCodes.GetBCLNoBoundFaces(Name, bcl.Name));
		}
		OperationResult operationResult = bcl.TryGetHydraulicPayload(out var pl2);
		if (!operationResult)
		{
			return operationResult;
		}
		int[] faces = perimFaces.Select((ProfileFaceRange f) => f.Face).ToArray();
		bool flowDistributionMethodBclTerm = bcl.FlowDistributionMethod == BoundaryFlowDistributionMethod.LateralDistribution;
		float[] elevations = null;
		if (flowDistributionMethodBclTerm)
		{
			BoundaryNodeBedElevationSampler.TrySample(mesh, perimFaces, Result?.Geometry?.AssociatedTerrain, out elevations);
		}
		if (pl2.Variable == "Normal Depth")
		{
			if (pl2 is ConstantPayload constantPayload)
			{
				Ras.Engine.BoundaryCondition boundaryCondition = Ras.Engine.BoundaryCondition.ConstantFaceValue(faces, constantPayload.Value);
				boundaryCondition.Name = bcl.Name;
				ConfigureFlowDistribution(boundaryCondition, flowDistributionMethodBclTerm, elevations);
				bcs.NormalDepthBoundaryConditions.Add(boundaryCondition);
				return true;
			}
			return OperationResult.Fail("Normal Depth BCs may only be set to constant values currently.");
		}
		if (pl2 is ConstantPayload constantPayload2)
		{
			Ras.Engine.BoundaryCondition boundaryCondition2 = Ras.Engine.BoundaryCondition.ConstantFaceValue(faces, constantPayload2.Value);
			boundaryCondition2.Name = bcl.Name;
			ConfigureFlowDistribution(boundaryCondition2, flowDistributionMethodBclTerm, elevations);
			if (pl2.Variable == "Stage")
			{
				bcs.StageBoundaryConditions.Add(boundaryCondition2);
			}
			else
			{
				if (!(pl2.Variable == "Flow"))
				{
					throw new NotImplementedException("Hydraulic variable not recognized: " + pl2.Variable.InDoubleQuotes());
				}
				bcs.FlowBoundaryConditions.Add(boundaryCondition2);
			}
		}
		else if (pl2 is TimeSeriesPayload timeSeriesPayload)
		{
			Ras.Engine.BoundaryCondition boundaryCondition3 = new Ras.Engine.BoundaryCondition
			{
				Name = bcl.Name
			};
			ConfigureFlowDistribution(boundaryCondition3, flowDistributionMethodBclTerm, elevations);
			boundaryCondition3.Faces = faces;
			boundaryCondition3.FaceFractions = Enumerable.Repeat(1.0 / (double)faces.Length, faces.Length).ToArray();
			boundaryCondition3.Values = timeSeriesPayload.Values.ToArray();
			boundaryCondition3.Timesteps = timeSeriesPayload.TimeSeriesDefinition.GetRelativeTimeSeconds(boundaryCondition3.Values.Length, pl.StartTime);
			if (pl2.Variable == "Stage")
			{
				bcs.StageBoundaryConditions.Add(boundaryCondition3);
			}
			else
			{
				if (!(pl2.Variable == "Flow"))
				{
					throw new NotImplementedException("Hydraulic variable not recognized: " + pl2.Variable);
				}
				bcs.FlowBoundaryConditions.Add(boundaryCondition3);
			}
		}
		else
		{
			if (pl2 is RatingCurvePayload)
			{
				throw new NotImplementedException();
			}
			if (!(pl2 is SpatiallyVariableTimeSeriesPayload spatiallyVariableTimeSeriesPayload))
			{
				if (pl2 is FunctionalPayload)
				{
					throw new NotImplementedException();
				}
				_ = pl2 is ExternalPayload;
				throw new NotImplementedException();
			}
			double[] relativeTimeSeconds = spatiallyVariableTimeSeriesPayload.TimeSeriesDefinition.GetRelativeTimeSeconds(spatiallyVariableTimeSeriesPayload.Values.Count, pl.StartTime);
			int count = faces.Length;
			FaceStationSpan[] faceSpans = SpatiallyVariablePayloadInterpolator.FaceStationsAlongLine(mesh, bcl.Polyline, perimFaces);
			if (spatiallyVariableTimeSeriesPayload.Variable == "Stage" || spatiallyVariableTimeSeriesPayload.Variable == "Flow")
			{
				OperationResult operationResult2 = spatiallyVariableTimeSeriesPayload.ValidateForCompute();
				if (!operationResult2)
				{
					return operationResult2;
				}
				double[,] faceValues = SpatiallyVariablePayloadInterpolator.BuildFaceValues(spatiallyVariableTimeSeriesPayload, relativeTimeSeconds, faceSpans);
				Ras.Engine.BoundaryCondition boundaryCondition4 = Ras.Engine.BoundaryCondition.VariableFaceValues(faces, relativeTimeSeconds, faceValues);
				boundaryCondition4.Name = bcl.Name;
				ConfigureFlowDistribution(boundaryCondition4, flowDistributionMethodBclTerm, elevations);
				if (spatiallyVariableTimeSeriesPayload.Variable == "Stage")
				{
					bcs.VariableStageBoundaryConditions.Add(boundaryCondition4);
				}
				else
				{
					bcs.VariableFlowBoundaryConditions.Add(boundaryCondition4);
				}
				return true;
			}
			if (!spatiallyVariableTimeSeriesPayload.Variable.RoughlyEquals("StageVelocityVelocity"))
			{
				return OperationResult.Fail(new Message("Boundary Condition Line {Name} has unsupported payload variable {Variable}.", bcl.Name, spatiallyVariableTimeSeriesPayload.Variable));
			}
			int count2 = 3;
			double[,,] faceMultipleValues = new double[relativeTimeSeconds.Length, count, count2];
			double[] stations = spatiallyVariableTimeSeriesPayload.Stations.ToArray();
			for (int intervalIndex = 0; intervalIndex < relativeTimeSeconds.Length; intervalIndex++)
			{
				double[] payloadValues = spatiallyVariableTimeSeriesPayload.Values[intervalIndex].ToArray();
				double[] interpolateBlockToFaces = SpatiallyVariablePayloadInterpolator.InterpolateBlockToFaces(stations, payloadValues, faceSpans);
				double[] interpolateBlockToFacesArray = SpatiallyVariablePayloadInterpolator.InterpolateBlockToFaces(stations, payloadValues, faceSpans, spatiallyVariableTimeSeriesPayload.StationCount);
				double[] interpolateBlockToFaces2 = SpatiallyVariablePayloadInterpolator.InterpolateBlockToFaces(stations, payloadValues, faceSpans, spatiallyVariableTimeSeriesPayload.StationCount * 2);
				for (int intervalIndex2 = 0; intervalIndex2 < count; intervalIndex2++)
				{
					faceMultipleValues[intervalIndex, intervalIndex2, 0] = interpolateBlockToFaces[intervalIndex2];
					faceMultipleValues[intervalIndex, intervalIndex2, 1] = interpolateBlockToFacesArray[intervalIndex2];
					faceMultipleValues[intervalIndex, intervalIndex2, 2] = interpolateBlockToFaces2[intervalIndex2];
				}
			}
			Ras.Engine.BoundaryCondition boundaryCondition5 = new Ras.Engine.BoundaryCondition
			{
				Name = bcl.Name,
				Faces = faces,
				FaceFractions = Enumerable.Repeat(1.0 / (double)faces.Length, faces.Length).ToArray(),
				Timesteps = relativeTimeSeconds,
				FaceMultipleValues = faceMultipleValues
			};
			ConfigureFlowDistribution(boundaryCondition5, flowDistributionMethodBclTerm, elevations);
			bcs.SpatiallyVariableStageVelocityBoundaryConditions.Add(boundaryCondition5);
		}
		return true;
	}

	private static void ConfigureFlowDistribution(Ras.Engine.BoundaryCondition engineBc, bool useLateralDistribution, float[] boundaryNodeBedElevations)
	{
		engineBc.UseLateralDistribution = useLateralDistribution;
		engineBc.BoundaryNodeBedElevations = boundaryNodeBedElevations;
	}

	private OperationResult IngestSpatialBoundaryConditionsToEngine(ComputeDriver computeDriver)
	{
		if (!IsResult)
		{
			throw new Exception("Expected this BC to be part of a prepared result.");
		}
		OperationResult operationResult = true;
		foreach (SpatialBoundaryConditionBase allSpatialBoundaryCondition in AllSpatialBoundaryConditions())
		{
			if (allSpatialBoundaryCondition.IsEnabled)
			{
				OperationResult operationResult2 = allSpatialBoundaryCondition.InitializeComputeDriver(computeDriver.computeControl, computeDriver.BoundaryConditions);
				if (!operationResult2.Result)
				{
					operationResult.Result = false;
					operationResult2.CopyMessagesTo(operationResult);
				}
			}
		}
		return operationResult;
	}

	private static void MutateFlowTimeSeries(double[] data, double minValue, double multiplier)
	{
		double minValue2 = 0.0;
		if (minValue > 0.0)
		{
			minValue2 = minValue;
		}
		double multiplierTerm = 1.0;
		if (multiplier > 0.0)
		{
			multiplierTerm = multiplier;
		}
		if (minValue2 != 0.0 || multiplierTerm != 1.0)
		{
			for (int i = 0; i < data.Length; i++)
			{
				double computed = data[i];
				computed *= multiplierTerm;
				data[i] = Math.Max(computed, minValue2);
			}
		}
	}

	public void ApplyInitialWaterState(Mesh mesh, IResample<float> tr, MeshWaterState init, ProgressReporter reporter)
	{
		if (InitialWaterSurface != null)
		{
			init.SetInitialStageMultisample(InitialWaterSurface, tr, reporter);
		}
		else
		{
			List<Tuple<double, List<int>>> valueTupleList = new List<Tuple<double, List<int>>>();
			foreach (BoundaryConditionLine boundaryConditionLine in BoundaryConditionLines)
			{
				if (!boundaryConditionLine.TryGetHydraulicPayload(out var pl) || pl.Variable != "Stage")
				{
					continue;
				}
				Polyline polyline = boundaryConditionLine.Polyline;
				List<int> intList = new List<int>();
				Polyline magnetizedPline;
				foreach (int ordinal in from f in mesh.PerimeterFacesAlongPolyline(polyline, out magnetizedPline)
					select f.Face)
				{
					Face face = mesh.Faces[ordinal];
					intList.Add(face.cellA);
				}
				double plConstantPayloadTerm = pl.Switch((ConstantPayload cpl) => cpl.Value, (TimeSeriesPayload tspl) => tspl.Values.Span[0], (RatingCurvePayload rcpl) => double.NaN, (FunctionalPayload fpl) => double.NaN, (ExternalPayload epl) => double.NaN, (SpatiallyVariableTimeSeriesPayload svpl) => double.NaN);
				if (!double.IsNaN(plConstantPayloadTerm))
				{
					valueTupleList.Add(new Tuple<double, List<int>>(plConstantPayloadTerm, intList));
				}
			}
			if (valueTupleList.Count > 0)
			{
				init.SetFloodfillStage(valueTupleList);
			}
			else
			{
				init.SetDry();
			}
		}
		if ((InitialVelocityX != null) & (InitialVelocityY != null))
		{
			init.SetInitialVelocityFromFaceCenters(InitialVelocityX, InitialVelocityY, reporter);
		}
	}

	internal void NotifyMetStationRemoved(MeteorologicStation station)
	{
		foreach (SpatialBoundaryConditionBase layer in new SpatialBoundaryConditionBase[] { Precipitation, Evapotranspiration, Wind, AirPressure, AirDensity })
		{
			layer.NotifyMetStationRemoved(station);
		}
	}

	public OperationResult DataEquals(BoundaryCondition other)
	{
		OperationResult operationResult = true;
		if (!StringHelpers.StringEquals(Name, other.Name))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("BoundaryCondition", "Name", Name, other.Name));
		}
		if (!StringHelpers.StringEquals(Description, other.Description))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("BoundaryCondition", "Description", Description, other.Description));
		}
		if (StartTime != other.StartTime)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("BoundaryCondition", "StartTime", StartTime, other.StartTime));
		}
		if (EndTime != other.EndTime)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("BoundaryCondition", "EndTime", EndTime, other.EndTime));
		}
		if (WarmupTime != other.WarmupTime)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("BoundaryCondition", "WarmupTime", WarmupTime, other.WarmupTime));
		}
		OperationResult operationResult2 = BoundaryConditionLines.DataEquals(other.BoundaryConditionLines);
		if (!operationResult2)
		{
			operationResult.AddFailure(operationResult2);
		}
		return operationResult;
	}

	public List<string> AllExternalConnectionsNeeded()
	{
		List<string> stringList = new List<string>();
		for (int i = 0; i < BoundaryConditionLines.Count; i++)
		{
			foreach (PayloadBase payload in BoundaryConditionLines[i].Payloads)
			{
				if (payload is ExternalPayload externalPayload)
				{
					stringList.Add(externalPayload.ExternalConnection);
				}
			}
		}
		return stringList;
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
			if (h5r.ExistsAsGroup("/Boundary Conditions"))
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

	public static string MakeFilename(string projectDir, string bcName)
	{
		return Project.MakeFilename<BoundaryCondition>(projectDir, bcName, ".h5");
	}
}
