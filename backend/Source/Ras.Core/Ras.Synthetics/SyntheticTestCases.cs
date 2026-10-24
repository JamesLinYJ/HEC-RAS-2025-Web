using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Ras.Hydraulics;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Utility.Logging;
using Utility.Memory;

namespace Ras.Synthetics;

public static class SyntheticTestCases
{
	private readonly struct AnalyticalSurfaces(ITimeSeriesResampler<float> waterSurface, ITimeSeriesResampler<float> velocityMagnitude, ITimeSeriesResampler<float> velocityX, ITimeSeriesResampler<float> velocityY)
	{
		public readonly ITimeSeriesResampler<float> WaterSurface = waterSurface;

		public readonly ITimeSeriesResampler<float> VelocityMagnitude = velocityMagnitude;

		public readonly ITimeSeriesResampler<float> VelocityX = velocityX;

		public readonly ITimeSeriesResampler<float> VelocityY = velocityY;

		public bool HasAny
		{
			get
			{
				if (WaterSurface == null && VelocityMagnitude == null && VelocityX == null)
				{
					return VelocityY != null;
				}
				return true;
			}
		}
	}

	private readonly struct GeometryGroup(Geometry geometry, Extent extendedExtent, Extent systemExtent)
	{
		public readonly Geometry Geometry = geometry;

		public readonly Extent ExtendedExtent = extendedExtent;

		public readonly Extent SystemExtent = systemExtent;
	}

	public const int DefaultTerrainCellCount = 4194304;

	public const int DefaultAnalyticalCellCount = 262144;

	public static Project CreateSyntheticTestCase(ISyntheticTestCaseParams synth, out ITimeSeriesResampler<float> analyticalStageResampler, out ITimeSeriesResampler<float> analyticalVelocityResampler, out ITimeSeriesResampler<float> analyticalVelocityXResampler, out ITimeSeriesResampler<float> analyticalVelocityYResampler)
	{
		Project project = NewProject(synth);
		ValidateParams(synth);
		Terrain terrain = BuildTerrain(project, synth, "Terrain");
		NValueLayer orAddNValueLayer = GetOrAddNValueLayer(project, synth.NValue, new Dictionary<string, NValueLayer>());
		GeometryGroup geometryGroup = BuildGeometryNv(project, synth, terrain, orAddNValueLayer, "Base Geometry");
		Plan plan = AddPlanForCase(bc: BuildBoundaryCondition(project, synth, geometryGroup, "Boundary"), prj: project, group: geometryGroup, synth: synth, planName: "Base Plan", timeWindowMode: PlanTimeWindowMode.Plan);
		AssignAnalyticalSurfaces(plan, BuildAnalyticalSurfaces(synth, geometryGroup.SystemExtent, plan), null);
		analyticalStageResampler = plan.AnalyticalWaterSurface;
		analyticalVelocityResampler = plan.AnalyticalVelocityMagnitude;
		analyticalVelocityXResampler = plan.AnalyticalVelocityX;
		analyticalVelocityYResampler = plan.AnalyticalVelocityY;
		return project;
	}

