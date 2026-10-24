using Geospatial.GDALAssist;
using OSGeo.GDAL;

namespace Ras.Backend;

public sealed record GdalRuntimeInformation(string Directory, string Version, string ReleaseName,
    string DataDirectory, string[] ExpectedLocations);

/// <summary>
/// The source GDAL information dialog describes the loaded runtime. Report
/// this process's actual retained GDAL setup and packaged native dependency,
/// rather than copying the source Windows installation paths into macOS UI.
/// </summary>
public static class GdalRuntimeService
{
    public static GdalRuntimeInformation Inspect()
    {
        GDALSetup.InitializeMultiplatform();
        string directory = GDALSetup.ToolDirectory;
        string version = GDALSetup.LoadedVersion;
        string dataDirectory = GDALSetup.GdalInitializedDataDirectory;
        if (!System.IO.Directory.Exists(directory) || !System.IO.Directory.Exists(dataDirectory)
            || string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException("The loaded GDAL installation has incomplete runtime information.");

        List<string> locations = [Path.Combine(AppContext.BaseDirectory, "GDAL")];
        string? configured = Environment.GetEnvironmentVariable("RAS_GDAL");
        locations.Add(string.IsNullOrWhiteSpace(configured) ? "ENV:RAS_GDAL" : configured);
        locations.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Work", "GDAL"));
        locations.Add(AppContext.BaseDirectory);
        return new(directory, version, Gdal.VersionInfo("RELEASE_NAME"), dataDirectory,
            locations.Distinct(StringComparer.Ordinal).ToArray());
    }
}
