using System.Runtime.InteropServices.JavaScript;

namespace Ras.Wasm;

internal static partial class RuntimeHost
{
#if RAS_BROWSER_WASM
    [JSImport("reportProgress", "runtime-host")]
    internal static partial void ReportProgress(string json);

    [JSImport("isCancellationRequested", "runtime-host")]
    internal static partial bool IsCancellationRequested();
#else
    internal static void ReportProgress(string json)
    {
    }

    internal static bool IsCancellationRequested() => false;
#endif
}