	public static Project CreateSyntheticProject(IReadOnlyList<PlanCase> cases, out IReadOnlyDictionary<string, ISyntheticTestCaseParams> measuredSourcesByGeometry, Func<ISyntheticTestCaseParams, string> keyFor = null)
	{
		if (cases == null || cases.Count == 0)
		{
			throw new ArgumentException("At least one PlanCase is required.", "cases");
		}
		if (keyFor == null)
		{
			keyFor = GeometryOnlyKey;
		}
		Project project = NewProject(cases[0].Params);
		Dictionary<string, Terrain> terrainByString = new Dictionary<string, Terrain>();
		Dictionary<string, NValueLayer> pool = new Dictionary<string, NValueLayer>();
		Dictionary<string, GeometryGroup> geometryGroupByString = new Dictionary<string, GeometryGroup>();
		Dictionary<string, BoundaryCondition> boundaryConditionByString = new Dictionary<string, BoundaryCondition>();
		Dictionary<string, ISyntheticTestCaseParams> measuredSourcesByGeometry2 = new Dictionary<string, ISyntheticTestCaseParams>();
		Dictionary<string, (AnalyticalSurfaces, string)> valueTupleByString = new Dictionary<string, (AnalyticalSurfaces, string)>();
		HashSet<string> used = new HashSet<string>();
		int ordinal = 0;
		int ordinal2 = 0;
		int ordinal3 = 0;
		foreach (PlanCase @case in cases)
		{
			ValidateParams(@case.Params);
			string key = (string.IsNullOrEmpty(@case.TerrainGroupKey) ? DefaultTerrainGroupKey(@case.Params) : @case.TerrainGroupKey);
			if (!terrainByString.TryGetValue(key, out var buildTerrainProjectTerm))
			{
				ordinal++;
				string message = ((ordinal == 1) ? "" : $" {ordinal}");
				buildTerrainProjectTerm = (terrainByString[key] = BuildTerrain(project, @case.Params, "Terrain" + message));
			}
			string keyFor2 = (string.IsNullOrEmpty(@case.GeometryGroupKey) ? keyFor(@case.Params) : @case.GeometryGroupKey);
			if (!geometryGroupByString.TryGetValue(keyFor2, out var keyFor2BuildGeometryNvTerm))
			{
				ordinal2++;
				string message2 = ((ordinal2 == 1) ? "" : $" {ordinal2}");
				NValueLayer orAddNValueLayer = GetOrAddNValueLayer(project, @case.Params.NValue, pool);
				keyFor2BuildGeometryNvTerm = (geometryGroupByString[keyFor2] = BuildGeometryNv(project, @case.Params, buildTerrainProjectTerm, orAddNValueLayer, "Base Geometry" + message2));
				measuredSourcesByGeometry2[keyFor2BuildGeometryNvTerm.Geometry.Name] = @case.Params;
			}
			string keyFor2IsNullOrEmptyTerm = keyFor2 + "||bc:" + (string.IsNullOrEmpty(@case.BoundaryConditionGroupKey) ? DefaultBoundaryGroupKey(@case.Params) : @case.BoundaryConditionGroupKey);
			bool condition = false;
			if (!boundaryConditionByString.TryGetValue(keyFor2IsNullOrEmptyTerm, out var buildBoundaryConditionProjectTerm))
			{
				ordinal3++;
				string message3 = ((ordinal3 == 1) ? "" : $" {ordinal3}");
				string boundaryName = (string.IsNullOrEmpty(@case.BoundaryConditionName) ? ("Boundary" + message3) : @case.BoundaryConditionName);
				buildBoundaryConditionProjectTerm = (boundaryConditionByString[keyFor2IsNullOrEmptyTerm] = BuildBoundaryCondition(project, @case.Params, keyFor2BuildGeometryNvTerm, boundaryName));
				condition = true;
			}
			Plan plan = AddPlanForCase(project, keyFor2BuildGeometryNvTerm, buildBoundaryConditionProjectTerm, @case.Params, @case.PlanName, @case.TimeWindowMode);
			if (condition)
			{
				@case.ConfigureBoundaryCondition?.Invoke(buildBoundaryConditionProjectTerm);
			}
			plan.EquationSet = @case.EquationSet;
			plan.TimeStepping = @case.TimeStepping;
			@case.ConfigurePlan?.Invoke(plan);
			string key2 = (string.IsNullOrEmpty(@case.AnalyticalGroupKey) ? keyFor2IsNullOrEmptyTerm : @case.AnalyticalGroupKey) + "|t:" + OutputTimeSignature(plan);
			if (valueTupleByString.TryGetValue(key2, out var localValue))
			{
				AssignAnalyticalSurfaces(plan, localValue.Item1, localValue.Item2);
				continue;
			}
			AnalyticalSurfaces analyticalSurfaces = BuildAnalyticalSurfaces(@case.Params, keyFor2BuildGeometryNvTerm.SystemExtent, plan);
			if (analyticalSurfaces.HasAny)
			{
				string uniqueName = UniqueName(keyFor2BuildGeometryNvTerm.Geometry.Name, used);
				valueTupleByString[key2] = (analyticalSurfaces, uniqueName);
				AssignAnalyticalSurfaces(plan, analyticalSurfaces, uniqueName);
			}
		}
		measuredSourcesByGeometry = measuredSourcesByGeometry2;
		return project;
	}

	public static string DefaultGeometryGroupKey(ISyntheticTestCaseParams synth)
	{
		StringBuilder stringBuilder = new StringBuilder();
		AppendGeometryKey(stringBuilder, synth);
		AppendBoundaryKey(stringBuilder, synth);
		return stringBuilder.ToString();
	}

