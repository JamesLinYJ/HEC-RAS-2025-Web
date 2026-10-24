using System.Text.Json.Serialization;
using Geospatial.Rasters;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using OSGeo.GDAL;
using Ras.Hydraulics;
using Ras.Layers;
using Ras.Layers.Terrains;
using Ras.Layers.Meteorology;
using Ras.Runtime;
using Utility.Units;

namespace Ras.Backend.ServerChecks;

internal sealed record FixtureManifest(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("rootPath")] string RootPath,
    [property: JsonPropertyName("projectPath")] string ProjectPath,
    [property: JsonPropertyName("planPath")] string PlanPath,
    [property: JsonPropertyName("geometryPath")] string GeometryPath,
    [property: JsonPropertyName("boundaryPath")] string BoundaryPath,
    [property: JsonPropertyName("terrainPath")] string TerrainPath,
    [property: JsonPropertyName("terrainElevation")] double TerrainElevation,
    [property: JsonPropertyName("initialStage")] double InitialStage,
    [property: JsonPropertyName("expectedRainfallDepthMetres")] double ExpectedRainfallDepthMetres,
    [property: JsonPropertyName("computeDurationSeconds")] double ComputeDurationSeconds,
    [property: JsonPropertyName("computeTimestepSeconds")] double ComputeTimestepSeconds,
    [property: JsonPropertyName("cellCount")] int CellCount);

internal sealed record EditorFixtureManifest(
    string Schema, string ProjectPath, string BoundaryPath, string StationName,
    int InitialRowCount, double[] InitialValues);

internal static class PlanComputeFixture
{
    private static readonly DateTime Start = new(2026, 10, 16, 0, 0, 0, DateTimeKind.Local);
    private const float TerrainElevation = 42f;
    private const float InitialStage = 43f;
    // The SI source Plan stores precipitation rate in mm/hour. 3.6 mm/hour
    // over the four-second acceptance Plan contributes 0.000004 m.
    private const float PrecipitationRateMillimetersPerHour = 3.6f;

    public static FixtureManifest Create(string requestedDirectory) => Create(requestedDirectory,
        durationSeconds: 4, timestepSeconds: 0.1, outputIntervalSeconds: 1, cellSide: 1);

    public static FixtureManifest CreateMap(string requestedDirectory) => Create(requestedDirectory,
        durationSeconds: 4, timestepSeconds: 0.1, outputIntervalSeconds: 1, cellSide: 4);

    public static FixtureManifest CreateVelocityMap(string requestedDirectory) => Create(requestedDirectory,
        durationSeconds: 4, timestepSeconds: 0.1, outputIntervalSeconds: 1, cellSide: 4, wind: true);

    public static EditorFixtureManifest CreateEditor(string requestedDirectory)
    {
        FixtureManifest fixture = Create(requestedDirectory);
        const string stationName = "中文雨量站";
        double[] values = [0.5, 1.25, 0.75];
        Project project = new(fixture.ProjectPath);
        try
        {
            BoundaryCondition boundary = project.BoundaryConditions.Single();
            boundary.Precipitation.SpatialDataType = SpatialDataType.Point;
            var station = new MeteorologicStation(stationName, new Point(0, 0));
            boundary.MetStations.Add(station);
            boundary.Precipitation.Gauges.Add(MetGaugedData.FromTableData(station,
                TimeSeriesDefinition.Irregular([Start, Start.AddMinutes(20), Start.AddMinutes(50)]), values));
            boundary.Save();
        }
        finally { Dispose(project); }
        return new("hecras-editor-e2e/v1", fixture.ProjectPath, fixture.BoundaryPath,
            stationName, values.Length, values);
    }

    public static FixtureManifest CreateCancellation(string requestedDirectory) => Create(requestedDirectory,
        // A modest 16 x 16 mesh keeps the fixture small while the 360,000-step
        // CPU run leaves a real cancellation window after source telemetry arrives.
        durationSeconds: 36_000, timestepSeconds: 0.1, outputIntervalSeconds: 3_600, cellSide: 16);

