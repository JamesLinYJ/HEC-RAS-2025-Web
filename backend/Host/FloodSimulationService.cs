using System.Buffers.Binary;
using System.Text.Json;
using Geospatial;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine;
using Ras.Engine.Structures;
using Ras.Engine.Structures.Weirs;
using Utility.Progress;
using Utility.Units;

namespace Ras.Backend;

public sealed record FloodScenario(string Name, double Rainfall, double StormDuration, double BreachStart,
    double BreachDuration, double BreachWidth, double ReservoirLevel, string RainPattern);
public sealed record FloodGeometry(double[][] Vertices, int[][] Cells, double[][] Centers, double[] Areas,
    double[] BedElevation, int[][] DamEdges, double DamY, double DamCrest, double ReservoirStage);
public sealed record FloodFrame(double TimeSeconds, string Fields, double RainRate, double RainTotal,
    double BreachFraction, double Discharge, double StoredVolume, double WetArea, double MaxDepth,
    double MaxSpeed, double[] DamCrests, double OutflowVolume, double MassBalanceError);
public sealed record FloodResult(string State, string RunId, string ModelVersion, FloodScenario Scenario,
    FloodGeometry Geometry, string FieldEncoding, FloodFrame[] Frames, long Steps, double DurationSeconds);

/// <summary>Project-free application adapter over the shared CPU SWE solver. SI units throughout.</summary>
public static class FloodSimulationService
{
    public const string ModelVersion = "valley-swe-1";
    private const double DamY = -140, Crest = 34;
    // Resamplers use half-open extents. Include an apron so boundary face samples are valid terrain.
    private static readonly Extent Domain = new(-440, 440, -620, 620);

    public static object Describe(FloodScenario scenario)
    {
        Validate(scenario);
        Mesh mesh = CreateMesh(scenario.BreachWidth);
        return new { modelVersion = ModelVersion, geometry = Geometry(mesh, scenario) };
    }

