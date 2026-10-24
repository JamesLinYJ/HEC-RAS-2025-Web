using System.Text.Json;

namespace Ras.Backend;

public sealed record BackendOperationProgress(double? Fraction = null, string? Message = null,
    string? TaskName = null, IReadOnlyDictionary<string, string>? Values = null,
    ResultComputeEnvelope? ResultCompute = null, ResultComputeProfilesUpdate? ResultProfiles = null,
    PlanComputeLiveMapRenderFrame? LiveMapFrame = null, PlanComputeJobMessageUpdate? PlanMessages = null,
    PlanComputeLiveVelocityFrame? LiveVelocityFrame = null,
    PlanComputeLiveVelocityUnavailable? LiveVelocityUnavailable = null,
    PlanComputeLiveCulvertFrame? LiveCulvertFrame = null,
    PlanComputeLiveCulvertUnavailable? LiveCulvertUnavailable = null,
    StreamModeMapFrame? StreamMapFrame = null);

public sealed record ResultComputeEnvelope(string RunId, string ProjectPath, string ProjectRevision,
    string ProjectXmlFingerprint, string PlanPath, string PlanRevision, string? ResultPath,
    string State, string? ResultRevision = null, string? FailureReason = null, string? ResultName = null);

/// <summary>Actual in-memory OutputBlock profile metadata, captured in its TimestepAdded callback.</summary>
public sealed record ResultComputeProfile(int Index, string Name, string Time);
public sealed record ResultComputeProfilesUpdate(string OutputBlockName, int StartIndex,
    int ProfileCount, ResultComputeProfile[] Profiles, string? ErrorMessage = null);

/// <summary>Serializes source progress and the single terminal response on stdout.</summary>
internal sealed class BackendProtocolWriter(TextWriter output)
{
    private readonly object gate = new();
    private bool finished;

    public void Progress(string id, BackendOperationProgress progress)
    {
        lock (gate)
        {
            if (finished) return;
            Write(new { id, progress }, newline: true);
        }
    }

    public void Complete(object response, bool stream)
    {
        lock (gate)
        {
            if (finished) throw new InvalidOperationException("The backend response has already completed.");
            // Serialization can fail for an unsupported source value. Leave
            // the terminal slot available for the protocol error in that case.
            string json = JsonSerializer.Serialize(response, BackendJson.Options);
            finished = true;
            WriteSerialized(json, newline: stream);
        }
    }

    private void Write(object response, bool newline)
    {
        WriteSerialized(JsonSerializer.Serialize(response, BackendJson.Options), newline);
    }

    private void WriteSerialized(string json, bool newline)
    {
        output.Write(json);
        if (newline) output.WriteLine();
        output.Flush();
    }
}
