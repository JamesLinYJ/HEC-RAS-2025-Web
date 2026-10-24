using System.Globalization;
using Utility.Filtering;

namespace Ras.Backend;

public sealed record TableFilterColumn(string Id, string Name, string SourceType, bool Filterable = true);
public sealed record TableFilterRow(string Id, Dictionary<string, string?> Values);
public sealed record TableFilterRequest(string Expression, TableFilterColumn[] Columns, TableFilterRow[] Rows);
public sealed record TableFilterResult(bool Valid, string[] RowIds, string? Error = null, bool ClearActiveFilter = false);

/// <summary>
/// Runs the same retained parser and evaluator as the original DataGrid. Input
/// is the captured editor draft, not a reopened file that would lose draft rows.
/// This operation never writes the project, table, or model.
/// </summary>
public static class TableFilterService
{
    public static TableFilterResult Evaluate(TableFilterRequest request)
    {
        if (request.Columns.Any(column => string.IsNullOrEmpty(column.Id)) ||
            request.Rows.Any(row => string.IsNullOrEmpty(row.Id)) ||
            request.Columns.Select(column => column.Id).Distinct(StringComparer.Ordinal).Count() != request.Columns.Length ||
            request.Rows.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() != request.Rows.Length)
            throw new ArgumentException("Invalid table filter row or column identities.");
        if (string.IsNullOrWhiteSpace(request.Expression))
            return new(true, request.Rows.Select(row => row.Id).ToArray());
        if (!FilterExpression.TryParse(request.Expression, out FilterExpression expression, out string error))
            return new(false, [], error);

        // DataGrid.BuildFilterRow uses the original header, not localized text,
        // and TryAdd makes the first of duplicate normalized headers authoritative.
        var fields = new Dictionary<string, TableFilterColumn>(StringComparer.Ordinal);
        foreach (TableFilterColumn column in request.Columns.Where(column => column.Filterable))
        {
            if (string.IsNullOrWhiteSpace(column.Name)) continue;
            fields.TryAdd(FilterNames.Normalize(column.Name), column);
            if (FilterText.TryFieldReference(column.Name, out string reference))
                fields.TryAdd(FilterNames.Normalize(reference), column);
        }
        foreach (FilterField field in expression.GetFields())
            if (!fields.ContainsKey(field.NormalizedName))
                return new(false, [], "Filter cleared - there is no column named '" + field.RawName + "'.", true);

        try
        {
            var matched = new List<string>();
            foreach (TableFilterRow source in request.Rows)
            {
                var row = new CapturedFilterRow(source, fields);
                if (expression.Evaluate(row)) matched.Add(source.Id);
            }
            return new(true, matched.ToArray());
        }
        catch (Exception failure) when (failure is FormatException or OverflowException or ArgumentException)
        {
            return new(false, [], failure.Message);
        }
    }

    // Source DataGrid accessors are lazy; an unrelated invalid text draft must
    // not fail a filter, and AND/OR keep the retained evaluator's short circuit.
    private sealed class CapturedFilterRow(TableFilterRow row, IReadOnlyDictionary<string, TableFilterColumn> fields) : IFilterRow
    {
        public bool TryGetValue(string normalizedName, out object value)
        {
            value = null!;
            if (!fields.TryGetValue(normalizedName, out TableFilterColumn? column)) return false;
            row.Values.TryGetValue(column.Id, out string? text);
            value = ParseValue(text, column.SourceType)!;
            return true;
        }
    }

    private static object? ParseValue(string? value, string sourceType)
    {
        if (value is null || value.Length == 0) return null;
        const NumberStyles floating = NumberStyles.Float;
        CultureInfo culture = CultureInfo.InvariantCulture;
        return sourceType.TrimEnd('?') switch
        {
            "double" or "Double" or "System.Double" => double.Parse(value, floating, culture),
            "float" or "Single" or "System.Single" => float.Parse(value, floating, culture),
            "decimal" or "Decimal" or "System.Decimal" => decimal.Parse(value, floating, culture),
            "int" or "Int32" or "System.Int32" => int.Parse(value, culture),
            "long" or "Int64" or "System.Int64" => long.Parse(value, culture),
            "short" or "Int16" or "System.Int16" => short.Parse(value, culture),
            "byte" or "Byte" or "System.Byte" => byte.Parse(value, culture),
            "uint" or "UInt32" or "System.UInt32" => uint.Parse(value, culture),
            "ulong" or "UInt64" or "System.UInt64" => ulong.Parse(value, culture),
            "ushort" or "UInt16" or "System.UInt16" => ushort.Parse(value, culture),
            "sbyte" or "SByte" or "System.SByte" => sbyte.Parse(value, culture),
            "bool" or "Boolean" or "System.Boolean" => bool.Parse(value),
            "DateTime" or "System.DateTime" => DateTime.Parse(value, culture, DateTimeStyles.RoundtripKind),
            "TimeSpan" or "System.TimeSpan" => TimeSpan.Parse(value, culture),
            _ => value
        };
    }
}