    internal static async Task<FloodResult> RunAsync(FloodScenario scenario,
        Action<BackendOperationProgress>? report, BackendDuplexControlReader? controls)
    {
        Validate(scenario);
        using var cancellation = new CancellationTokenSource();
        using var stop = new CancellationTokenSource();
        Task pump = controls is null ? Task.CompletedTask : Task.Run(async () =>
        {
            try
            {
                await foreach (BackendDuplexControlFrame frame in controls.ReadAllAsync(stop.Token).ConfigureAwait(false))
                    if (frame.Control.TryGetProperty("type", out JsonElement type) && type.GetString() == "cancel")
                        cancellation.Cancel();
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        });
        try { return await Task.Run(() => Solve(scenario, report, cancellation.Token)).ConfigureAwait(false); }
        finally { stop.Cancel(); await pump.ConfigureAwait(false); }
    }

    public static FloodResult Solve(FloodScenario scenario, Action<BackendOperationProgress>? report = null,
        CancellationToken cancellation = default)
    {
        Validate(scenario);
        cancellation.ThrowIfCancellationRequested();
        var progress = new FloodProgress(report);
        var reporter = new ProgressReporter(progress);
        using var registration = cancellation.Register(reporter.RequestCancel);
        report?.Invoke(new BackendOperationProgress(0, "正在准备河谷"));
        Mesh mesh = CreateMesh(scenario.BreachWidth);
        var sanity = mesh.SanityCheck();
        if (!sanity.Result) throw new InvalidOperationException(sanity.GetConcatenatedMessages());
        var terrain = new FunctionalResampler<float>(Domain, null, p => (float)Elevation(p.X, p.Y));
        var roughness = new ConstantValueResampler<float>(.035f, Domain);
        var prepared = MeshPropertyTables.ComputeFrom(mesh, terrain, roughness,
            PropertyTableOptions.Default(UnitSystem.SI), out MeshPropertyTables tables, reporter);
        cancellation.ThrowIfCancellationRequested();
        if (!prepared.Result) throw new InvalidOperationException(prepared.GetConcatenatedMessages());

        FloodGeometry geometry = Geometry(mesh, scenario);
        double duration = Math.Max(scenario.StormDuration, scenario.BreachStart + scenario.BreachDuration + 60) * 60;
        var control = new SolverControl(UnitSystem.SI, SolverControl.EquationSet.SWE, SolverControl.TimeStepping.Implicit)
        {
            numCores = 1, LtsLevels = SolverControl.LtsLevelOptions.GlobalStepping
        };
        control.computeControl.Timestep = Math.Ceiling(duration / 120 / 60) * 60;
        control.computeControl.EndTime = duration;
        control.computeControl.variableDt = true;
        MeshWaterState initial = MeshWaterState.Factory<SWEWaterStateConfig>(mesh, tables);
        initial.SetInitialStageFromCellCenters(new FunctionalResampler<float>(Domain, null,
            p => p.Y < DamY ? (float)geometry.ReservoirStage : (float)tables.CellMinElevation(0) - 1000));
        var driver = new ComputeDriver(mesh, tables, initial, control.computeControl, control) { Reporter = reporter };
        var breach = new ProgressiveBreach(scenario) { UnitSys = UnitSystem.SI };
        breach.Faces = DamFaces(mesh);
        breach.UpCells = breach.Faces.Select(f => mesh.CellCenters[mesh.Faces[f].cellA].Y < DamY ? mesh.Faces[f].cellA : mesh.Faces[f].cellB).ToArray();
        breach.DnCells = breach.Faces.Select((f, i) => mesh.Faces[f].cellA == breach.UpCells[i] ? mesh.Faces[f].cellB : mesh.Faces[f].cellA).ToArray();
        breach.FaceSign = breach.Faces.Select((f, i) => (sbyte)(mesh.Faces[f].cellA == breach.UpCells[i] ? 1 : -1)).ToArray();
        driver.HydraulicStructures.AddHydraulicStructure(breach);
        int[] outlet = Enumerable.Range(0, mesh.Faces.Length).Where(f =>
            Math.Abs(mesh.FacePoints[mesh.Faces[f].fpA].Point.Y - 600) < .001 &&
            Math.Abs(mesh.FacePoints[mesh.Faces[f].fpB].Point.Y - 600) < .001).ToArray();
        var boundary = BoundaryCondition.ConstantFaceValue(outlet, .035 / 6);
        boundary.EGSlope = .035 / 6;
        boundary.Name = "Valley outlet";
        driver.BoundaryConditions.AddBoundaryCondition(boundary, BoundaryCondition.BCType.NormalDepth);
        double[] times = Enumerable.Range(0, (int)Math.Ceiling(duration / 60) + 1).Select(i => i * 60d).ToArray();
        driver.BoundaryConditions.SetPrecipitation(times,
            i => Enumerable.Repeat((float)((RainTotal(scenario, times[i] + 60) - RainTotal(scenario, times[i])) / 1000 / 60), mesh.CellCount).ToArray(),
            1, JaggedWeights<float>.Identity(mesh.CellCount));
        var frames = new List<FloodFrame>();
        double outflow = 0, previousTime = 0, initialVolume = initial.CellVolume.Sum(), domainArea = geometry.Areas.Sum();
        driver.ComputeStepDone = step =>
        {
            outflow += outlet.Sum(f => step.WaterState.FaceFlow[f]) * (step.Time - previousTime);
            previousTime = step.Time;
        };
        driver.TimestepDone = step =>
        {
            cancellation.ThrowIfCancellationRequested();
            frames.Add(Capture(step, mesh, tables, geometry, breach, scenario, outflow, initialVolume, domainArea));
            report?.Invoke(new BackendOperationProgress(step.Time / duration, "正在推演"));
        };
        try
        {
            // Prepare/SolveSteps explicitly so canceled or failed preparation also releases solver resources.
            driver.Prepare(reporter);
            cancellation.ThrowIfCancellationRequested();
            progress.Solving = true;
            var result = driver.SolveSteps(duration);
            cancellation.ThrowIfCancellationRequested();
            if (!result.Result || Math.Abs(driver.Time - duration) > .001)
            {
                int c = Math.Max(0, driver.globalLimitingCell);
                MeshWaterState failed = driver.ConstructGlobalState();
                throw new InvalidOperationException("洪水推演未完成：" + result.GetConcatenatedMessages() + $" Time={driver.Time}; Steps={driver.StepCount}; Limitation={driver.globalStepLimitation}; MaxDepth={frames.LastOrDefault()?.MaxDepth}; MaxSpeed={frames.LastOrDefault()?.MaxSpeed}; Cell={c}; Old={initial.CellElevation[c]}/{initial.CellVolume[c]}; New={failed.CellElevation[c]}/{failed.CellVolume[c]}");
            }
            return new FloodResult("completed", Guid.NewGuid().ToString("N"), ModelVersion, scenario,
                geometry, "float32-le/base64:wse,depth,vx,vy", frames.ToArray(), driver.StepCount, duration);
        }
        finally { driver.Cleanup(); }
    }

    private static FloodFrame Capture(MeshTimestep step, Mesh mesh, MeshPropertyTables tables,
        FloodGeometry geometry, ProgressiveBreach breach, FloodScenario scenario, double outflow, double initialVolume, double domainArea)
    {
        MeshWaterState state = step.WaterState;
        int count = mesh.CellCount;
        var bytes = new byte[count * 4 * sizeof(float)];
        double maxDepth = 0, maxSpeed = 0, area = 0, volume = 0;
        for (int i = 0; i < count; i++)
        {
            double depth = Math.Max(0, state.CellElevation[i] - tables.CellMinElevation(i));
            bool wet = state.CellVolume[i] / geometry.Areas[i] > .01;
            double vx = wet ? state.CellVelocity[i].X : 0, vy = wet ? state.CellVelocity[i].Y : 0;
            double[] fields = [state.CellElevation[i], depth, vx, vy];
            for (int f = 0; f < fields.Length; f++)
            {
                if (!double.IsFinite(fields[f]) || Math.Abs(fields[f]) > 1e6)
                    throw new InvalidOperationException("推演结果超出有效范围，请调整参数后重试。");
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan((f * count + i) * 4, 4), (float)fields[f]);
            }
            if (!double.IsFinite(state.CellVolume[i]) || state.CellVolume[i] < -1e-5)
                throw new InvalidOperationException("推演水量无效。");
            volume += Math.Max(0, state.CellVolume[i]);
            maxDepth = Math.Max(maxDepth, depth);
            maxSpeed = Math.Max(maxSpeed, Math.Sqrt(vx * vx + vy * vy));
            if (state.CellVolume[i] / geometry.Areas[i] > .01) area += geometry.Areas[i];
        }
        return new FloodFrame(step.Time, Convert.ToBase64String(bytes), RainRate(scenario, step.Time),
            RainTotal(scenario, step.Time), breach.Fraction(step.Time), breach.GetWeirTotalFaceFlow(state),
            volume, area, maxDepth, maxSpeed, breach.CrestsAt(step.Time, mesh, tables), outflow,
            volume - initialVolume - RainTotal(scenario, step.Time) / 1000 * domainArea + outflow);
    }