    private static FixtureManifest Create(string requestedDirectory, double durationSeconds,
        double timestepSeconds, double outputIntervalSeconds, int cellSide, bool wind = false)
    {
        string root = Path.GetFullPath(requestedDirectory);
        if (File.Exists(root) || Directory.Exists(root))
            throw new IOException("Fixture output must be a new directory; existing files are never overwritten.");

        Directory.CreateDirectory(root);
        try
        {
            RasRuntimeBootstrap.Configure();
            string projectPath = Path.Combine(root, "Plan Compute Acceptance.ras");
            string terrainPath;
            string planPath;
            string geometryPath;
            string boundaryPath;

            Project project = Project.CreateNew(projectPath, UnitSystem.SI);
            try
            {
                Terrain terrain = project.AddNewTerrain("Solver Terrain");
                terrainPath = Path.Combine(terrain.SourceDirectory, "flat.tif");
                Directory.CreateDirectory(terrain.SourceDirectory);
                int rasterSize = Math.Max(32, cellSide + 8);
                using (Driver driver = Gdal.GetDriverByName("GTiff"))
                using (Dataset raster = driver.Create(terrainPath, rasterSize, rasterSize, 1, DataType.GDT_Float32,
                           ["TILED=YES", "BLOCKXSIZE=16", "BLOCKYSIZE=16"]))
                {
                    raster.SetGeoTransform([-4, 1, 0, rasterSize - 4, 0, -1]);
                    using Band band = raster.GetRasterBand(1);
                    band.Fill(TerrainElevation, 0);
                }
                terrain.Add(RasterTerrainInput.CreateAndScan(terrain, terrainPath));

                NValueLayer nValues = project.AddNewNValueLayer("Solver Roughness");
                nValues.SetConstant(0.035f);
                Geometry geometry = project.AddNewGeometry("Solver Geometry", terrain, nValues);
                geometry.FlowAreaLayer.Add(new FlowArea(CreateMesh(cellSide), "Solver Mesh"));

                BoundaryCondition boundary = project.AddNewBoundaryCondition("Solver Boundary", Start,
                    TimeSpan.FromSeconds(Math.Max(TimeSpan.FromHours(2).TotalSeconds, durationSeconds)));
                boundary.InitialConditions.Stage.IsEnabled = true;
                boundary.InitialConditions.Stage.SpatialDataType = SpatialDataType.Constant;
                boundary.InitialConditions.Stage.ConstantValue = InitialStage;
                boundary.Precipitation.IsEnabled = true;
                boundary.Precipitation.SpatialDataType = SpatialDataType.Constant;
                boundary.Precipitation.ConstantValue = PrecipitationRateMillimetersPerHour;
                if (wind)
                {
                    boundary.Wind.IsEnabled = true;
                    boundary.Wind.SpatialDataType = SpatialDataType.Constant;
                    boundary.Wind.ConstantVx = 15f;
                    boundary.Wind.ConstantVy = 5f;
                }

                Plan plan = project.AddNewPlan("Plan CPU", geometry, boundary);
                Configure(plan, durationSeconds, timestepSeconds, outputIntervalSeconds);

                foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>()
                             .Concat(project.Geometries).Concat(project.BoundaryConditions)
                             .Concat(project.Terrains).Concat(project.NValues))
                    layer.Save();
                project.Save();

                planPath = Path.GetFullPath(plan.Filename);
                geometryPath = Path.GetFullPath(geometry.Filename);
                boundaryPath = Path.GetFullPath(boundary.Filename);
            }
            finally
            {
                Dispose(project);
            }

            Project reopened = new(projectPath);
            try
            {
                BoundaryCondition boundary = reopened.BoundaryConditions.Single();
                if (wind && (!boundary.Wind.IsEnabled ||
                    boundary.Wind.SpatialDataType != SpatialDataType.Constant ||
                    boundary.Wind.ConstantVx != 15f || boundary.Wind.ConstantVy != 5f))
                    throw new InvalidDataException("The reopened fixture did not retain its real wind forcing.");
                if (!boundary.InitialConditions.Stage.IsEnabled ||
                    boundary.InitialConditions.Stage.SpatialDataType != SpatialDataType.Constant ||
                    boundary.InitialConditions.Stage.ConstantValue != InitialStage)
                    throw new InvalidDataException("The reopened fixture did not retain its wet initial stage.");

                Plan reopenedPlan = reopened.Plans.Single();
                if (reopenedPlan.SolverType != SolverType.CPU ||
                    Math.Abs(reopenedPlan.InternalComputeDurationSeconds - durationSeconds) > 1e-7 ||
                    Math.Abs(reopenedPlan.ComputeTimestep.TotalSeconds - timestepSeconds) > 1e-7 ||
                    Math.Abs(reopenedPlan.BaseOutputInterval.TotalSeconds - outputIntervalSeconds) > 1e-7 ||
                    reopenedPlan.Geometry?.Mesh?.CellCount != cellSide * cellSide)
                    throw new InvalidDataException("The reopened fixture did not retain the requested CPU Plan duration, timestep, output interval, or mesh cell count.");
            }
            finally
            {
                Dispose(reopened);
            }

            double expectedRainfallDepthMetres = PrecipitationRateMillimetersPerHour * durationSeconds / 3_600_000d;
            return new FixtureManifest("hecras-plan-compute-e2e/v1", root, projectPath,
                planPath, geometryPath, boundaryPath, terrainPath, TerrainElevation, InitialStage,
                expectedRainfallDepthMetres,
                ComputeDurationSeconds: durationSeconds, ComputeTimestepSeconds: timestepSeconds,
                CellCount: cellSide * cellSide);
        }
        catch
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            throw;
        }
    }

    private static void Configure(Plan plan, double durationSeconds, double timestepSeconds,
        double outputIntervalSeconds)
    {
        plan.TimeWindowMode = PlanTimeWindowMode.Plan;
        plan.InternalStartTime = Start;
        plan.InternalComputeDurationSeconds = durationSeconds;
        plan.ComputeTimestep = TimeSpan.FromSeconds(timestepSeconds);
        plan.BaseOutputInterval = TimeSpan.FromSeconds(outputIntervalSeconds);
        plan.SolverType = SolverType.CPU;
    }

    private static Mesh CreateMesh(int side)
    {
        if (side < 1) throw new ArgumentOutOfRangeException(nameof(side));

        Point[] points = Enumerable.Range(0, (side + 1) * (side + 1))
            .Select(index => new Point(index % (side + 1), index / (side + 1))).ToArray();
        int PointIndex(int x, int y) => y * (side + 1) + x;
        var faces = new List<Face>();
        var cellFaces = new int[side * side][];
        var nodeFaces = Enumerable.Range(0, points.Length).Select(_ => new List<int>()).ToArray();
        var faceByUndirectedEdge = new Dictionary<(int Low, int High), int>();

        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
            {
                int cellIndex = y * side + x;
                int[] vertices =
                [
                    PointIndex(x, y), PointIndex(x + 1, y),
                    PointIndex(x + 1, y + 1), PointIndex(x, y + 1),
                ];
                var incidentFaces = new int[4];
                for (int edge = 0; edge < vertices.Length; edge++)
                {
                    int start = vertices[edge];
                    int end = vertices[(edge + 1) % vertices.Length];
                    var key = start < end ? (start, end) : (end, start);
                    if (!faceByUndirectedEdge.TryGetValue(key, out int faceIndex))
                    {
                        faceIndex = faces.Count;
                        faceByUndirectedEdge.Add(key, faceIndex);
                        faces.Add(new Face { fpA = start, fpB = end, cellA = cellIndex, cellB = -1 });
                    }
                    else
                    {
                        Face face = faces[faceIndex];
                        if (face.cellB != -1 || face.fpA != end || face.fpB != start)
                            throw new InvalidDataException("The cancellation mesh contains a non-manifold or inconsistently oriented edge.");
                        face.cellB = cellIndex;
                        faces[faceIndex] = face;
                    }

                    incidentFaces[edge] = faceIndex;
                    nodeFaces[start].Add(faceIndex);
                    nodeFaces[end].Add(faceIndex);
                }
                cellFaces[cellIndex] = incidentFaces;
            }
        }

        FacePoint[] nodes = points.Select((point, index) => new FacePoint(point)
        {
            Faces = nodeFaces[index].Distinct().ToArray(),
        }).ToArray();
        Point[] perimeter = Enumerable.Range(0, side + 1).Select(x => points[PointIndex(x, 0)])
            .Concat(Enumerable.Range(1, side).Select(y => points[PointIndex(side, y)]))
            .Concat(Enumerable.Range(0, side).Reverse().Select(x => points[PointIndex(x, side)]))
            .Concat(Enumerable.Range(1, side - 1).Reverse().Select(y => points[PointIndex(0, y)]))
            .ToArray();
        Cell[] cells = cellFaces.Select(cellFaceSet => new Cell { Faces = cellFaceSet }).ToArray();
        Point[] centers = Enumerable.Range(0, side * side)
            .Select(index => new Point(index % side + 0.5, index / side + 0.5)).ToArray();
        for (int cellIndex = 0; cellIndex < cells.Length; cellIndex++)
        {
            foreach (int faceIndex in cells[cellIndex].Faces.ToArray())
            {
                Face face = faces[faceIndex];
                if (face.cellA != cellIndex && face.cellB != cellIndex)
                    throw new InvalidDataException($"Cancellation mesh cell {cellIndex} references face {faceIndex}, but the face does not reference that cell.");
            }
        }
        return new Mesh(new Polygon(perimeter), cells, faces.ToArray(), nodes, centers, null!);
    }

    private static void Dispose(Project project)
    {
        foreach (Result result in project.Results) result.Dispose();
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>()
                     .Concat(project.Geometries).Concat(project.BoundaryConditions)
                     .Concat(project.Terrains).Concat(project.NValues).Concat(project.ClassificationLayers))
            layer.Dispose();
    }
}