	public static string GeometryOnlyKey(ISyntheticTestCaseParams synth)
	{
		StringBuilder stringBuilder = new StringBuilder();
		AppendGeometryKey(stringBuilder, synth);
		return stringBuilder.ToString();
	}

	public static string DefaultTerrainGroupKey(ISyntheticTestCaseParams synth)
	{
		StringBuilder stringBuilder = new StringBuilder();
		AppendTerrainKey(stringBuilder, synth);
		return stringBuilder.ToString();
	}

	public static string DefaultBoundaryGroupKey(ISyntheticTestCaseParams synth)
	{
		StringBuilder stringBuilder = new StringBuilder();
		AppendBoundaryKey(stringBuilder, synth);
		return stringBuilder.ToString();
	}

	private static void AppendGeometryKey(StringBuilder sb, ISyntheticTestCaseParams synth)
	{
		sb.Append(synth.GetType().FullName);
		sb.Append('|').Append(synth.Units);
		Extent systemExtent = synth.SystemExtent;
		sb.Append("|ext:").Append(Fmt(systemExtent.MinX)).Append(',')
			.Append(Fmt(systemExtent.MinY))
			.Append(',')
			.Append(Fmt(systemExtent.MaxX))
			.Append(',')
			.Append(Fmt(systemExtent.MaxY));
		sb.Append("|n:").Append(Fmt(synth.NValue));
		if (synth is BasicRectangleParams basicRectangleParams)
		{
			sb.Append("|rect:").Append(basicRectangleParams.CellsWide).Append('x')
				.Append(basicRectangleParams.CellsTall)
				.Append(':')
				.Append(Fmt(basicRectangleParams.CellSize))
				.Append(':')
				.Append(Fmt(basicRectangleParams.CellSizeX))
				.Append(':')
				.Append(Fmt(basicRectangleParams.Slope));
		}
	}

	private static void AppendTerrainKey(StringBuilder sb, ISyntheticTestCaseParams synth)
	{
		sb.Append(synth.GetType().FullName);
		sb.Append('|').Append(synth.Units);
		Extent systemExtent = synth.SystemExtent;
		sb.Append("|ext:").Append(Fmt(systemExtent.MinX)).Append(',')
			.Append(Fmt(systemExtent.MinY))
			.Append(',')
			.Append(Fmt(systemExtent.MaxX))
			.Append(',')
			.Append(Fmt(systemExtent.MaxY));
		if (synth is BasicRectangleParams basicRectangleParams)
		{
			sb.Append("|slope:").Append(Fmt(basicRectangleParams.Slope));
		}
	}

	private static void AppendBoundaryKey(StringBuilder sb, ISyntheticTestCaseParams synth)
	{
		if (synth is InOutPlanarParams inOutPlanarParams)
		{
			sb.Append("|io:").Append(Fmt(inOutPlanarParams.UpstreamFlow)).Append(':')
				.Append(Fmt(inOutPlanarParams.DownstreamStage))
				.Append(':')
				.Append(Fmt(inOutPlanarParams.UpstreamEGSlope))
				.Append(':')
				.Append(Fmt(inOutPlanarParams.RampSeconds));
		}
	}

	private static string Fmt(double v)
	{
		return v.ToString("G9", CultureInfo.InvariantCulture);
	}

	private static Project NewProject(ISyntheticTestCaseParams synth)
	{
		Project project = Project.CreateInMemory();
		Projection projection = synth.GetProjection();
		if (projection == null)
		{
			project.ProjectionDefinition = ProjectionDefinition.Local(synth.Units);
		}
		else
		{
			project.DebugSetProjection(projection);
		}
		return project;
	}

	private static void ValidateParams(ISyntheticTestCaseParams synth)
	{
		if (synth.SolveDt <= 0.0 || synth.SolveDuration <= 0.0)
		{
			throw new Exception("Didnt set solution parameters");
		}
		if (synth.SystemExtent.IsNull())
		{
			throw new Exception("Invalid system extent");
		}
	}