    // A fixed-width rectangular breach lowers linearly over the specified formation time.
    // Native weir fluxes, wetting/drying and SWE transport remain in Ras.Engine.
    private sealed class ProgressiveBreach(FloodScenario scenario) : Weir("Valley dam", new Polyline(new Point[] { new(-420, DamY), new(420, DamY) }))
    {
        public double Fraction(double seconds) => Math.Clamp((seconds / 60 - scenario.BreachStart) / scenario.BreachDuration, 0, 1);
        public double[] CrestsAt(double time, Mesh mesh, MeshPropertyTables tables) => Faces.Select(face =>
        {
            Face f = mesh.Faces[face];
            double x = (mesh.FacePoints[f.fpA].Point.X + mesh.FacePoints[f.fpB].Point.X) / 2;
            tables.GetFaceTables(face, out var _, out var elevations, out var _, out var _);
            double bed = elevations.Span[0];
            return Math.Abs(x) < scenario.BreachWidth / 2 + 1e-6 ? Math.Max(bed, Crest - (Crest - bed) * Fraction(time)) : Math.Max(bed, Crest);
        }).ToArray();
        public override void Prepare(SolverControl control, Mesh mesh, MeshPropertyTables tables)
        {
            PhysProps.WeirCoef = 1.7;
            base.Prepare(control, mesh, tables);
            WeirCrest = CrestsAt(0, mesh, tables);
        }
        public override void Apply(double time, double dt, HydraulicStructureCollection collection,
            MeshWaterState water, Mesh mesh, MeshPropertyTables tables, HydraulicMeshComputer computer)
        {
            WeirCrest = CrestsAt(time + dt / 2, mesh, tables);
            base.Apply(time, dt, collection, water, mesh, tables, computer);
        }
    }

