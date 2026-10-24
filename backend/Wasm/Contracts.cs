using System;
using System.Text.Json.Serialization;

namespace Ras.Wasm;

internal sealed class ComputeRequest
{
    public ComputeRequest()
    {
    }

    public string EquationSet { get; init; } = "SWE";
    public string TimeStepping { get; init; } = "Explicit";
    public double TerrainElevation { get; init; } = 42;
    public double InitialStage { get; init; } = 43;
    public double Roughness { get; init; } = 0.035;
    public double PrecipitationRate { get; init; } = 0.000001;
    public double TimestepSeconds { get; init; } = 0.1;
    public double DurationSeconds { get; init; } = 4;
}

internal sealed class RuntimeInfoResponse
{
    public string Runtime { get; init; } = string.Empty;
    public string Solver { get; init; } = string.Empty;
    public string[] SolverTypes { get; init; } = Array.Empty<string>();
    public string ComputePath { get; init; } = string.Empty;
    public string Fixture { get; init; } = string.Empty;
    public bool ProjectFileCompute { get; init; }
    public bool Hdf5ProjectInput { get; init; }
    public bool Cancellation { get; init; }
}

internal sealed class ComputeResponse
{
    public string Status { get; init; } = "failed";
    public string Error { get; init; } = string.Empty;
    public string SolverType { get; init; } = string.Empty;
    public long Steps { get; init; }
    public double FinalTimeSeconds { get; init; }
    public int WetCellCount { get; init; }
    public double[] CellWaterSurface { get; init; } = Array.Empty<double>();
    public double[] CellDepth { get; init; } = Array.Empty<double>();
    public double[] FaceVelocity { get; init; } = Array.Empty<double>();
}

internal sealed class ProgressMessage
{
    public int Progress { get; init; }
    public string Message { get; init; } = string.Empty;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ComputeRequest))]
[JsonSerializable(typeof(RuntimeInfoResponse))]
[JsonSerializable(typeof(ComputeResponse))]
[JsonSerializable(typeof(ProgressMessage))]
internal partial class EngineJsonContext : JsonSerializerContext
{
}