	private static Terrain BuildTerrain(Project prj, ISyntheticTestCaseParams synth, string terrainName)
	{
		Terrain terrain = prj.AddNewTerrain(terrainName);
		Extent ext = synth.SystemExtent.Extend(0.01 * synth.SystemExtent.MaxDimension);
		IResample<float> resample = synth.TerrainResampler();
		if (resample == null)
		{
			resample = new FunctionalResampler<float>(ext, -9999f, synth.TerrainElevation);
		}
		terrain.DebugAdd(resample);
		return terrain;
	}

	private static GeometryGroup BuildGeometryNv(Project prj, ISyntheticTestCaseParams synth, Terrain terrain, NValueLayer nValueLayer, string geometryName)
	{
		Extent systemExtent = synth.SystemExtent;
		Extent extendedExtent = systemExtent.Extend(0.01 * systemExtent.MaxDimension);
		Geometry geometry = prj.AddNewGeometry(geometryName);
		Mesh mesh = synth.CreateMesh();
		if (mesh != null)
		{
			FlowArea flowArea = new FlowArea(mesh, "Base Mesh");
			geometry.FlowAreaLayer.Add(flowArea);
		}
		if (synth.TryCreateTopology(out var topology))
		{
			geometry.ConceptualMeshLayer.Topology = topology;
		}
		GeometryAssociation geometryAssociation = prj.GetGeometryAssociation(geometry);
		geometryAssociation.Terrain = terrain;
		geometryAssociation.NValueLayer = nValueLayer;
		return new GeometryGroup(geometry, extendedExtent, systemExtent);
	}

	private static NValueLayer GetOrAddNValueLayer(Project prj, float nValue, Dictionary<string, NValueLayer> pool)
	{
		string key = Fmt(nValue);
		if (!pool.TryGetValue(key, out var pool2))
		{
			pool2 = NValueLayer.CreateInMemory(nValue);
			prj.DebugAddNewNValueLayer(pool2);
			pool[key] = pool2;
		}
		return pool2;
	}

	private static BoundaryCondition BuildBoundaryCondition(Project prj, ISyntheticTestCaseParams synth, GeometryGroup group, string boundaryName)
	{
		BoundaryCondition boundaryCondition = prj.AddNewBoundaryCondition(boundaryName);
		Extent extendedExtent = group.ExtendedExtent;
		List<BoundaryConditionLine> bCLines = synth.GetBCLines(boundaryCondition);
		if (bCLines != null)
		{
			boundaryCondition.BoundaryConditionLines.AddRange(bCLines);
		}
		synth.ConfigurePrecipitation(boundaryCondition);
		Func<Point, float> initStage = synth.InitialStageFunction();
		if (initStage != null)
		{
			boundaryCondition.InitialWaterSurface = new FunctionalResampler<float>(extendedExtent, -9999f, (Point pt) => initStage(pt));
		}
		Func<Point, float> initVelocity = synth.InitialVelocityMagnitudeFunction();
		if (initVelocity != null)
		{
			boundaryCondition.InitialVelocity = new FunctionalResampler<float>(extendedExtent, -9999f, (Point pt) => initVelocity(pt));
		}
		Func<Point, float> initVelocityX = synth.InitialVelocityXFunction();
		if (initVelocityX != null)
		{
			boundaryCondition.InitialVelocityX = new FunctionalResampler<float>(extendedExtent, -9999f, (Point pt) => initVelocityX(pt));
		}
		Func<Point, float> initVelocityY = synth.InitialVelocityYFunction();
		if (initVelocityY != null)
		{
			boundaryCondition.InitialVelocityY = new FunctionalResampler<float>(extendedExtent, -9999f, (Point pt) => initVelocityY(pt));
		}
		return boundaryCondition;
	}

	private static Plan AddPlanForCase(Project prj, GeometryGroup group, BoundaryCondition bc, ISyntheticTestCaseParams synth, string planName, PlanTimeWindowMode timeWindowMode)
	{
		Plan plan = prj.AddNewPlan(planName);
		plan.ComputeTimestep = TimeSpan.FromSeconds(synth.SolveDt);
		plan.InternalComputeDurationSeconds = synth.SolveDuration;
		plan.TimeWindowMode = timeWindowMode;
		plan.SetOutputFrequency(synth.ReportFrequency);
		plan.CoreCount = 8;
		plan.Description = $"Synthetic {synth.GetType().Name} plan. Compute timestep {synth.SolveDt} s, duration {synth.SolveDuration} s, output every {synth.ReportFrequency} timestep(s).";
		synth.InitPlanOptions(plan);
		PlanAssociation planAssociation = prj.GetPlanAssociation(plan);
		planAssociation.Geometry = group.Geometry;
		planAssociation.BoundaryCondition = bc;
		bc.StartTime = plan.InternalStartTime;
		bc.EndTime = plan.InternalStartTime.AddSeconds(synth.SolveDuration);
		return plan;
	}