    private static FloodGeometry Geometry(Mesh mesh, FloodScenario scenario) => new(
        mesh.FacePoints.Select(p => new[] { p.Point.X, p.Point.Y, Elevation(p.Point.X, p.Point.Y) }).ToArray(),
        Enumerable.Range(0, mesh.CellCount).Select(c => mesh.CellFacePoints(c).ToArray()).ToArray(),
        mesh.CellCenters.Select(p => new[] { p.X, p.Y }).ToArray(),
        Enumerable.Range(0, mesh.CellCount).Select(mesh.CellPolygonArea).ToArray(),
        mesh.CellCenters.Select(p => Elevation(p.X, p.Y)).ToArray(),
        DamFaces(mesh).Select(f => new[] { mesh.Faces[f].fpA, mesh.Faces[f].fpB }).ToArray(),
        DamY, Crest, Elevation(0, DamY) + scenario.ReservoirLevel);

    private static int[] DamFaces(Mesh mesh) => Enumerable.Range(0, mesh.Faces.Length).Where(f =>
        Math.Abs(mesh.FacePoints[mesh.Faces[f].fpA].Point.Y - DamY) < .001 &&
        Math.Abs(mesh.FacePoints[mesh.Faces[f].fpB].Point.Y - DamY) < .001).OrderBy(f =>
            (mesh.FacePoints[mesh.Faces[f].fpA].Point.X + mesh.FacePoints[mesh.Faces[f].fpB].Point.X) / 2).ToArray();

    private static Mesh CreateMesh(double width)
    {
        // Insert the exact breach edges; no rounding of the user's width to a grid cell.
        double[] xs = Enumerable.Range(0, 43).Select(i => -420d + i * 20)
            .Where(x => Math.Abs(x - width / 2) >= 5 && Math.Abs(x + width / 2) >= 5)
            .Concat([-width / 2, width / 2]).Distinct().Order().ToArray();
        int nx = xs.Length - 1, ny = 60;
        Point[] points = Enumerable.Range(0, (nx + 1) * (ny + 1))
            .Select(i => new Point(xs[i % (nx + 1)], -600 + i / (nx + 1) * 20)).ToArray();
        var faces = new List<Face>();
        var cells = new List<Cell>();
        var centers = new List<Point>();
        var lookup = new Dictionary<(int, int), int>();
        for (int y = 0; y < ny; y++) for (int x = 0; x < nx; x++)
        {
            int a = y * (nx + 1) + x;
            int[] corners = [a, a + 1, a + nx + 2, a + nx + 1];
            var indices = new int[4];
            for (int k = 0; k < 4; k++)
            {
                int p = corners[k], q = corners[(k + 1) % 4];
                var key = (Math.Min(p, q), Math.Max(p, q));
                if (lookup.TryGetValue(key, out int index)) { var f = faces[index]; f.cellB = cells.Count; faces[index] = f; }
                else { index = faces.Count; lookup.Add(key, index); faces.Add(new Face { fpA = p, fpB = q, cellA = cells.Count, cellB = -1 }); }
                indices[k] = index;
            }
            cells.Add(new Cell { Faces = indices });
            centers.Add(new Point((xs[x] + xs[x + 1]) / 2, -590 + y * 20));
        }
        var adjacency = points.Select(_ => new List<int>()).ToArray();
        for (int i = 0; i < faces.Count; i++) { adjacency[faces[i].fpA].Add(i); adjacency[faces[i].fpB].Add(i); }
        FacePoint[] nodes = points.Select((p, i) => new FacePoint(p) { Faces = adjacency[i].OrderBy(f =>
        {
            Point q = points[faces[f].fpA == i ? faces[f].fpB : faces[f].fpA];
            return Math.Atan2(q.Y - p.Y, q.X - p.X);
        }).ToArray() }).ToArray();
        return new Mesh(new Polygon(new Point[] { new(-420, -600), new(420, -600), new(420, 600), new(-420, 600) }),
            cells.ToArray(), faces.ToArray(), nodes, centers.ToArray(), new Memory<Point>[faces.Count]);
    }

