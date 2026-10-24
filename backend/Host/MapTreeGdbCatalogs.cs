using Geospatial.IO;

namespace Ras.Backend;

/// <summary>Shares GDB raster catalogs only for one map tree construction.</summary>
internal sealed class MapTreeGdbCatalogs
{
    private static readonly StringComparer SourcePathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly Dictionary<string, Lazy<GdbRasterCatalogResult>> _catalogs =
        new(SourcePathComparer);
    private readonly Func<string, GdbRasterCatalogResult> _catalog;
    private readonly Action<GdbMapSourceIdentity> _verify;

    internal MapTreeGdbCatalogs()
        : this(sourcePath => GdbRasterSourceService.CatalogSubdatasets(sourcePath),
            GdbMapSourceService.VerifySource)
    {
    }

    internal MapTreeGdbCatalogs(Func<string, GdbRasterCatalogResult> catalog,
        Action<GdbMapSourceIdentity> verify)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(verify);
        _catalog = catalog;
        _verify = verify;
    }

    internal GdbRasterCatalogResult Get(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        string canonicalPath = SourceFilePaths.Canonical(Path.GetFullPath(sourcePath));
        if (!_catalogs.TryGetValue(canonicalPath, out Lazy<GdbRasterCatalogResult>? catalog))
        {
            catalog = new Lazy<GdbRasterCatalogResult>(() => _catalog(canonicalPath),
                LazyThreadSafetyMode.ExecutionAndPublication);
            _catalogs.Add(canonicalPath, catalog);
        }

        return catalog.Value;
    }

    internal void VerifySuccessfulSources()
    {
        foreach (Lazy<GdbRasterCatalogResult> catalog in _catalogs.Values)
        {
            if (catalog.IsValueCreated)
                _verify(catalog.Value.Source);
        }
    }
}