	private static AnalyticalSurfaces BuildAnalyticalSurfaces(ISyntheticTestCaseParams synth, Extent ext, Plan pl)
	{
		double[] time = pl.OutputTimestepsInSimulationTime();
		return new AnalyticalSurfaces(Build(synth.AnalyticalStageFunction()), Build(synth.AnalyticalVelocityMagnitudeFunction()), Build(synth.AnalyticalVelocityXFunction()), Build(synth.AnalyticalVelocityYFunction()));
		ITimeSeriesResampler<float> Build(Func<Point, double, float> fn)
		{
			if (fn != null)
			{
				return BuildAnalyticalResampler(time, ext, pl.StartTime, fn);
			}
			return null;
		}
	}

	private static void AssignAnalyticalSurfaces(Plan pl, AnalyticalSurfaces surfaces, string groupName)
	{
		pl.AnalyticalWaterSurface = surfaces.WaterSurface;
		pl.AnalyticalVelocityMagnitude = surfaces.VelocityMagnitude;
		pl.AnalyticalVelocityX = surfaces.VelocityX;
		pl.AnalyticalVelocityY = surfaces.VelocityY;
		pl.AnalyticalGroupName = groupName;
	}

	private static string OutputTimeSignature(Plan pl)
	{
		double[] outputTimestepsInSimulationTime = pl.OutputTimestepsInSimulationTime();
		double outputTimestepsInSimulationTimeStep = ((outputTimestepsInSimulationTime.Length > 1) ? (outputTimestepsInSimulationTime[1] - outputTimestepsInSimulationTime[0]) : 0.0);
		return $"{outputTimestepsInSimulationTime.Length}@{outputTimestepsInSimulationTimeStep.ToString("G9", CultureInfo.InvariantCulture)}";
	}

	private static string UniqueName(string baseName, HashSet<string> used)
	{
		string baseName2 = baseName;
		int unity = 1;
		while (!used.Add(baseName2))
		{
			baseName2 = $"{baseName} {++unity}";
		}
		return baseName2;
	}

	private static ITimeSeriesResampler<float> BuildAnalyticalResampler(double[] time, Extent ext, DateTime startTime, Func<Point, double, float> analytical)
	{
		return new MultiBandFunctionalResampler<float>(time.Length, ext, -9999f, (int tIdx, Point pt) => analytical(pt, (float)time[tIdx])).AsTimeSeriesResampler(time.Select((double tSec) => startTime.Add(TimeSpan.FromSeconds(tSec))).ToList());
	}

	public static Project Save(ISyntheticTestCaseParams synth, string directory, int terrainCellCount = 4194304, int analyticalCellCount = 262144)
	{
		ITimeSeriesResampler<float> analyticalStageResampler;
		ITimeSeriesResampler<float> analyticalVelocityResampler;
		ITimeSeriesResampler<float> analyticalVelocityXResampler;
		ITimeSeriesResampler<float> analyticalVelocityYResampler;
		return SaveProject(CreateSyntheticTestCase(synth, out analyticalStageResampler, out analyticalVelocityResampler, out analyticalVelocityXResampler, out analyticalVelocityYResampler), directory, synth.GetType().Name, terrainCellCount, analyticalCellCount);
	}

	public static Project SaveProject(Project prj, string directory, string projectName, int terrainCellCount = 4194304, int analyticalCellCount = 262144)
	{
		return SaveProject(prj, directory, projectName, null, terrainCellCount, analyticalCellCount);
	}