    public static double Elevation(double x, double y)
    {
        x /= 6; double z = y / 6;
        double river = Math.Sin((z + 24) * .034) * 11 + Math.Sin((z + 24) * .074) * 3;
        double distance = Math.Abs(x - river);
        double width = z < -25 ? 11 + Math.Sin(Math.Clamp((-z - 25) / 70, 0, 1) * Math.PI) * 10 : 5.5 + Math.Clamp((z + 25) / 80, 0, 1) * 4;
        double flank = Math.Max(0, distance - width), t = Math.Clamp(flank / 24, 0, 1);
        double ridge = Math.Sin(z * .07 + x * .035) * 3.5 + Math.Sin(x * .15 - z * .09) * 1.7 + Math.Cos(z * .22 + x * .13) * .7;
        return 3.8 - (z + 24) * .035 + flank * .32 + flank * flank * .0025 + ridge * t * t * (3 - 2 * t) - Math.Exp(-distance * distance / 28) * 1.5;
    }

    public static double RainRate(FloodScenario s, double seconds)
    {
        double p = seconds / (s.StormDuration * 60), peak = s.RainPattern == "front" ? .25 : s.RainPattern == "back" ? .75 : .5;
        if (p < 0 || p >= 1) return 0;
        double position = p < peak ? p / peak : (1 - p) / (1 - peak);
        return s.Rainfall * (.22 + .78 * Math.Sin(position * Math.PI / 2));
    }
    public static double RainTotal(FloodScenario s, double seconds)
    {
        double p = Math.Clamp(seconds / (s.StormDuration * 60), 0, 1), peak = s.RainPattern == "front" ? .25 : s.RainPattern == "back" ? .75 : .5;
        double integral = p <= peak ? peak * 2 / Math.PI * (1 - Math.Cos(p / peak * Math.PI / 2)) :
            peak * 2 / Math.PI + (1 - peak) * 2 / Math.PI * Math.Cos((1 - p) / (1 - peak) * Math.PI / 2);
        return s.Rainfall * s.StormDuration / 60 * (.22 * p + .78 * integral);
    }
    private static void Validate(FloodScenario s)
    {
        static void Range(double value, double min, double max)
        { if (!double.IsFinite(value) || value < min || value > max) throw new ArgumentException("降雨或溃坝参数超出范围。"); }
        Range(s.Rainfall, 0, 120); Range(s.StormDuration, 60, 240); Range(s.BreachStart, 5, 120);
        Range(s.BreachDuration, 1, 60); Range(s.BreachWidth, 15, 100); Range(s.ReservoirLevel, 8, 26);
        if (s.RainPattern is not ("front" or "middle" or "back") || string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 100)
            throw new ArgumentException("场景名称或雨型无效。");
    }
    private sealed class FloodProgress(Action<BackendOperationProgress>? report) : IProgressTarget
    {
        public bool Solving { get; set; }
        private int last = -1;
        public void WriteMessage(ProgressReporter sender, Utility.Logging.Message message) { }
        public void WriteProgress(ProgressReporter sender, int localProgress, int globalProgress)
        { if (globalProgress != last) { last = globalProgress; report?.Invoke(new BackendOperationProgress((Solving ? .05 + .94 * Math.Clamp(globalProgress, 0, 100) / 100d : .05 * Math.Clamp(globalProgress, 0, 100) / 100d), Solving ? "正在推演" : "正在准备河谷")); } }
    }
}
