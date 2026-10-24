using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geospatial.GDALAssist;
using Geospatial.Features;
using Geospatial.IO;
using Geospatial.Rasters;
using Geospatial.Rasters.Classifications;
using Geospatial.Vectors;
using H5Assist;
using Ras.Layers;
using Ras.Layers.Classifiers;

namespace Ras.Backend;

public sealed record ClassificationImportFile(string File, string Revision, string Kind, string? Column = null, string? Name = null);
public sealed record ClassificationImportItem(ClassificationImportFile Selection, string[] Columns, int FeatureCount, int PreviewFeatureCount,
    string? GeoJson, SpatialGridPreview? Raster, int Rows = 0, int ColumnsCount = 0, int Bands = 0);
public sealed record ClassificationImportInspection(ClassificationImportItem[] Items, string Projection);
public sealed record ClassificationImportInspectRequest(ModelSaveRequest Model, string[] Files, ClassificationImportFile[]? Selections = null);
public sealed record ClassificationImportRequest(ModelSaveRequest Model, ClassificationImportFile[] Files);

public static partial class ModelEditorAdapter
{
    public static Task<ClassificationImportInspection> InspectClassificationAsync(ClassificationImportInspectRequest request)
    {
        CheckClassificationImport(request.Model);
        using var context = new ModelContext(request.Model);
        var layer = context.Layer as ClassificationLayer ?? throw new ArgumentException("This model is not a classification layer.");
        var result = ClassificationImportService.Inspect(layer, request);
        CheckClassificationImport(request.Model);
        return Task.FromResult(result);
    }
    public static Task<ModelSnapshot> ImportClassificationAsync(ClassificationImportRequest request)
    {
        CheckClassificationImport(request.Model);
        return SaveAsync(request.Model, classificationImport: request.Files);
    }
    private static void CheckClassificationImport(ModelSaveRequest model)
    {
        VerifyKind(model);
        if (model.Kind != "Surface Layers" || string.IsNullOrEmpty(model.ProjectPath)) throw new ArgumentException("Classification imports require the owning project.");
        if (Revision(model.AssetPath) != model.Revision) throw new SourceFileChangedException();
    }
}