	public static Project SaveProject(Project prj, string directory, string projectName, IReadOnlyDictionary<string, ISyntheticTestCaseParams> measuredSourcesByGeometry, int terrainCellCount = 4194304, int analyticalCellCount = 262144, IProgress<double> rasterProgress = null)
	{
		if (prj.Terrains.Count > 1)
		{
			throw new NotImplementedException("SaveProject supports a single shared terrain; multi-geometry projects need per-terrain export.");
		}
		Terrain terrain = prj.Terrains.First();
		Extent validExtent = terrain.ValidExtent;
		string path = Path.Combine(directory, "Temp");
		if (!Directory.Exists(path))
		{
			Directory.CreateDirectory(path);
		}
		try
		{
			string path2 = Path.Combine(path, "Terrain.tif");
			RasterDefinition exportRdef = RasterDefinition.ComputeBoundingRaster(terrain.ValidExtent, terrainCellCount);
			TiffExportEngine.ExportWithOverviews(path2, terrain, exportRdef);
			string filename = TerrainV6.CreateSingleFileTerrain(path2, "Terrain");
			prj.RemoveTerrain(terrain);
			terrain = new Terrain(filename);
			prj.AddTerrain(terrain);
			foreach (Geometry geometry in prj.Geometries)
			{
				prj.GetGeometryAssociation(geometry).Terrain = terrain;
			}
			ExportAnalyticalRasters(prj, directory, validExtent, analyticalCellCount, rasterProgress);
			if (measuredSourcesByGeometry != null)
			{
				foreach (KeyValuePair<string, ISyntheticTestCaseParams> measuredSourcesByGeometry2 in measuredSourcesByGeometry)
				{
					ExportMeasuredSamples(measuredSourcesByGeometry2.Value, directory);
					ExportShapefiles(measuredSourcesByGeometry2.Value, directory);
				}
			}
			return new Project(prj.SaveAs(directory, projectName));
		}
		finally
		{
			try
			{
				Directory.Delete(path, recursive: true);
			}
			catch
			{
			}
		}
	}

	private static void ExportAnalyticalRasters(Project prj, string directory, Extent extent, int analyticalCellCount, IProgress<double> progress = null)
	{
		RasterDefinition rdef = RasterDefinition.ComputeBoundingRaster(extent, analyticalCellCount);
		List<Plan> plans = new List<Plan>();
		HashSet<string> stringSet = new HashSet<string>();
		foreach (Plan plan in prj.Plans)
		{
			if (stringSet.Add(AnalyticalFolderName(plan)))
			{
				plans.Add(plan);
			}
		}
		int total = 0;
		foreach (Plan plan2 in plans)
		{
			int timesLength = plan2.OutputTimestepsInSimulationTime().Length;
			total += CountSurface(plan2.AnalyticalWaterSurface, timesLength) + CountSurface(plan2.AnalyticalVelocityMagnitude, timesLength) + CountSurface(plan2.AnalyticalVelocityX, timesLength) + CountSurface(plan2.AnalyticalVelocityY, timesLength);
		}
		if (total == 0)
		{
			Console.WriteLine("No analytical solution rasters to export.");
			return;
		}
		Console.WriteLine($"Exporting {total} analytical solution raster(s) across {plans.Count(HasAnalytical)} group(s)...");
		int rastersWritten = 0;
		foreach (Plan plan3 in plans)
		{
			string subfolder = AnalyticalFolderName(plan3);
			double[] times = plan3.OutputTimestepsInSimulationTime();
			ExportAnalyticalSurface(plan3.AnalyticalWaterSurface, "WSE", subfolder, times, rdef, directory, ReportOne);
			ExportAnalyticalSurface(plan3.AnalyticalVelocityMagnitude, "VelMag", subfolder, times, rdef, directory, ReportOne);
			ExportAnalyticalSurface(plan3.AnalyticalVelocityX, "VelX", subfolder, times, rdef, directory, ReportOne);
			ExportAnalyticalSurface(plan3.AnalyticalVelocityY, "VelY", subfolder, times, rdef, directory, ReportOne);
		}
		progress?.Report(1.0);
		Console.WriteLine($"Exported {rastersWritten} analytical solution raster(s).");
		void ReportOne()
		{
			rastersWritten++;
			progress?.Report((double)rastersWritten / (double)total);
		}
	}

	private static string AnalyticalFolderName(Plan pl)
	{
		if (!string.IsNullOrEmpty(pl.AnalyticalGroupName))
		{
			return pl.AnalyticalGroupName;
		}
		return pl.Name;
	}

	private static bool HasAnalytical(Plan pl)
	{
		if (pl.AnalyticalWaterSurface == null && pl.AnalyticalVelocityMagnitude == null && pl.AnalyticalVelocityX == null)
		{
			return pl.AnalyticalVelocityY != null;
		}
		return true;
	}

