using Geospatial.GDALAssist;
using Geospatial.Rasters;
using System.Xml.Linq;

namespace Ras.Backend;

/// <summary>One actual GDAL raster subdataset exposed by an OpenFileGDB dataset.</summary>
public sealed record GdbRasterSubdatasetDescriptor(
    string Name,
    string ConnectionString,
    string DriverName,
    int Width,
    int Height,
    int BandCount,
    string DataType,
    double? NoDataValue,
    MapVectorSourceExtent? Extent,
    string? ProjectionWKT,
    bool CanRender,
    string Status,
    string? Diagnostic,
    string? MapType = null);

/// <summary>The live raster catalog and the exact directory manifest used to read it.</summary>
public sealed record GdbRasterCatalogResult(
    GdbMapSourceIdentity Source,
    GdbRasterSubdatasetDescriptor[] Subdatasets);

/// <summary>Loads one exact GDB-backed raster child from a persisted map tree.</summary>
public sealed record GdbRasterSourceLoadRequest(
    MapTreeRequest Map,
    CatalogTreeNodeLocator Node,
    GdbMapSourceIdentity? ExpectedSource = null);

/// <summary>Exact saved-map, GDB parent, raster child, and source-directory ownership.</summary>
public sealed record GdbRasterNodeCapture(
    MapSourceMapCapture Map,
    CatalogTreeNodeLocator ParentNode,
    CatalogTreeNodeLocator Node,
    GdbMapSourceIdentity Source);

/// <summary>
/// A live, read-only GDAL scope. Dataset, Raster, and the visualization snapshot are valid only
/// during the synchronous callback supplied to WithOpenedRasterSource.
/// </summary>
public sealed record GdbRasterOpenedSource(
    GdbRasterNodeCapture Capture,
    GdbRasterSubdatasetDescriptor Subdataset,
    GDALRaster Dataset,
    IResample<float> Raster,
    RasterDefinition BaseDefinition,
    double? NoDataValue,
    string? ProjectionWKT,
    XDocument VisualizationSnapshot);

/// <summary>A validated GDAL subdataset scope, independent of its source cell type.</summary>
public sealed record GdbRasterOpenedDataset(
    GdbRasterNodeCapture Capture,
    GdbRasterSubdatasetDescriptor Subdataset,
    GDALRaster Dataset,
    RasterDefinition BaseDefinition,
    XDocument VisualizationSnapshot);
