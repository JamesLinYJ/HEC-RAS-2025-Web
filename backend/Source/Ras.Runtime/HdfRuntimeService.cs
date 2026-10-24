using System;
using System.IO;
using HDF.PInvoke;

namespace Ras.Runtime;

public sealed record HdfRuntimeInfo(string Version, bool IsThreadSafe);

public static class HdfRuntimeService
{
    private static readonly Lazy<HdfRuntimeInfo> Runtime = new(ReadRuntime);

    public static HdfRuntimeInfo Inspect() => Runtime.Value;

    public static void Configure()
    {
        if (!Inspect().IsThreadSafe)
            throw new PlatformNotSupportedException(
                "The HDF5 runtime must support concurrent source reads and file finalization. Install the thread-safe HDF5 runtime package.");
    }

    private static HdfRuntimeInfo ReadRuntime()
    {
        uint major = 0, minor = 0, release = 0;
        bool isThreadSafe = false;
        if (H5.get_libversion(ref major, ref minor, ref release) < 0 ||
            H5.is_library_threadsafe(ref isThreadSafe) < 0)
            throw new InvalidDataException("The loaded HDF5 runtime could not report its capabilities.");
        return new HdfRuntimeInfo($"{major}.{minor}.{release}", isThreadSafe);
    }
}
