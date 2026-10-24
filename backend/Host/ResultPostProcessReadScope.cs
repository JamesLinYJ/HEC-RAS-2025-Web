using Ras.Layers;
using Ras.Layers.Results;

namespace Ras.Backend;

/// <summary>
/// UI readers run in independent backend processes. A unique cache directory
/// prevents them from deleting or transposing another reader's PostProcess.h5.
/// Source Result data and the retained postprocessing algorithm stay unchanged.
/// </summary>
internal sealed class ResultPostProcessReadScope : IDisposable
{
    private readonly string directory;
    private bool disposed;

    internal PostProcessor Processor { get; }

    internal ResultPostProcessReadScope(Result result)
    {
        directory = Path.Combine(Path.GetTempPath(), "HEC", "RAS", "PostProcessReads", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { Processor = PostProcessor.CreateReadCache(result, Path.Combine(directory, "PostProcess.h5")); }
        catch
        {
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { Processor.Dispose(); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
