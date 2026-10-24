using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Hec.Dss;

namespace Ras.Runtime;

public sealed record DssRuntimeInformation(bool Available, string LibraryPath, string? Error);

public static class DssRuntimeService
{
    private static readonly Lazy<nint> Library = new(LoadLibrary);
    private static readonly string[] ReadExports = ["hec_dss_open", "hec_dss_close", "hec_dss_getVersion",
        "hec_dss_record_count", "hec_dss_CONSTANT_MAX_PATH_SIZE", "hec_dss_catalog", "hec_dss_dataType",
        "hec_dss_set_value", "hec_dss_tsGetDateTimeRange", "hec_dss_tsGetSizes", "hec_dss_tsRetrieve",
        "hec_dss_tsRetrieveInfo", "hec_dss_pdRetrieveInfo", "hec_dss_pdRetrieve", "hec_dss_locationRetrieve",
        "hec_dss_dateToYearMonthDay", "hec_dss_dateToJulian", "hec_dss_julianToYearMonthDay"];

    static DssRuntimeService() => NativeLibrary.SetDllImportResolver(typeof(DssReader).Assembly, Resolve);

    public static void Configure() { }

    public static string LibraryPath => Path.Combine(AppContext.BaseDirectory,
        OperatingSystem.IsMacOS() ? "libhecdss.dylib" : OperatingSystem.IsWindows() ? "hecdss.dll" : "libhecdss.so");

    public static DssRuntimeInformation Inspect()
    {
        try { RequireAvailable(); return new(true, LibraryPath, null); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { return new(false, LibraryPath, error.Message); }
    }

    public static void RequireAvailable() => _ = Library.Value;

    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
        => name == "hecdss" ? Library.Value : nint.Zero;

    private static nint LoadLibrary()
    {
        if (!File.Exists(LibraryPath))
            throw new DllNotFoundException("The DSS runtime is not packaged at " + LibraryPath);
        nint library = NativeLibrary.Load(LibraryPath);
        try
        {
            foreach (string name in ReadExports)
                if (!NativeLibrary.TryGetExport(library, name, out _))
                    throw new EntryPointNotFoundException("The packaged DSS runtime is missing " + name);
            return library;
        }
        catch { NativeLibrary.Free(library); throw; }
    }
}
