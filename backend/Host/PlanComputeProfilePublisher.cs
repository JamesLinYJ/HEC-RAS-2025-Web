using System.Globalization;
using Ras.Output;

namespace Ras.Backend;

/// <summary>
/// Bridges the retained writer's in-memory TimestepAdded metadata. It never
/// opens the Result HDF, asks a mapper for values, or invents a file revision.
/// Each event runs after OutputBlock adds both its timestamp and profile name.
/// </summary>
internal sealed class PlanComputeProfilePublisher : IDisposable
{
    private const int MaximumProfilesPerPacket = 512;
    private readonly IHorizontalOutputBlock? block;
    private readonly Action<BackendOperationProgress> report;
    private readonly object gate = new();
    private int publishedCount;
    private bool disposed;
    private bool reportedMismatch;

    public PlanComputeProfilePublisher(IHorizontalOutputBlock? block,
        Action<BackendOperationProgress> report)
    {
        this.block = block;
        this.report = report;
        if (block is null) return;
        block.TimestepAdded += PublishNewProfiles;
        PublishNewProfiles();
    }

    private void PublishNewProfiles()
    {
        lock (gate)
        {
            if (disposed || block is null) return;
            int count = block.ProfileCount;
            if (count < publishedCount || block.Times.Count != count || block.ProfileNames.Count != count)
            {
                if (!reportedMismatch)
                {
                    reportedMismatch = true;
                    report(new(ResultProfiles: new(block.Name, publishedCount, count, [],
                        "The source output profile names and times are inconsistent.")));
                }
                return;
            }
            reportedMismatch = false;
            while (publishedCount < count)
            {
                int start = publishedCount;
                int length = Math.Min(MaximumProfilesPerPacket, count - start);
                var profiles = new ResultComputeProfile[length];
                for (int i = 0; i < length; i++)
                {
                    int index = start + i;
                    profiles[i] = new(index, block.ProfileNames[index],
                        block.Times[index].ToString("O", CultureInfo.InvariantCulture));
                }
                report(new(ResultProfiles: new(block.Name, start, count, profiles)));
                publishedCount += length;
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            if (block is not null) block.TimestepAdded -= PublishNewProfiles;
        }
    }
}
