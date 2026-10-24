using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using Utility.Memory;

namespace Ras.Backend;

internal sealed record MapShapefileDbfField(
    int Ordinal,
    string Name,
    char SourceType,
    int Width,
    int DecimalCount,
    string ValueType);

/// <summary>
/// Rewrites only a Shapefile DBF draft while preserving its source descriptor
/// bytes and every inherited row's raw cell bytes. The retained generic DBF
/// writer infers descriptor widths and precision from values, which is not a
/// safe operation for an in-place geometry edit.
/// </summary>
internal static class MapShapefileAttributeTableDraftWriter
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private sealed record Layout(byte[] SourceBytes, byte[] Header, int HeaderLength,
        int RecordLength, int RowCount, MapShapefileDbfField[] Fields);

    internal static IReadOnlyList<MapShapefileDbfField> ReadSchema(string dbfPath) =>
        ReadLayout(dbfPath).Fields;

    internal static void Write(string sourceDbfPath, string stagedDbfPath, Table outputTable,
        IReadOnlyList<int?> sourceRowOrdinals, Table sourceTable)
    {
        ArgumentNullException.ThrowIfNull(outputTable);
        ArgumentNullException.ThrowIfNull(sourceRowOrdinals);
        ArgumentNullException.ThrowIfNull(sourceTable);
        Layout source = ReadLayout(sourceDbfPath);
        ValidateTable(source.Fields, outputTable);
        ValidateTable(source.Fields, sourceTable);
        if (sourceTable.Rows.Count != source.RowCount)
            throw new InvalidDataException("The captured DBF rows do not match the source file's declared row count.");
        if (outputTable.Rows.Count != sourceRowOrdinals.Count)
            throw new InvalidDataException("The DBF draft rows do not match the polygon or line record order.");
        if (source.Fields.Sum(field => field.Width) + 1 != source.RecordLength)
            throw new InvalidDataException("The source DBF row length does not match its ordered field descriptors.");

        int outputRecordLength = checked(1 + source.Fields.Sum(field => field.Width));
        if (outputRecordLength > ushort.MaxValue)
            throw new InvalidDataException("The edited DBF exceeds the supported row layout.");
        byte[] header = source.Header.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), outputTable.Rows.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10, 2), checked((ushort)outputRecordLength));

        string? directory = Path.GetDirectoryName(Path.GetFullPath(stagedDbfPath));
        if (directory is null) throw new InvalidDataException("The staged DBF has no parent directory.");
        Directory.CreateDirectory(directory);
        using (var output = new FileStream(stagedDbfPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            output.Write(header);
            byte[] rowBuffer = new byte[outputRecordLength];
            for (int outputRow = 0; outputRow < sourceRowOrdinals.Count; outputRow++)
            {
                int? sourceRow = sourceRowOrdinals[outputRow];
                if (sourceRow is int ordinal)
                {
                    if ((uint)ordinal >= (uint)source.RowCount)
                        throw new InvalidDataException("A DBF draft refers to a source row outside its captured table.");
                    ValidateInheritedValues(outputTable.Rows[outputRow], sourceTable.Rows[ordinal], source.Fields);
                    int sourceOffset = checked(source.HeaderLength + ordinal * source.RecordLength);
                    source.SourceBytes.AsSpan(sourceOffset, source.RecordLength).CopyTo(rowBuffer);
                }
                else
                {
                    rowBuffer.AsSpan().Fill((byte)' ');
                    rowBuffer[0] = (byte)' ';
                    int columnOffset = 1;
                    for (int column = 0; column < source.Fields.Length; column++)
                    {
                        MapShapefileDbfField field = source.Fields[column];
                        WriteNewCell(rowBuffer.AsSpan(columnOffset, field.Width),
                            outputTable.Rows[outputRow][column], field);
                        columnOffset += field.Width;
                    }
                }
                output.Write(rowBuffer);
            }
            output.WriteByte(0x1A);
            output.Flush(flushToDisk: true);
        }
        VerifyWrittenRows(source, stagedDbfPath, sourceRowOrdinals);
    }

    private static void ValidateInheritedValues(TableRow outputRow, TableRow sourceRow,
        IReadOnlyList<MapShapefileDbfField> fields)
    {
        for (int column = 0; column < fields.Count; column++)
            if (!SameValue(outputRow[column], sourceRow[column]))
                throw new InvalidDataException(
                    $"The inherited DBF row changes source attribute '{fields[column].Name}'; raw source cells cannot be rewritten as a geometry edit.");
    }

    private static void VerifyWrittenRows(Layout source, string stagedDbfPath,
        IReadOnlyList<int?> sourceRowOrdinals)
    {
        Layout written = ReadLayout(stagedDbfPath);
        if (written.RowCount != sourceRowOrdinals.Count || written.RecordLength != source.RecordLength ||
            written.HeaderLength != source.HeaderLength ||
            !written.Header.AsSpan(32).SequenceEqual(source.Header.AsSpan(32)))
            throw new InvalidDataException("The staged DBF changed the source descriptors or row layout.");

        for (int outputRow = 0; outputRow < sourceRowOrdinals.Count; outputRow++)
        {
            if (sourceRowOrdinals[outputRow] is not int sourceRow) continue;
            int sourceOffset = checked(source.HeaderLength + sourceRow * source.RecordLength);
            int outputOffset = checked(written.HeaderLength + outputRow * written.RecordLength);
            if (!source.SourceBytes.AsSpan(sourceOffset, source.RecordLength)
                    .SequenceEqual(written.SourceBytes.AsSpan(outputOffset, written.RecordLength)))
                throw new InvalidDataException("An inherited DBF record was not preserved byte-for-byte.");
        }
    }

    private static bool SameValue(object? left, object? right)
    {
        if (left is null or DBNull) return right is null or DBNull;
        if (right is null or DBNull) return false;
        return left.GetType() == right.GetType() && left.Equals(right);
    }

    private static Layout ReadLayout(string dbfPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbfPath);
        byte[] bytes = File.ReadAllBytes(dbfPath);
        if (bytes.Length < 33) throw new InvalidDataException("The source DBF header is truncated.");
        int rowCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4));
        int headerLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8, 2));
        int recordLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10, 2));
        if (rowCount < 0 || headerLength < 33 || headerLength > bytes.Length || recordLength < 1)
            throw new InvalidDataException("The source DBF header has an invalid row layout.");

        var fields = new List<MapShapefileDbfField>();
        int offset = 32;
        while (offset < headerLength)
        {
            if (bytes[offset] == 0x0D) break;
            if (offset + 32 > headerLength)
                throw new InvalidDataException("The source DBF field descriptor is truncated.");
            ReadOnlySpan<byte> descriptor = bytes.AsSpan(offset, 32);
            string name = Encoding.UTF8.GetString(descriptor[..11]).Trim().Trim('\0');
            char sourceType = (char)descriptor[11];
            int width = descriptor[16];
            int decimalCount = descriptor[17];
            if (name.Length == 0 || width <= 0 || decimalCount > width)
                throw new InvalidDataException("The source DBF contains an invalid field descriptor.");
            fields.Add(new(fields.Count, name, sourceType, width, decimalCount,
                SemanticType(sourceType, decimalCount)));
            offset += 32;
        }
        if (offset >= headerLength || bytes[offset] != 0x0D)
            throw new InvalidDataException("The source DBF has no field-descriptor terminator.");
        int expectedRecordLength = checked(1 + fields.Sum(field => field.Width));
        if (recordLength != expectedRecordLength ||
            (long)headerLength + (long)rowCount * recordLength > bytes.LongLength)
            throw new InvalidDataException("The source DBF rows do not match its declared descriptor layout.");

        return new(bytes, bytes.AsSpan(0, headerLength).ToArray(), headerLength,
            recordLength, rowCount, fields.ToArray());
    }

    private static string SemanticType(char sourceType, int decimalCount) => char.ToUpperInvariant(sourceType) switch
    {
        'I' => "integer",
        'N' when decimalCount > 0 => "number",
        'N' => "integer",
        'F' => "number",
        'L' => "boolean",
        _ => "string"
    };

    private static void ValidateTable(IReadOnlyList<MapShapefileDbfField> fields, Table table)
    {
        if (fields.Count != table.Columns.Count)
            throw new InvalidDataException("The source DBF schema and decoded table have different column counts.");
        for (int i = 0; i < fields.Count; i++)
        {
            MapShapefileDbfField field = fields[i];
            if (!string.Equals(field.Name, table.Columns[i].Name, StringComparison.Ordinal) ||
                !IsCompatibleTableType(field.SourceType, field.DecimalCount, table.Columns[i].Type))
                throw new InvalidDataException("The source DBF schema and decoded table column identities do not match.");
        }
    }

    private static bool IsCompatibleTableType(char sourceType, int decimalCount, Type tableType) =>
        char.ToUpperInvariant(sourceType) switch
        {
            'C' => tableType == typeof(string),
            'N' when decimalCount > 0 => tableType == typeof(double) || tableType == typeof(float),
            'N' or 'I' => tableType == typeof(int),
            'F' => tableType == typeof(double) || tableType == typeof(float),
            'L' => tableType == typeof(bool),
            'D' => tableType == typeof(DateTime) || tableType == typeof(string),
            _ => tableType == typeof(string)
        };

    private static void WriteNewCell(Span<byte> destination, object? value, MapShapefileDbfField field)
    {
        destination.Fill((byte)' ');
        if (value is null or DBNull)
        {
            if (char.ToUpperInvariant(field.SourceType) == 'L') destination[0] = (byte)'?';
            return;
        }

        switch (char.ToUpperInvariant(field.SourceType))
        {
            case 'L':
                if (value is not bool logical)
                    throw InvalidNewValue(field);
                destination[0] = logical ? (byte)'T' : (byte)'F';
                return;
            case 'N':
            case 'F':
            case 'I':
                WriteRightAligned(destination, FormatNumber(value, field), field);
                return;
            case 'D':
                string date = value switch
                {
                    DateTime dateTime => dateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                    string text => text,
                    _ => throw InvalidNewValue(field)
                };
                if (date.Length != 0 && (date.Length != 8 ||
                    !DateOnly.TryParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out _)))
                    throw InvalidNewValue(field);
                WriteRightAligned(destination, date, field);
                return;
            default:
                if (value is not string textValue) throw InvalidNewValue(field);
                WriteTextRightAligned(destination, textValue, field);
                return;
        }
    }

    private static string FormatNumber(object value, MapShapefileDbfField field)
    {
        if (field.DecimalCount == 0)
            return FormatInteger(value, field).ToString(CultureInfo.InvariantCulture);

        if (value is string exactText && decimal.TryParse(exactText.Trim(), NumberStyles.Number,
                CultureInfo.InvariantCulture, out decimal exactParsed))
            return FormatDecimal(exactParsed, field);
        if (value is decimal exactDecimal)
            return FormatDecimal(exactDecimal, field);
        if (IsIntegralValue(value))
            return FormatInteger(value, field).ToString(CultureInfo.InvariantCulture) +
                (field.DecimalCount == 0 ? string.Empty : "." + new string('0', field.DecimalCount));

        double numeric;
        try { numeric = Convert.ToDouble(value, CultureInfo.InvariantCulture); }
        catch (Exception error) when (error is FormatException or InvalidCastException or OverflowException)
        { throw InvalidNewValue(field); }
        if (!double.IsFinite(numeric)) throw InvalidNewValue(field);
        string formatted = numeric.ToString("F" + field.DecimalCount, CultureInfo.InvariantCulture);
        if (!double.TryParse(formatted, NumberStyles.Float, CultureInfo.InvariantCulture, out double readBack) ||
            (value is float single && (float)readBack != single) ||
            (value is not float && readBack != numeric))
            throw InvalidNewValue(field);
        return formatted;
    }

    private static BigInteger FormatInteger(object value, MapShapefileDbfField field)
    {
        if (value is BigInteger integer) return integer;
        if (value is string text && BigInteger.TryParse(text.Trim(), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out BigInteger parsed))
            return parsed;
        if (value is decimal decimalValue && decimal.Truncate(decimalValue) == decimalValue)
            return new BigInteger(decimalValue);
        if (value is double doubleValue && double.IsFinite(doubleValue) && Math.Truncate(doubleValue) == doubleValue)
            return new BigInteger(doubleValue);
        if (value is float floatValue && float.IsFinite(floatValue) && MathF.Truncate(floatValue) == floatValue)
            return new BigInteger((double)floatValue);
        if (value is byte or sbyte or short or ushort or int or uint or long or ulong)
            return new BigInteger(Convert.ToDecimal(value, CultureInfo.InvariantCulture));
        throw InvalidNewValue(field);
    }

    private static bool IsIntegralValue(object value) => value is BigInteger or byte or sbyte or short or ushort or
        int or uint or long or ulong;

    private static string FormatDecimal(decimal value, MapShapefileDbfField field)
    {
        string formatted = value.ToString("F" + field.DecimalCount, CultureInfo.InvariantCulture);
        if (!decimal.TryParse(formatted, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal readBack) ||
            readBack != value)
            throw InvalidNewValue(field);
        return formatted;
    }

    private static void WriteRightAligned(Span<byte> destination, string value, MapShapefileDbfField field)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        if (bytes.Length > destination.Length)
            throw new InvalidDataException($"The new DBF value for field '{field.Name}' exceeds its captured width.");
        bytes.CopyTo(destination[(destination.Length - bytes.Length)..]);
    }

    private static void WriteTextRightAligned(Span<byte> destination, string value, MapShapefileDbfField field)
    {
        byte[] bytes;
        try { bytes = StrictUtf8.GetBytes(value); }
        catch (EncoderFallbackException error)
        { throw new InvalidDataException($"The new DBF value for field '{field.Name}' is not valid UTF-8 text.", error); }
        if (bytes.Length > destination.Length)
            throw new InvalidDataException($"The new DBF value for field '{field.Name}' exceeds its captured width.");
        bytes.CopyTo(destination[(destination.Length - bytes.Length)..]);
    }

    private static InvalidDataException InvalidNewValue(MapShapefileDbfField field) =>
        new($"The new DBF value for field '{field.Name}' does not match its captured source type.");
}
