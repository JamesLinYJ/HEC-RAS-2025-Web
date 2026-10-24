using Geospatial.GDALAssist;

namespace Ras.Runtime;

/// <summary>Initializes the same packaged native runtimes for the desktop host and command-line client.</summary>
public static class RasRuntimeBootstrap
{
    public static void Configure()
    {
        DssRuntimeService.Configure();
        GDALSetup.InitializeMultiplatform();
        HdfRuntimeService.Configure();
    }
}