internal static class ClassificationImportService
{
    private static string Kind(string file) => Path.GetExtension(file).ToLowerInvariant() switch {
        ".tif" or ".tiff" => "Raster", ".shp" => "Polygon", _ => throw new ArgumentException("Only TIFF rasters and polygon shapefiles can be imported into a classification layer.")
    };
    internal static string Hash(string file)
    {
        file = Path.GetFullPath(file);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        string[] paths = Kind(file) == "Polygon" ? [file, Path.ChangeExtension(file, ".shx"), Path.ChangeExtension(file, ".dbf"), Path.ChangeExtension(file, ".prj"), Path.ChangeExtension(file, ".cpg")]
            : (GDALRaster.GetVRTSources(file) ?? [file]).Append(file).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        foreach (string path in paths)
        {
            byte[] name = Encoding.UTF8.GetBytes(path); hash.AppendData(BitConverter.GetBytes(name.Length)); hash.AppendData(name);
            if (!File.Exists(path)) { hash.AppendData(BitConverter.GetBytes(-1L)); continue; }
            using var input = File.OpenRead(path); hash.AppendData(BitConverter.GetBytes(input.Length)); byte[] bytes = new byte[65536]; int count;
            while ((count = input.Read(bytes)) > 0) hash.AppendData(bytes.AsSpan(0, count));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static PolygonCollectionFeatureCollection ReadPolygons(string file, Projection? projection)
    {
        if (!ShapefileIO.IsPolygonShapefile(file)) throw new ArgumentException("Choose a polygon shapefile.");
        PolygonCollectionFeatureCollection collection;
        var result = projection == null ? ShapefileIO.TryRead(file, out collection) : ShapefileIO.TryRead(file, out collection, projection);
        if (!result || collection == null) throw new InvalidDataException(result.GetConcatenatedMessages());
        return collection;
    }
    private static string[] StringColumns(PolygonCollectionFeatureCollection collection) => collection.AttributeTable.Columns.Where(column => column.Type == typeof(string)).Select(column => column.Name).ToArray();
    private static string Class(PolygonCollectionFeatureCollection collection, int row, string? column)
    {
        if (string.IsNullOrEmpty(column)) return "NoData";
        if (!StringColumns(collection).Contains(column, StringComparer.Ordinal)) throw new ArgumentException("Select an existing string classification column.");
        return collection.AttributeTable[row][collection.AttributeTable.GetColumnIndex(column)]?.ToString() ?? "NoData";
    }
    public static ClassificationImportInspection Inspect(ClassificationLayer layer, ClassificationImportInspectRequest request)
    {
        List<ClassificationImportItem> items = [];
        foreach (string file in request.Files.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal))
        {
            string kind = Kind(file), revision = Hash(file);
            var prior = request.Selections?.SingleOrDefault(item => Path.GetFullPath(item.File) == file && item.Revision == revision);
            var selection = new ClassificationImportFile(file, revision, kind, prior?.Column, prior?.Name ?? Path.GetFileNameWithoutExtension(file));
            if (kind == "Raster")
            {
                using var raster = new GDALRaster(file);
                using var data = new GdalBandedRasterDataSource<float>(raster);
                var preview = SpatialGridService.Preview(data.AsBandedRasterizer(), 0);
                var definition = data.RasterDefinition;
                items.Add(new(selection, [], 0, 0, null, preview, definition.Rows, definition.Cols, raster.GetBandCount()));
            }
            else
            {
                var collection = ReadPolygons(file, layer.Project.Projection); string[] columns = StringColumns(collection);
                if (prior == null) selection = selection with { Column = columns.FirstOrDefault() };
                if (selection.Column != null && selection.Column.Length > 0 && !columns.Contains(selection.Column, StringComparer.Ordinal)) throw new ArgumentException("Select an existing string classification column.");
                var features = new List<object>(); int count = 0, previewPoints = 0;
                for (int row = 0; row < collection.Features.Count; row++) foreach (Polygon polygon in collection.Features[row])
                {
                    count++;
                    if (previewPoints + polygon.Points.Count > 20000 || features.Count >= 500) continue;
                    var points = polygon.Points.Select(point => new[] { point.X, point.Y }).ToList();
                    if (points.Count < 3) continue;
                    if (!points[0].SequenceEqual(points[^1])) points.Add(points[0]);
                    features.Add(new { type = "Feature", geometry = new { type = "Polygon", coordinates = new[] { points } }, properties = new { Classification = Class(collection, row, selection.Column) } });
                    previewPoints += points.Count;
                }
                if (count == 0) throw new ArgumentException("The shapefile contains no polygons.");
                string geo = JsonSerializer.Serialize(new { type = "FeatureCollection", features }, BackendJson.Options);
                items.Add(new(selection, columns, count, features.Count, geo, null));
            }
            if (Hash(file) != revision) throw new SourceFileChangedException();
        }
        return new(items.ToArray(), layer.Project.Projection?.GetWKT() ?? "");
    }
    public static int Import(ClassificationLayer layer, ClassificationImportFile[] files, List<string> created)
    {
        if (files.Length == 0 || files.Select(item => Path.GetFullPath(item.File)).Distinct(StringComparer.Ordinal).Count() != files.Length) throw new ArgumentException("Choose distinct classification input files.");
        Verify(files); int selected = -1;
        foreach (var item in files)
        {
            if (item.Kind != Kind(item.File)) throw new ArgumentException("The classification input type does not match its file.");
            if (item.Kind == "Raster")
            {
                string name = item.Name?.Trim() ?? Path.GetFileNameWithoutExtension(item.File);
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(H5Reader.SanitizeObjectName(name))) throw new ArgumentException("Classification input names must not be empty.");
                if (layer.Layers.Any(input => H5Reader.SanitizeObjectName(input.Name) == H5Reader.SanitizeObjectName(name))) throw new ArgumentException("Classification input names must map to distinct HDF groups.");
                // Publish each owned raster without overwriting any existing file.
                // The model HDF is replaced only after every import/save succeeds.
                string target = Path.Combine(layer.SourceDirectory, "classification-" + Guid.NewGuid().ToString("N") + ".tif"); created.Add(target);
                var config = RasterFileConverterConfig.Default(); config.DestinationProjection = layer.Project.Projection;
                var result = TiffExportEngine.ConvertFile<int>(item.File, target, config);
                if (!result) throw new IOException(result.GetConcatenatedMessages());
                using var raster = TiffDataSource<int>.TryLoad(target) ?? throw new InvalidDataException("The imported classification raster could not be read.");
                var tiles = raster.BasePyramidLevel; var classes = new HashSet<int>(); int[] buffer = new int[tiles.BlockSize.PixelCount];
                for (int index = 0; index < tiles.BlockCount(); index++) if (tiles.ReadBlock(index, buffer)) foreach (int value in buffer) classes.Add(value);
                var classifier = new Classification(); var standard = ClassificationStandards.NLCD2011;
                foreach (int value in classes.Order()) classifier.Set(value, standard.KeyMap.ContainsKey(value) ? standard.KeyMap.Forward[value] : value.ToString(CultureInfo.InvariantCulture));
                if (raster.NoDataValue is int noData) classifier.Set(noData, "NoData");
                var input = layer.AddNewRasterLayer(target, classifier); input.Name = name; selected = layer.LayerCount - 1;
            }
            else
            {
                var collection = ReadPolygons(item.File, layer.Project.Projection);
                // Match ObservableClassificationLayer.GetOrMakePolygonClassificationInput.
                var input = layer.Layers.OfType<PolygonClassificationInput>().FirstOrDefault() ?? layer.AddNewPolygonLayer("Classification Polygons");
                int count = 0;
                for (int row = 0; row < collection.Features.Count; row++) foreach (Polygon polygon in collection.Features[row])
                {
                    if (polygon.Points.Count < 3 || polygon.Points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y))) throw new ArgumentException("The shapefile contains an invalid polygon.");
                    input.Add(polygon, Class(collection, row, item.Column)); count++;
                }
                if (count == 0) throw new ArgumentException("The shapefile contains no polygons.");
                selected = layer.Layers.ToList().IndexOf(input);
            }
        }
        Verify(files);
        return selected;
    }
    public static void Verify(IEnumerable<ClassificationImportFile> files)
    {
        foreach (var item in files) if (Hash(item.File) != item.Revision) throw new SourceFileChangedException();
    }
}
