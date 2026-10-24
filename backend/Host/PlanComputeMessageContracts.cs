using System.Collections.Specialized;
using Ras.Layers;
using Utility.Logging;
using Visual.Observables;

namespace Ras.Backend;

public sealed record PlanDiagnosticTarget(string Kind, string ProjectPath, string ProjectRevision,
    string PlanPath, string PlanRevision, string AssetPath, string AssetRevision,
    string? FeatureID = null, string? FeatureName = null);

public sealed record PlanComputeJobLogDatum(string Key, string? Value,
    PlanDiagnosticTarget? Target = null);

public sealed record PlanComputeJobMessage(int Index, string Code, string DefaultSeverity,
    string EvaluatedMessage, PlanComputeJobLogDatum[] LogData);

public sealed record PlanComputeJobMessageUpdate(string RunId, PlanComputeJobMessage[] Messages);

/// <summary>
/// Streams exact append-only source BatchJob.Messages entries. The terminal
/// OperationResult is projected separately and may aggregate repeated messages.
/// </summary>
internal sealed class PlanComputeJobMessageCapture : IDisposable
{
    private readonly object gate = new();
    private readonly BatchJob job;
    private readonly string runId;
    private readonly Func<int, Message, PlanComputeJobMessage> project;
    private readonly Action<PlanComputeJobMessageUpdate> publish;
    private int nextIndex;
    private bool disposed;

    // The Windows caller gives BatchJob a UI synchronization context. The
    // headless host must preserve that serialization when solver tasks report
    // from different threads; ObservableCollection itself is not thread-safe.
    internal static BatchJob CreateJob(Utility.Progress.ProgressReporter reporter, string name)
    {
        object sourceCollectionGate = new();
        var context = new Utility.SynchronizationContext(action =>
        {
            lock (sourceCollectionGate) action?.Invoke();
        });
        return new BatchJob(reporter, name, uiThreadSyncContext: context);
    }

    public PlanComputeJobMessageCapture(BatchJob job, string runId,
        Func<int, Message, PlanComputeJobMessage> project,
        Action<PlanComputeJobMessageUpdate> publish)
    {
        this.job = job ?? throw new ArgumentNullException(nameof(job));
        this.runId = string.IsNullOrWhiteSpace(runId)
            ? throw new ArgumentException("A compute run identity is required.", nameof(runId))
            : runId;
        this.project = project ?? throw new ArgumentNullException(nameof(project));
        this.publish = publish ?? throw new ArgumentNullException(nameof(publish));
        ((INotifyCollectionChanged)job.Messages).CollectionChanged += MessagesChanged;
        PublishAppended();
    }

    public void Flush() => PublishAppended();

    private void MessagesChanged(object? sender, NotifyCollectionChangedEventArgs args) => PublishAppended();

    private void PublishAppended()
    {
        lock (gate)
        {
            if (disposed || job.Messages.Count <= nextIndex) return;

            var appended = new PlanComputeJobMessage[job.Messages.Count - nextIndex];
            for (int index = nextIndex; index < job.Messages.Count; index++)
                appended[index - nextIndex] = project(index, job.Messages[index]);

            publish(new PlanComputeJobMessageUpdate(runId, appended));
            nextIndex = job.Messages.Count;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            ((INotifyCollectionChanged)job.Messages).CollectionChanged -= MessagesChanged;
        }
    }
}
