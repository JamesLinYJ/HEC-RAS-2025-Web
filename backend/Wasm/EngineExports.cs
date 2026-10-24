using System;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Threading.Tasks;
using Geospatial;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine;
using Utility.Logging;
using Utility.Progress;
using Utility.Units;

namespace Ras.Wasm;

public static partial class EngineExports
{
#if RAS_BROWSER_WASM
    [JSExport]
#endif
    public static string GetRuntimeInfo() => JsonSerializer.Serialize(new RuntimeInfoResponse
    {
        Runtime = "dotnet-browser-wasm",
        Solver = "Ras.Engine CPU solver",
        SolverTypes = ["SolverExpSWE", "SolverImpSWE"],
        ComputePath = "in-memory HEC-RAS mesh -> hydraulic property tables -> MeshWaterState -> ComputeDriver -> existing CPU solver",
        Fixture = "one wet quadrilateral cell, flat terrain and uniform roughness/rainfall",
        ProjectFileCompute = false,
        Hdf5ProjectInput = false,
        Cancellation = true
    }, EngineJsonContext.Default.RuntimeInfoResponse);

#if RAS_BROWSER_WASM
    [JSExport]
#endif
    public static Task<string> RunCompute(string requestJson)
    {
        ComputeRequest request = new();
        try
        {
            request = ParseRequest(requestJson);
            Validate(request);
            return Task.FromResult(RunSolver(request));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(SerializeResponse(new ComputeResponse
            {
                Status = "cancelled",
                SolverType = SolverTypeName(request.TimeStepping)
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(SerializeResponse(new ComputeResponse
            {
                Status = "failed",
                Error = ex.Message,
                SolverType = "unknown"
            }));
        }
    }

    private static string RunSolver(ComputeRequest request)
    {
        ProgressReporter reporter = new(new RuntimeProgressTarget());
        PollCancellation(reporter);

        Mesh mesh = CreateFixtureMesh();
        var terrain = new ConstantValueResampler<float>((float)request.TerrainElevation, Extent.EffectivelyInfinite);
        var roughness = new ConstantValueResampler<float>((float)request.Roughness, Extent.EffectivelyInfinite);
        PropertyTableOptions tableOptions = PropertyTableOptions.Default(UnitSystem.SI);

        OperationResult tableResult = MeshPropertyTables.ComputeFrom(mesh, terrain, roughness, tableOptions, out MeshPropertyTables tables, reporter);
        if (!tableResult.Result)
        {
            return Failure(tableResult.GetConcatenatedMessages(), request);
        }
        PollCancellation(reporter);

        SolverControl solverControl = new(UnitSystem.SI, SolverControl.EquationSet.SWE,
            request.TimeStepping == "Implicit" ? SolverControl.TimeStepping.Implicit : SolverControl.TimeStepping.Explicit)
        {
            numCores = 1
        };
        ComputeControl computeControl = solverControl.computeControl;
        computeControl.Timestep = request.TimestepSeconds;
        computeControl.EndTime = request.DurationSeconds;
        computeControl.variableDt = false;

        MeshWaterState initialState = MeshWaterState.Factory<SWEWaterStateConfig>(mesh, tables);
        initialState.SetConstantElevation(request.InitialStage);
        var driver = new ComputeDriver(mesh, tables, initialState, computeControl, solverControl)
        {
            Reporter = reporter
        };
        driver.BoundaryConditions.SetPrecipitation(
            [0, request.DurationSeconds],
            _ => [(float)request.PrecipitationRate],
            1f,
            JaggedWeights<float>.Identity(mesh.CellCount));

        OperationResult computeResult = driver.Solve(request.DurationSeconds);
        if (reporter.CancelRequested)
        {
            return SerializeResponse(new ComputeResponse
            {
                Status = "cancelled",
                SolverType = SolverTypeName(request.TimeStepping),
                Steps = driver.StepCount,
                FinalTimeSeconds = driver.Time
            });
        }
        if (!computeResult.Result)
        {
            return Failure(computeResult.GetConcatenatedMessages(), request, driver.StepCount, driver.Time);
        }

        MeshWaterState finalState = driver.ConstructGlobalState();
        double[] depths = new double[mesh.CellCount];
        for (int i = 0; i < depths.Length; i++)
        {
            depths[i] = Math.Max(0, finalState.CellElevation[i] - tables.CellMinElevation(i));
        }

        RuntimeHost.ReportProgress(JsonSerializer.Serialize(new ProgressMessage
        {
            Progress = 100,
            Message = "CPU solve completed"
        }, EngineJsonContext.Default.ProgressMessage));
        return SerializeResponse(new ComputeResponse
        {
            Status = "completed",
            SolverType = SolverTypeName(request.TimeStepping),
            Steps = driver.StepCount,
            FinalTimeSeconds = driver.Time,
            WetCellCount = finalState.GetWetCellCount(),
            CellWaterSurface = finalState.CellElevation,
            CellDepth = depths,
            FaceVelocity = finalState.FaceVelocity
        });
    }

    private static Mesh CreateFixtureMesh()
    {
        Point[] points = [new(4, 4), new(6, 4), new(6, 6), new(4, 6)];
        Face[] faces = Enumerable.Range(0, points.Length)
            .Select(index => new Face
            {
                fpA = index,
                fpB = (index + 1) % points.Length,
                cellA = 0,
                cellB = -1
            }).ToArray();
        FacePoint[] nodes = points.Select((point, index) => new FacePoint(point)
        {
            Faces = [(index + points.Length - 1) % points.Length, index]
        }).ToArray();
        return new Mesh(new Polygon(points),
            [new Cell { Faces = new int[] { 0, 1, 2, 3 } }],
            faces,
            nodes,
            [new Point(5, 5)],
            null);
    }

    private static void Validate(ComputeRequest request)
    {
        if (request.EquationSet != "SWE")
        {
            throw new ArgumentException("Only the existing SWE CPU solver is available in this browser proof.");
        }
        if (request.TimeStepping is not ("Explicit" or "Implicit"))
        {
            throw new ArgumentException("timeStepping must be Explicit or Implicit.");
        }
        RequireRange(request.TerrainElevation, -10000, 10000, nameof(request.TerrainElevation));
        RequireRange(request.InitialStage, -10000, 10000, nameof(request.InitialStage));
        if (request.InitialStage <= request.TerrainElevation)
        {
            throw new ArgumentOutOfRangeException(nameof(request.InitialStage), "Initial stage must be above the flat terrain so the fixture contains water.");
        }
        RequireRange(request.Roughness, 0.005, 0.2, nameof(request.Roughness));
        RequireRange(request.PrecipitationRate, 0, 0.01, nameof(request.PrecipitationRate));
        RequireRange(request.TimestepSeconds, 0.01, 1, nameof(request.TimestepSeconds));
        RequireRange(request.DurationSeconds, request.TimestepSeconds, 60, nameof(request.DurationSeconds));
        if (request.DurationSeconds / request.TimestepSeconds > 6000)
        {
            throw new ArgumentOutOfRangeException(nameof(request.DurationSeconds), "The browser fixture is limited to 6,000 compute steps per request.");
        }
    }

    private static ComputeRequest ParseRequest(string requestJson)
    {
        ComputeRequest defaults = new();
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            return defaults;
        }

        using JsonDocument document = JsonDocument.Parse(requestJson);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The compute request must be a JSON object.");
        }

        return new ComputeRequest
        {
            EquationSet = ReadString(root, "equationSet", defaults.EquationSet),
            TimeStepping = ReadString(root, "timeStepping", defaults.TimeStepping),
            TerrainElevation = ReadNumber(root, "terrainElevation", defaults.TerrainElevation),
            InitialStage = ReadNumber(root, "initialStage", defaults.InitialStage),
            Roughness = ReadNumber(root, "roughness", defaults.Roughness),
            PrecipitationRate = ReadNumber(root, "precipitationRate", defaults.PrecipitationRate),
            TimestepSeconds = ReadNumber(root, "timestepSeconds", defaults.TimestepSeconds),
            DurationSeconds = ReadNumber(root, "durationSeconds", defaults.DurationSeconds)
        };
    }

    private static string ReadString(JsonElement root, string propertyName, string fallback)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return fallback;
        }
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException($"{propertyName} must be a string.");
        }
        return value.GetString() ?? fallback;
    }

    private static double ReadNumber(JsonElement root, string propertyName, double fallback)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            return fallback;
        }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number))
        {
            throw new ArgumentException($"{propertyName} must be a number.");
        }
        return number;
    }

    private static void RequireRange(double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be between {minimum} and {maximum}.");
        }
    }

    private static string Failure(string error, ComputeRequest request, long steps = 0, double finalTime = 0) =>
        SerializeResponse(new ComputeResponse
        {
            Status = "failed",
            Error = error,
            SolverType = SolverTypeName(request.TimeStepping),
            Steps = steps,
            FinalTimeSeconds = finalTime
        });

    private static string SerializeResponse(ComputeResponse response) =>
        JsonSerializer.Serialize(response, EngineJsonContext.Default.ComputeResponse);

    private static string SolverTypeName(string timeStepping) =>
        timeStepping == "Implicit" ? "SolverImpSWE" : "SolverExpSWE";

    private static void PollCancellation(ProgressReporter reporter)
    {
        if (RuntimeHost.IsCancellationRequested())
        {
            reporter.RequestCancel();
            throw new OperationCanceledException();
        }
    }

    private sealed class RuntimeProgressTarget : IProgressTarget
    {
        public void WriteMessage(ProgressReporter sender, Message message)
        {
            if (RuntimeHost.IsCancellationRequested())
            {
                sender.RequestCancel();
            }
            RuntimeHost.ReportProgress(JsonSerializer.Serialize(new ProgressMessage
            {
                Progress = Math.Clamp(sender.MostRecentProgress, 0, 100),
                Message = message.EvaluatedMessage
            }, EngineJsonContext.Default.ProgressMessage));
        }

        public void WriteProgress(ProgressReporter sender, int localProgress, int globalProgress)
        {
            if (RuntimeHost.IsCancellationRequested())
            {
                sender.RequestCancel();
            }
            RuntimeHost.ReportProgress(JsonSerializer.Serialize(new ProgressMessage
            {
                Progress = Math.Clamp(globalProgress, 0, 100),
                Message = "Solving CPU hydraulic model"
            }, EngineJsonContext.Default.ProgressMessage));
        }
    }
}