	private static int CountSurface(ITimeSeriesResampler<float> resampler, int timesLength)
	{
		if (resampler != null)
		{
			return Math.Min(timesLength, resampler.ProfileCount);
		}
		return 0;
	}

	private static int ExportAnalyticalSurface(ITimeSeriesResampler<float> resampler, string variable, string subfolder, double[] times, RasterDefinition rdef, string directory, Action reportOne = null)
	{
		if (resampler == null)
		{
			return 0;
		}
		string path = Path.Combine(directory, "Analytical", subfolder);
		if (!Directory.Exists(path))
		{
			Directory.CreateDirectory(path);
		}
		int count = Math.Min(times.Length, resampler.ProfileCount);
		for (int i = 0; i < count; i++)
		{
			TiffExportEngine.Export(Path.Combine(path, $"{variable}_t{times[i]:0}.tif"), resampler.AsSingleProfile(i), rdef);
			reportOne?.Invoke();
		}
		return count;
	}

	private static void ExportMeasuredSamples(ISyntheticTestCaseParams synth, string directory)
	{
		IReadOnlyList<MeasuredVelocitySample> measuredVelocitySamples = synth.GetMeasuredVelocitySamples();
		IReadOnlyList<MeasuredStageSample> measuredStageSamples = synth.GetMeasuredStageSamples();
		bool hasMeasuredVelocitySamples = measuredVelocitySamples != null && measuredVelocitySamples.Count > 0;
		bool hasMeasuredStageSamples = measuredStageSamples != null && measuredStageSamples.Count > 0;
		if (!hasMeasuredVelocitySamples && !hasMeasuredStageSamples)
		{
			return;
		}
		string path = Path.Combine(directory, "Data");
		Directory.CreateDirectory(path);
		if (hasMeasuredVelocitySamples)
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append("# time=-1 denotes steady-state\n");
			stringBuilder.Append("x,y,time,vx,vy,speed\n");
			foreach (MeasuredVelocitySample measuredVelocitySample in measuredVelocitySamples)
			{
				double x = measuredVelocitySample.Velocity.X;
				double y = measuredVelocitySample.Velocity.Y;
				double v = Math.Sqrt(x * x + y * y);
				stringBuilder.Append(Inv(measuredVelocitySample.Location.X)).Append(',').Append(Inv(measuredVelocitySample.Location.Y))
					.Append(',')
					.Append(Inv(measuredVelocitySample.Time))
					.Append(',')
					.Append(Inv(x))
					.Append(',')
					.Append(Inv(y))
					.Append(',')
					.Append(Inv(v))
					.Append('\n');
			}
			File.WriteAllText(Path.Combine(path, "measured_velocity.csv"), stringBuilder.ToString());
		}
		if (!hasMeasuredStageSamples)
		{
			return;
		}
		StringBuilder stringBuilder2 = new StringBuilder();
		stringBuilder2.Append("# time=-1 denotes steady-state\n");
		stringBuilder2.Append("x,y,time,stage\n");
		foreach (MeasuredStageSample measuredStageSample in measuredStageSamples)
		{
			stringBuilder2.Append(Inv(measuredStageSample.Location.X)).Append(',').Append(Inv(measuredStageSample.Location.Y))
				.Append(',')
				.Append(Inv(measuredStageSample.Time))
				.Append(',')
				.Append(Inv(measuredStageSample.Stage))
				.Append('\n');
		}
		File.WriteAllText(Path.Combine(path, "measured_stage.csv"), stringBuilder2.ToString());
		static string Inv(double computed)
		{
			return computed.ToString("R", CultureInfo.InvariantCulture);
		}
	}

	private static void ExportShapefiles(ISyntheticTestCaseParams synth, string directory)
	{
		IReadOnlyList<NamedResultPoint> resultPoints = synth.GetResultPoints();
		IReadOnlyList<NamedResultLine> resultLines = synth.GetResultLines();
		IReadOnlyList<MeasuredStageSample> measuredStageSamples = synth.GetMeasuredStageSamples();
		IReadOnlyList<MeasuredVelocitySample> measuredVelocitySamples = synth.GetMeasuredVelocitySamples();
		bool hasResultPoints = resultPoints != null && resultPoints.Count > 0;
		bool hasResultLines = resultLines != null && resultLines.Count > 0;
		bool hasMeasuredStageSamples = measuredStageSamples != null && measuredStageSamples.Count > 0;
		bool hasMeasuredVelocitySamples = measuredVelocitySamples != null && measuredVelocitySamples.Count > 0;
		if (!hasResultPoints && !hasResultLines && !hasMeasuredStageSamples && !hasMeasuredVelocitySamples)
		{
			return;
		}
		string outDir = Path.Combine(directory, "Data");
		Directory.CreateDirectory(outDir);
		Projection proj = synth.GetProjection();
		int ordinal = 0;
		if (hasResultPoints)
		{
			List<Point> pts = new List<Point>(resultPoints.Count);
			Table tbl = new Table();
			tbl.AddColumn("Name", typeof(string));
			foreach (NamedResultPoint resultPoint in resultPoints)
			{
				pts.Add(resultPoint.Location);
				tbl.AddNewRow(new object[1] { resultPoint.Name });
			}
			if (TryWriteShapefile(Path.Combine(outDir, "result_points.shp"), tbl, () => ShapefileIO.Write(Path.Combine(outDir, "result_points.shp"), pts, tbl, proj)))
			{
				ordinal++;
			}
		}
		if (hasResultLines)
		{
			List<Polyline> lines = new List<Polyline>(resultLines.Count);
			Table tbl2 = new Table();
			tbl2.AddColumn("Name", typeof(string));
			foreach (NamedResultLine resultLine in resultLines)
			{
				lines.Add(resultLine.Line);
				tbl2.AddNewRow(new object[1] { resultLine.Name });
			}
			if (TryWriteShapefile(Path.Combine(outDir, "result_lines.shp"), tbl2, () => ShapefileIO.Write(Path.Combine(outDir, "result_lines.shp"), lines, tbl2, proj)))
			{
				ordinal++;
			}
		}
		if (hasMeasuredStageSamples)
		{
			List<Point> pts2 = new List<Point>(measuredStageSamples.Count);
			Table tbl3 = new Table();
			tbl3.AddColumn("Time", typeof(double));
			tbl3.AddColumn("Stage", typeof(double));
			foreach (MeasuredStageSample measuredStageSample in measuredStageSamples)
			{
				pts2.Add(measuredStageSample.Location);
				tbl3.AddNewRow(new object[2] { measuredStageSample.Time, measuredStageSample.Stage });
			}
			if (TryWriteShapefile(Path.Combine(outDir, "measured_stage.shp"), tbl3, () => ShapefileIO.Write(Path.Combine(outDir, "measured_stage.shp"), pts2, tbl3, proj)))
			{
				ordinal++;
			}
		}
		if (hasMeasuredVelocitySamples)
		{
			List<Point> pts3 = new List<Point>(measuredVelocitySamples.Count);
			Table tbl4 = new Table();
			tbl4.AddColumn("Time", typeof(double));
			tbl4.AddColumn("VX", typeof(double));
			tbl4.AddColumn("VY", typeof(double));
			tbl4.AddColumn("Speed", typeof(double));
			foreach (MeasuredVelocitySample measuredVelocitySample in measuredVelocitySamples)
			{
				double x = measuredVelocitySample.Velocity.X;
				double y = measuredVelocitySample.Velocity.Y;
				pts3.Add(measuredVelocitySample.Location);
				tbl4.AddNewRow(new object[4]
				{
					measuredVelocitySample.Time,
					x,
					y,
					Math.Sqrt(x * x + y * y)
				});
			}
			if (TryWriteShapefile(Path.Combine(outDir, "measured_velocity.shp"), tbl4, () => ShapefileIO.Write(Path.Combine(outDir, "measured_velocity.shp"), pts3, tbl4, proj)))
			{
				ordinal++;
			}
		}
		if (ordinal > 0)
		{
			Console.WriteLine($"Exported {ordinal} sampling-geometry shapefile(s).");
		}
	}

	private static bool TryWriteShapefile(string shpFilename, Table attrTable, Func<OperationResult> write)
	{
		ShapefileIO.DeleteAllAssociations(shpFilename);
		OperationResult operationResult = write();
		if (!operationResult.Result)
		{
			Console.WriteLine("Failed to write " + Path.GetFileName(shpFilename) + ": " + operationResult.GetConcatenatedMessages());
		}
		return operationResult.Result;
	}
}
