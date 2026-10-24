using System.Globalization;
using Geospatial.Features;
using Geospatial.IO;
using Utility.Extensions;
using Utility.Memory;
using Visual.Observables;

namespace Ras.Backend;

public sealed record AttributeTableLoadRequest(string Path);
public sealed record AttributeColumn(string Id, string Name, string Type, bool ReadOnly = false);
public sealed record AttributeRow(string Id, Dictionary<string, string?> Values);
public sealed record AttributeTableData(string SourcePath, string ShapeType, AttributeColumn[] Columns, AttributeRow[] Rows, bool InitValueTypesToDefault = false);
public sealed record AttributeCellEdit(string RowId, string ColumnId, string? Value);
public sealed record AttributeMutation(string Operation, string? ColumnId = null, string? Name = null, string? Type = null, AttributeCellEdit[]? Cells = null);
public sealed record AttributeTableTransformRequest(AttributeTableData Table, AttributeMutation Mutation);

public static class AttributeTableService
{
    public static AttributeTableData Load(AttributeTableLoadRequest request)
    {
        string path = System.IO.Path.GetFullPath(request.Path);
        if (!File.Exists(path)) throw new FileNotFoundException("The shapefile was not found.", path);
        if (!string.Equals(System.IO.Path.GetExtension(path), ".shp", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a Shapefile (.shp).");
        Table? table;
        string shape;
        Utility.Logging.OperationResult result;
        if (ShapefileIO.IsPolylineShapefile(path))
        {
            result = ShapefileIO.TryRead(path, out PolylineCollectionFeatureCollection collection);
            table = collection?.AttributeTable!; shape = "Polyline";
        }
        else if (ShapefileIO.IsPolygonShapefile(path))
        {
            result = ShapefileIO.TryRead(path, out PolygonCollectionFeatureCollection collection);
            table = collection?.AttributeTable!; shape = "Polygon";
        }
        else if (ShapefileIO.IsPointShapefile(path))
        {
            result = ShapefileIO.TryRead(path, out PointFeatureCollection collection);
            table = collection?.AttributeTable!; shape = "Point";
        }
        else throw new NotSupportedException("Unsupported shapefile type.");
        if (!result || table is null) throw new InvalidDataException(result.GetConcatenatedMessages());
        if (table.Rows.Count > 100000 || table.Columns.Count > 512 || (long)table.Rows.Count * table.Columns.Count > 5000000)
            throw new NotSupportedException("This attribute table exceeds the viewer's current capacity.");
        var columns = table.Columns.Select((column, index) => new AttributeColumn("column/" + index, column.Name, column.Type.Name, column.IsReadOnly)).ToArray();
        return Snapshot(table, path, shape, columns, Enumerable.Range(0, table.Rows.Count).Select(i => "feature/" + i).ToArray());
    }

    public static AttributeTableData Transform(AttributeTableTransformRequest request)
    {
        var source = request.Table;
        Validate(source);
        var table = new Table { InitValueTypesToDefault = source.InitValueTypesToDefault };
        foreach (var column in source.Columns) table.AddColumn(new TableColumn(column.Name, SourceType(column.Type), column.ReadOnly));
        foreach (var row in source.Rows)
            table.AddNewRow(source.Columns.Select(column => Parse(row.Values[column.Id], SourceType(column.Type))!).ToArray());
        var observable = new ObservableTable(table);
        var columns = source.Columns.ToList();
        var mutation = request.Mutation;
        int index = mutation.ColumnId is null ? -1 : columns.FindIndex(column => column.Id == mutation.ColumnId);
        switch (mutation.Operation)
        {
            case "addColumn":
                ValidateName(columns, mutation.Name, null);
                if (!Enum.TryParse<TableType>(mutation.Type, false, out var type) || !Enum.IsDefined(type)) throw new ArgumentException("Choose a supported column type.");
                if (!observable.TryEnsureColumn(mutation.Name!, type.AsType(), out _)) throw new ArgumentException("The column already exists.");
                columns.Add(new AttributeColumn(Guid.NewGuid().ToString(), mutation.Name!, type.AsType().Name));
                break;
            case "renameColumn":
                RequireColumn(index); ValidateName(columns, mutation.Name, mutation.ColumnId);
                if (columns[index].Name != mutation.Name && !observable.TryRenameColumn(index, mutation.Name!)) throw new ArgumentException("The column could not be renamed.");
                columns[index] = columns[index] with { Name = mutation.Name! };
                break;
            case "removeColumn":
                RequireColumn(index); observable.RemoveColumn(index); columns.RemoveAt(index);
                break;
            case "setCells":
                var seen = new HashSet<(string, string)>();
                foreach (var cell in mutation.Cells ?? [])
                {
                    int rowIndex = Array.FindIndex(source.Rows, row => row.Id == cell.RowId);
                    int colIndex = columns.FindIndex(column => column.Id == cell.ColumnId);
                    if (rowIndex < 0 || colIndex < 0 || columns[colIndex].ReadOnly || !seen.Add((cell.RowId, cell.ColumnId)))
                        throw new ArgumentException("The edited cell is no longer available.");
                    observable.SetCellValue(rowIndex, colIndex, Parse(cell.Value, SourceType(columns[colIndex].Type))!);
                }
                break;
            default: throw new ArgumentException("Unsupported attribute table operation.");
        }
        return Snapshot(table, source.SourcePath, source.ShapeType, columns.ToArray(), source.Rows.Select(row => row.Id).ToArray());
    }
    private static void RequireColumn(int index) { if (index < 0) throw new ArgumentException("The column no longer exists."); }
    private static void ValidateName(IEnumerable<AttributeColumn> columns, string? name, string? excluded)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a column name.");
        if (columns.Any(column => column.Id != excluded && column.Name.RoughlyEquals(name))) throw new ArgumentException("A column with this name already exists.");
    }
    private static void Validate(AttributeTableData source)
    {
        if (source.Rows.Length > 100000 || source.Columns.Length > 512 || (long)source.Rows.Length * source.Columns.Length > 5000000)
            throw new ArgumentException("This attribute table exceeds the viewer's current capacity.");
        if (source.Columns.Any(c => string.IsNullOrEmpty(c.Id) || string.IsNullOrWhiteSpace(c.Name)) || source.Rows.Any(r => string.IsNullOrEmpty(r.Id))
            || source.Columns.Select(c => c.Id).Distinct().Count() != source.Columns.Length || source.Rows.Select(r => r.Id).Distinct().Count() != source.Rows.Length)
            throw new ArgumentException("Invalid attribute table identities.");
        var keys = source.Columns.Select(c => c.Id).ToHashSet();
        if (source.Rows.Any(row => !keys.SetEquals(row.Values.Keys))) throw new ArgumentException("Attribute table cells do not match its columns.");
    }
    private static AttributeTableData Snapshot(Table table, string path, string shape, AttributeColumn[] columns, string[] rowIds)
    {
        var rows = table.Rows.Select((row, index) => new AttributeRow(rowIds[index], columns.Select((column, col) => (column.Id, Value: Format(row[col]))).ToDictionary(x => x.Id, x => x.Value))).ToArray();
        return new(path, shape, columns, rows, table.InitValueTypesToDefault);
    }
    private static Type SourceType(string name) => name switch
    {
        "String" => typeof(string), "Int32" => typeof(int), "Double" => typeof(double), "Single" => typeof(float), "Boolean" => typeof(bool),
        _ => throw new NotSupportedException("Unsupported source attribute column type: " + name)
    };
    private static string? Format(object? value) => value switch
    {
        null => null, double number => number.ToString("R", CultureInfo.InvariantCulture), float number => number.ToString("R", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture), _ => value.ToString()
    };
    private static object? Parse(string? value, Type type)
    {
        if (value is null) return null;
        if (type == typeof(string)) return value;
        if (type == typeof(int) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer)) return integer;
        if (type == typeof(double) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) return number;
        if (type == typeof(float) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float single)) return single;
        if (type == typeof(bool) && bool.TryParse(value, out bool boolean)) return boolean;
        throw new FormatException("A cell value is not valid for its column type.");
    }
}
