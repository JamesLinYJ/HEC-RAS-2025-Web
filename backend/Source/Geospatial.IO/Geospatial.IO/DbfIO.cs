using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Extensions;
using Utility.Logging;
using Utility.Memory;

namespace Geospatial.IO;

public static class DbfIO
{
	[DebuggerDisplay("{Name}")]
	private struct DBFField
	{
		public static class FieldTypes
		{
			public const string Binary = "B";

			public const string Character = "C";

			public const string Date = "D";

			public const string Numeric = "N";

			public const string Logical = "L";

			public const string Memo = "M";

			public const string Timestamp = "@";

			public const string Long = "I";

			public const string AutoIncrement = "+";

			public const string Float = "F";

			public const string Double = "D";

			public const string OLE = "G";
		}

		private const byte SPACE = 32;

		private const byte DASH = 45;

		private const byte DOT = 46;

		private const byte ASCZERO = 48;

		public const int MaxFieldNameSizeV7 = 32;

		public const int MaxFieldNameSizeV4 = 10;

		private static readonly double[] Base10;

		private static readonly long[] Base10LNG;

		private static readonly double[] SubBase10;

		public string Name;

		public byte TypeID;

		public Type Type;

		public int Length;

		public int NumDecimal;

		public bool Skip;

		static DBFField()
		{
			Base10 = new double[30]
			{
				1.0, 10.0, 100.0, 1000.0, 10000.0, 100000.0, 1000000.0, 10000000.0, 100000000.0, 1000000000.0,
				10000000000.0, 100000000000.0, 1000000000000.0, 10000000000000.0, 100000000000000.0, 1000000000000000.0, 10000000000000000.0, 1E+17, 1E+18, 1E+19,
				1E+20, 1E+21, 1E+22, 1E+23, 1E+24, 1E+25, 1E+26, 1E+27, 1E+28, 1E+29
			};
			SubBase10 = new double[30]
			{
				1.0, 0.1, 0.01, 0.001, 0.0001, 1E-05, 1E-06, 1E-07, 1E-08, 1E-09,
				1E-10, 1E-11, 1E-12, 1E-13, 1E-14, 1E-15, 1E-16, 1E-17, 1E-18, 1E-19,
				1E-20, 1E-21, 1E-22, 1E-23, 1E-24, 1E-25, 1E-26, 1E-27, 1E-28, 1E-29
			};
			Base10LNG = new long[30];
			for (int i = 0; i < Base10LNG.Length; i++)
			{
				Base10LNG[i] = IntPow(10L, i);
			}
		}

		private static long IntPow(long x, int pow)
		{
			long unity = 1L;
			for (long ordinal = 0L; ordinal < pow; ordinal++)
			{
				unity *= x;
			}
			return unity;
		}

		public double ParseNumeric(byte[] buf, int pos)
		{
			int count = Length - NumDecimal - 1;
			int numDecimalTerm = Length - NumDecimal - 2;
			if (buf[pos + count] != 46)
			{
				return FallbackParse(buf, pos);
			}
			double sign = 1.0;
			double computed = 0.0;
			for (int i = 0; i < count; i++)
			{
				int buf2 = buf[pos + i];
				switch (buf2)
				{
				case 45:
					sign = -1.0;
					break;
				case 48:
				case 49:
				case 50:
				case 51:
				case 52:
				case 53:
				case 54:
				case 55:
				case 56:
				case 57:
				{
					int buf2Offset = buf2 - 48;
					computed += (double)buf2Offset * Base10[numDecimalTerm - i];
					break;
				}
				default:
					return FallbackParse(buf, pos);
				case 32:
					break;
				}
			}
			for (int j = count + 1; j < Length; j++)
			{
				int buf3 = buf[pos + j];
				if (buf3 >= 48 && buf3 <= 57)
				{
					int buf3Offset = buf3 - 48;
					computed += (double)buf3Offset * SubBase10[j - count];
					continue;
				}
				return FallbackParse(buf, pos);
			}
			return computed * sign;
		}

		private double FallbackParse(byte[] buffer, int pos)
		{
			if (double.TryParse(Encoding.ASCII.GetString(buffer.AsSpan().Slice(pos, Length)), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
			{
				return result;
			}
			return double.NaN;
		}

		public long ParseInteger(Span<byte> buf)
		{
			int lengthOffset = buf.Length - 1;
			long ordinal = 0L;
			int buf2Offset;
			for (int lengthOffset2 = buf.Length - 1; lengthOffset2 >= 0; ordinal += buf2Offset * Base10LNG[lengthOffset - lengthOffset2], lengthOffset2--)
			{
				int buf2 = buf[lengthOffset2];
				switch (buf2)
				{
				case 45:
					ordinal *= -1;
					break;
				default:
					buf2Offset = buf2 - 48;
					if (buf2Offset <= 9 && buf2Offset >= 0)
					{
						continue;
					}
					_logger.LogWarning("Possible DBF parse error in ParseInteger. Returning -1.");
					return -1L;
				case 32:
					break;
				}
				break;
			}
			return ordinal;
		}
	}

	public static class MessageCodes
	{
		public static readonly MessageClass ErrorParsingDBFHeader = new MessageClass("ShpWriter_ErrParsingDbfHeader", MessageSeverity.Error, "Error parsing DBF header for \"{Filename}\".");

		public static readonly MessageClass ErrorWritingDBFFile = new MessageClass("DbfWriter_ErrWritingDbfFile", MessageSeverity.Error, "Error writing DBF file. \"{ExceptionMsg}\".");

		public static readonly MessageClass FieldNameTooLong = new MessageClass("DbfWriter_FieldNameTooLong", MessageSeverity.Error, "DBF field name \"{FieldName}\" exceeds the maximum length of {MaxLength} characters and AutoTrimFieldNames is disabled.");

		public static readonly MessageClass FieldNameTruncated = new MessageClass("DbfWriter_FieldNameTruncated", MessageSeverity.Warning, "DBF field name \"{IntendedName}\" exceeded the maximum length of {MaxLength} characters and was renamed to \"{ModifiedName}\".");

		public static readonly MessageClass StringValueTruncated = new MessageClass("DbfWriter_StringValueTruncated", MessageSeverity.Warning, "String values in column \"{FieldName}\" exceeded the DBF maximum length of {MaxLength} characters (longest was {OriginalLength}) and were truncated.");

		public static readonly MessageClass UnsupportedColumnType = new MessageClass("DbfWriter_UnsupportedColumnType", MessageSeverity.Error, "Column \"{ColumnName}\" has unsupported type \"{ColumnType}\" for DBF output.");

		public static Message GetErrorParsingDBFHeader(string filename)
		{
			return ErrorParsingDBFHeader.Create(filename);
		}

		public static Message GetErrorWritingDBFFile(string exceptionMsg)
		{
			return ErrorWritingDBFFile.Create(exceptionMsg);
		}

		public static Message GetFieldNameTooLong(string fieldName, int maxLength)
		{
			return FieldNameTooLong.Create(fieldName, maxLength);
		}

		public static Message GetFieldNameTruncated(string intendedName, int maxLength, string modifiedName)
		{
			return FieldNameTruncated.Create(intendedName, maxLength, modifiedName);
		}

		public static Message GetStringValueTruncated(string fieldName, int originalLength, int maxLength)
		{
			return StringValueTruncated.Create(fieldName, originalLength, maxLength);
		}

		public static Message GetUnsupportedColumnType(string columnName, string columnType)
		{
			return UnsupportedColumnType.Create(columnName, columnType);
		}
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("ShapefileIO");

	private const int DbfHeaderLength = 32;

	private const int DbfFieldDescriptorLength = 32;

	public const int FieldNameMaxLength = 10;

	public const int ShapefileDbfStringMaxLength = 254;

	private const byte HeaderTerminationByte = 13;

	public static OperationResult TryRead(string dbfFilename, out Table attrTable)
	{
		try
		{
			using FileStream fileStream = new FileStream(dbfFilename, FileMode.Open, FileAccess.Read);
			if (fileStream.Length < 32)
			{
				attrTable = null;
				return OperationResult.Fail(FileMessageCodes.GetFileTooSmall(dbfFilename, 32));
			}
			if (!TryReadHeader(fileStream, out var fields, out var recordCount, out var headerLength))
			{
				attrTable = null;
				return OperationResult.Fail(MessageCodes.GetErrorParsingDBFHeader(dbfFilename));
			}
			attrTable = BuildTable(fields);
			ReadAttributes(dbfFilename, fields, attrTable, recordCount, headerLength);
			return true;
		}
		catch
		{
			attrTable = null;
			return OperationResult.Fail(MessageCodes.GetErrorParsingDBFHeader(dbfFilename));
		}
	}

	private static bool TryReadHeader(FileStream fs, out DBFField[] fields, out int recordCount, out int headerLength)
	{
		headerLength = 0;
		if (fs.Length < 32)
		{
			fields = null;
			recordCount = 0;
			return false;
		}
		Span<byte> buffer = stackalloc byte[32];
		if (fs.Read(buffer) != 32)
		{
			fields = null;
			recordCount = -1;
			return false;
		}
		recordCount = IntLE(buffer.Slice(4));
		int headerLength2 = ShortLE(buffer.Slice(8));
		ShortLE(buffer.Slice(10));
		headerLength = headerLength2;
		int count = (headerLength2 - 32 - 1) / 32;
		if (count < 0)
		{
			_logger.LogWarning("DBF declares an impossible header length of {HeaderLength} bytes.", headerLength2);
			fields = null;
			return false;
		}
		_ = ref buffer[28];
		_ = ref buffer[29];
		byte[] byteValues = new byte[count * 32];
		if (fs.Length < fs.Position + byteValues.Length)
		{
			fields = null;
			return false;
		}
		if (fs.Read(byteValues, 0, byteValues.Length) != byteValues.Length)
		{
			fields = null;
			return false;
		}
		int count2 = count;
		for (int i = 0; i < count; i++)
		{
			if (byteValues[i * 32] == 13)
			{
				count2 = i;
				break;
			}
		}
		fields = new DBFField[count2];
		int intervalIndex = 0;
		for (int j = 0; j < count2; j++)
		{
			DBFField dBFField = new DBFField
			{
				Name = Encoding.UTF8.GetString(byteValues, intervalIndex, 11).Trim().Trim('\0'),
				TypeID = byteValues[intervalIndex + 11],
				Length = byteValues[intervalIndex + 16],
				NumDecimal = byteValues[intervalIndex + 17]
			};
			switch ((char)dBFField.TypeID)
			{
			case 'I':
				dBFField.Type = typeof(int);
				break;
			case 'N':
				if (dBFField.NumDecimal > 0)
				{
					dBFField.Type = typeof(double);
				}
				else
				{
					dBFField.Type = typeof(int);
				}
				break;
			case 'F':
				dBFField.Type = typeof(double);
				break;
			case 'L':
				dBFField.Type = typeof(bool);
				break;
			case 'C':
				dBFField.Type = typeof(string);
				break;
			default:
				dBFField.Type = typeof(string);
				break;
			}
			fields[j] = dBFField;
			intervalIndex += 32;
		}
		return true;
	}

	private static Table BuildTable(DBFField[] fields)
	{
		Table table = new Table();
		for (int i = 0; i < fields.Length; i++)
		{
			DBFField dBFField = fields[i];
			table.AddColumn(dBFField.Name, dBFField.Type);
		}
		return table;
	}

	private static void ReadAttributes(string dbfFilename, DBFField[] fields, Table dt, int rowCount, int headerLength)
	{
		byte[] dbfBytes = File.ReadAllBytes(dbfFilename);
		int fieldLength = fields.Length;
		int startPos = headerLength;
		int sum = fields.Sum((DBFField a) => a.Length);
		int perRecordLength = sum + 1;
		if (startPos + (long)rowCount * (long)perRecordLength > dbfBytes.Length)
		{
			int rowCount2 = Math.Max(0, (dbfBytes.Length - startPos) / perRecordLength);
			_logger.LogWarning("DBF \"{Filename}\" declares {DeclaredRows} records of {RecordLength} bytes but only holds {ActualRows}.", dbfFilename, rowCount, perRecordLength, rowCount2);
			rowCount = rowCount2;
		}
		int[] colIndex = new int[fieldLength];
		int c;
		for (c = 0; c < fieldLength; c++)
		{
			colIndex[c] = dt.Columns.FirstIndexOf((TableColumn col) => col.Name == fields[c].Name);
			if (colIndex[c] > -1 && dt.Columns[colIndex[c]].IsReadOnly)
			{
				colIndex[c] = -1;
			}
		}
		dt.PresizeNull(rowCount);
		Parallel.SmartFor(rowCount, (int start, int fin) =>
		{
			int dBFFieldLengthSum = startPos + start * perRecordLength;
			Span<byte> dbfBytesSpan = dbfBytes.AsSpan();
			for (int i = start; i <= fin; i++)
			{
				dBFFieldLengthSum++;
				object[] objectValues = new object[fields.Length];
				for (int j = 0; j < fields.Length; j++)
				{
					DBFField dBFField = fields[j];
					int colIndex2 = colIndex[j];
					if (colIndex2 == -1)
					{
						dBFFieldLengthSum += dBFField.Length;
					}
					else
					{
						Span<byte> rawValue = dbfBytesSpan.Slice(dBFFieldLengthSum, dBFField.Length);
						if ((dBFField.Type == typeof(double) || dBFField.Type == typeof(float) || dBFField.Type == typeof(int)) && rawValue.IndexOfAnyExcept((byte)32, (byte)0) < 0)
						{
							objectValues[colIndex2] = null;
						}
						else if (dBFField.Type == typeof(bool))
						{
							objectValues[colIndex2] = rawValue.Length == 0 ? null : rawValue[0] switch
							{
								(byte)'T' or (byte)'t' or (byte)'Y' or (byte)'y' => true,
								(byte)'F' or (byte)'f' or (byte)'N' or (byte)'n' => false,
								(byte)'?' or (byte)' ' or 0 => null,
								_ => throw new InvalidDataException("Invalid DBF logical value.")
							};
						}
						else if (dBFField.Type == typeof(double))
						{
							objectValues[colIndex2] = dBFField.ParseNumeric(dbfBytes, dBFFieldLengthSum);
						}
						else if (dBFField.Type == typeof(float))
						{
							objectValues[colIndex2] = (float)dBFField.ParseNumeric(dbfBytes, dBFFieldLengthSum);
						}
						else if (dBFField.Type == typeof(int))
						{
							objectValues[colIndex2] = (int)dBFField.ParseInteger(dbfBytesSpan.Slice(dBFFieldLengthSum, dBFField.Length));
						}
						else
						{
							Span<byte> slice = dbfBytesSpan.Slice(dBFFieldLengthSum, dBFField.Length);
							int firstNonSpaceCharacter = FirstNonSpaceCharacter(slice);
							if (firstNonSpaceCharacter == 0)
							{
								objectValues[colIndex2] = Encoding.UTF8.GetString(slice);
							}
							else if (firstNonSpaceCharacter == slice.Length)
							{
								objectValues[colIndex2] = string.Empty;
							}
							else
							{
								objectValues[colIndex2] = Encoding.UTF8.GetString(slice.Slice(firstNonSpaceCharacter));
							}
						}
						dBFFieldLengthSum += dBFField.Length;
					}
				}
				dt.SetRow(i, objectValues);
			}
		});
	}

	public static OperationResult TryWrite(string dbfFilename, Table attrTable, DbfWriteSettings settings = null)
	{
		OperationResult operationResult = OperationResult.Success();
		try
		{
			if (settings == null)
			{
				settings = DbfWriteSettings.Default();
			}
			OperationResult operationResult2 = TryBuildDbfFields(attrTable, settings, out var fields);
			operationResult.MergeIn(operationResult2);
			if (!operationResult2.Result)
			{
				return operationResult;
			}
			if (File.Exists(dbfFilename))
			{
				if (!settings.OverwriteFileIfExists)
				{
					operationResult.AddFailure(FileMessageCodes.GetFileAlreadyExists(dbfFilename));
					return operationResult;
				}
				File.Delete(dbfFilename);
			}
			using (FileStream fileStream = new FileStream(dbfFilename, FileMode.CreateNew))
			{
				Span<byte> byteSpan = stackalloc byte[32];
				int ordinal = 0;
				DBFField[] fieldsArray = fields;
				for (int i = 0; i < fieldsArray.Length; i++)
				{
					if (!fieldsArray[i].Skip)
					{
						ordinal++;
					}
				}
				int ordinal2 = 33 + ordinal * 32;
				int sumFieldsTerm = fields.Sum((DBFField f) => f.Length) + 1;
				byteSpan.Fill(0);
				WriteHeader(byteSpan, attrTable.Rows.Count, (short)ordinal2, (short)sumFieldsTerm);
				fileStream.Write(byteSpan);
				for (int intervalIndex = 0; intervalIndex < attrTable.Columns.Count; intervalIndex++)
				{
					DBFField dBFField = fields[intervalIndex];
					if (dBFField.Skip)
					{
						continue;
					}
					byteSpan.Fill(0);
                    Encoding.UTF8.GetBytes(dBFField.Name, byteSpan.Slice(0, 11));
					byteSpan[11] = dBFField.TypeID;
					byteSpan[16] = (byte)dBFField.Length;
					byteSpan[17] = (byte)dBFField.NumDecimal;
					fileStream.Write(byteSpan);
				}
				fileStream.WriteByte(13);
				Span<byte> byteSpan2 = stackalloc byte[sumFieldsTerm];
				for (int intervalIndex3 = 0; intervalIndex3 < attrTable.Rows.Count; intervalIndex3++)
				{
					byteSpan2[0] = 32;
					Span<byte> slice = byteSpan2.Slice(1);
					int dBFField2LengthSum = 0;
					object[] itemArray = attrTable.Rows[intervalIndex3].ItemArray;
					for (int intervalIndex4 = 0; intervalIndex4 < attrTable.Columns.Count; intervalIndex4++)
					{
						TableColumn tableColumn = attrTable.Columns[intervalIndex4];
						DBFField dBFField2 = fields[intervalIndex4];
						if (dBFField2.Skip)
						{
							continue;
						}
						string itemArrayTerm;
						if (itemArray[intervalIndex4] == null || itemArray[intervalIndex4] is DBNull)
						{
							itemArrayTerm = tableColumn.Type == typeof(bool) ? "?" : "";
						}
						else if (tableColumn.Type == typeof(byte))
						{
							itemArrayTerm = (itemArray[intervalIndex4] as byte?).GetValueOrDefault().ToString(CultureInfo.InvariantCulture);
						}
						else if (tableColumn.Type == typeof(short))
						{
							itemArrayTerm = (itemArray[intervalIndex4] as short?).GetValueOrDefault().ToString(CultureInfo.InvariantCulture);
						}
						else if (tableColumn.Type == typeof(int))
						{
							itemArrayTerm = (itemArray[intervalIndex4] as int?).GetValueOrDefault().ToString(CultureInfo.InvariantCulture);
						}
						else if (tableColumn.Type == typeof(long))
						{
							itemArrayTerm = (itemArray[intervalIndex4] as long?).GetValueOrDefault().ToString(CultureInfo.InvariantCulture);
						}
						else if (tableColumn.Type == typeof(float))
						{
							float? itemArray2 = itemArray[intervalIndex4] as float?;
							if (!itemArray2.HasValue || float.IsNaN(itemArray2.Value))
							{
								itemArrayTerm = "";
							}
							else
							{
								itemArrayTerm = itemArray2.Value.ToString("F" + dBFField2.NumDecimal, CultureInfo.InvariantCulture);
								itemArrayTerm = TrimFloatString(itemArrayTerm, dBFField2.Length);
							}
						}
						else if (tableColumn.Type == typeof(double))
						{
							double? itemArray3 = itemArray[intervalIndex4] as double?;
							if (!itemArray3.HasValue || double.IsNaN(itemArray3.Value))
							{
								itemArrayTerm = "";
							}
							else
							{
								itemArrayTerm = itemArray3.Value.ToString("F" + dBFField2.NumDecimal, CultureInfo.InvariantCulture);
								itemArrayTerm = TrimFloatString(itemArrayTerm, dBFField2.Length);
							}
						}
						else if (tableColumn.Type == typeof(bool))
						{
							itemArrayTerm = ((itemArray[intervalIndex4] as bool? != true) ? "F" : "T");
						}
						else if (tableColumn.Type == typeof(DateTime))
						{
							DateTime dateTime = (itemArray[intervalIndex4] as DateTime?) ?? DateTime.MinValue;
							itemArrayTerm = dateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
						}
						else if (tableColumn.Type == typeof(char))
						{
							itemArrayTerm = ((itemArray[intervalIndex4] as char?) ?? ' ').ToString();
						}
						else
						{
							if (!(tableColumn.Type == typeof(string)))
							{
								throw new NotImplementedException("Column header not supported: " + tableColumn.Type);
							}
							itemArrayTerm = (itemArray[intervalIndex4] as string) ?? "";
							itemArrayTerm = Utf8Prefix(itemArrayTerm, dBFField2.Length);
						}
						PrintRightJustify(slice.Slice(dBFField2LengthSum, dBFField2.Length), itemArrayTerm);
						dBFField2LengthSum += dBFField2.Length;
					}
					fileStream.Write(byteSpan2);
				}
			}
			return operationResult;
		}
		catch (Exception ex)
		{
			operationResult.AddFailure(MessageCodes.GetErrorWritingDBFFile(ex.Message));
			return operationResult;
		}
	}

	private static string TrimFloatString(string sv, int maxLen)
	{
		if (sv.Length > maxLen)
		{
			int indexOf = sv.IndexOf('.');
			sv = ((indexOf != -1 && indexOf <= maxLen) ? sv.Substring(0, maxLen) : new string('*', maxLen));
		}
		return sv;
	}

	public static OperationResult TryWriteEmpty(string dbfFilename, int recordCount)
	{
		try
		{
			if (File.Exists(dbfFilename))
			{
				return OperationResult.Fail(FileMessageCodes.GetFileAlreadyExists(dbfFilename));
			}
			using (FileStream fileStream = new FileStream(dbfFilename, FileMode.CreateNew))
			{
				Span<byte> byteSpan = stackalloc byte[32];
				int ordinal = 33;
				int unity = 1;
				byteSpan.Fill(0);
				WriteHeader(byteSpan, recordCount, (short)ordinal, (short)unity);
				fileStream.Write(byteSpan);
				fileStream.WriteByte(13);
				byte[] byteValues = new byte[recordCount];
				byteValues.Fill((byte)32);
				fileStream.Write(byteValues);
			}
			return OperationResult.Success();
		}
		catch (Exception ex)
		{
			return OperationResult.Fail("Writing DBF file.", ex);
		}
	}

	private static void PrintRightJustify(Span<byte> slice, long ordinal)
	{
		string textValue = ordinal.ToString();
		if (textValue.Length > slice.Length)
		{
			throw new Exception("Not enough space to print value.");
		}
		PrintRightJustify(slice, textValue);
	}

	private static void PrintRightJustify(Span<byte> slice, double computed, int nDecimal)
	{
		string nDecimalTerm = computed.ToString("F" + nDecimal);
		if (nDecimalTerm.Length > slice.Length)
		{
			throw new Exception("Not enough space to print value.");
		}
		PrintRightJustify(slice, nDecimalTerm);
	}

	private static void PrintRightJustify(Span<byte> slice, string textValue)
	{
        int length = Encoding.UTF8.GetByteCount(textValue);
        if (length > slice.Length) throw new InvalidDataException("Not enough space to print value.");
        slice.Slice(0, slice.Length - length).Fill(32);
        Encoding.UTF8.GetBytes(textValue, slice.Slice(slice.Length - length));
	}
    private static string Utf8Prefix(string value, int maxBytes)
    {
        int chars = 0, bytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > maxBytes) break;
            bytes += rune.Utf8SequenceLength; chars += rune.Utf16SequenceLength;
        }
        return value.Substring(0, chars);
    }

	private static void WriteHeader(Span<byte> buffer, int recordCount, short headerLen, short recordLen)
	{
		if (buffer.Length < 32)
		{
			throw new ArgumentException("Buffer not large enough for DBF header.");
		}
		buffer[0] = 4;
		DateTime now = DateTime.Now;
		int yearOffset = now.Year - 1900;
		buffer[1] = (byte)yearOffset;
		buffer[2] = (byte)now.Month;
		buffer[3] = (byte)now.Day;
		WriteLE(buffer.Slice(4), recordCount);
		WriteLE(buffer.Slice(8), headerLen);
		WriteLE(buffer.Slice(10), recordLen);
	}

	private static void FixedPointLength(double minValue, double maxValue, out int fieldLen, out int numDecimals)
	{
		string minValue2 = minValue.ToString("F0");
		string maxValue2 = maxValue.ToString("F0");
		int length = minValue2.Length;
		int length2 = maxValue2.Length;
		int fieldLen2 = Math.Max(length, length2);
		if (fieldLen2 > 18)
		{
			fieldLen = fieldLen2;
			numDecimals = 0;
		}
		else
		{
			fieldLen = 19;
			numDecimals = fieldLen - fieldLen2 - 1;
		}
	}

	private static OperationResult TryBuildDbfFields(Table tbl, DbfWriteSettings settings, out DBFField[] fields)
	{
		OperationResult operationResult = OperationResult.Success();
		fields = new DBFField[tbl.Columns.Count];
		HashSet<string> ordinalIgnoreCase = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < tbl.Columns.Count; i++)
		{
			TableColumn tableColumn = tbl.Columns[i];
			int useShapefileFieldNameLimitSettingsTerm = 10; // Fixed DBF descriptors reserve 11 bytes including the terminator.
			if (Encoding.UTF8.GetByteCount(tableColumn.Name) > useShapefileFieldNameLimitSettingsTerm && !settings.AutoTrimFieldNames)
			{
				operationResult.AddFailure(MessageCodes.GetFieldNameTooLong(tableColumn.Name, useShapefileFieldNameLimitSettingsTerm));
				return operationResult;
			}
			string truncateUnique = Utf8Prefix(tableColumn.Name, useShapefileFieldNameLimitSettingsTerm);
            for (int suffix = 1; ordinalIgnoreCase.Contains(truncateUnique); suffix++)
            {
                string tail = suffix.ToString(CultureInfo.InvariantCulture);
                truncateUnique = Utf8Prefix(tableColumn.Name, useShapefileFieldNameLimitSettingsTerm - tail.Length) + tail;
            }
			if (!string.Equals(truncateUnique, tableColumn.Name, StringComparison.Ordinal))
			{
				operationResult.AddMessage(MessageCodes.GetFieldNameTruncated(tableColumn.Name, useShapefileFieldNameLimitSettingsTerm, truncateUnique));
			}
			ordinalIgnoreCase.Add(truncateUnique);
			DBFField dBFField = new DBFField
			{
				Name = truncateUnique
			};
			if (tableColumn.Type == typeof(byte))
			{
				dBFField.TypeID = 78;
				dBFField.Length = 3;
			}
			else if (tableColumn.Type == typeof(short))
			{
				dBFField.TypeID = 78;
				dBFField.Length = 5;
			}
			else if (tableColumn.Type == typeof(int))
			{
				dBFField.TypeID = 78;
				dBFField.Length = 10;
			}
			else if (tableColumn.Type == typeof(long))
			{
				dBFField.TypeID = 78;
				dBFField.Length = 20;
			}
			else if (tableColumn.Type == typeof(float) || tableColumn.Type == typeof(double))
			{
				dBFField.TypeID = 70;
				dBFField.Length = settings.FloatCharLength;
				dBFField.NumDecimal = settings.FloatNumDecimals;
			}
			else if (tableColumn.Type == typeof(bool))
			{
				dBFField.TypeID = 76;
				dBFField.Length = 1;
			}
			else if (tableColumn.Type == typeof(DateTime))
			{
				dBFField.TypeID = 68;
				dBFField.Length = 8;
			}
			else if (tableColumn.Type == typeof(char))
			{
				dBFField.TypeID = 67;
				dBFField.Length = 1;
			}
			else if (tableColumn.Type == typeof(string))
			{
				dBFField.TypeID = 67;
				int useShapefileStringLimitSettingsTerm = (settings.UseShapefileStringLimit ? 254 : 255);
				int rowsTblTerm = 0;
				for (int j = 0; j < tbl.Rows.Count; j++)
				{
					rowsTblTerm = Math.Max(Encoding.UTF8.GetByteCount((tbl.Rows[j][i] as string) ?? ""), rowsTblTerm);
				}
				dBFField.Length = Math.Max(1, Math.Min(rowsTblTerm, useShapefileStringLimitSettingsTerm));
				if (rowsTblTerm > useShapefileStringLimitSettingsTerm)
				{
					operationResult.AddMessage(MessageCodes.GetStringValueTruncated(truncateUnique, rowsTblTerm, useShapefileStringLimitSettingsTerm));
				}
			}
			else
			{
				if (!settings.IgnoreBadColumnTypes)
				{
					operationResult.AddFailure(MessageCodes.GetUnsupportedColumnType(tableColumn.Name, tableColumn.Type?.ToString() ?? "null"));
					return operationResult;
				}
				dBFField.Skip = true;
			}
			fields[i] = dBFField;
		}
		return operationResult;
	}

	private static Span<byte> TrySlice(Span<byte> b, int start, int ct)
	{
		if (start + ct <= b.Length)
		{
			return b.Slice(start, ct);
		}
		return Span<byte>.Empty;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int IntBE(ReadOnlySpan<byte> bytes)
	{
		return BinaryPrimitives.ReadInt32BigEndian(bytes);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int IntLE(ReadOnlySpan<byte> bytes)
	{
		return BinaryPrimitives.ReadInt32LittleEndian(bytes);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static short ShortLE(ReadOnlySpan<byte> bytes)
	{
		return BinaryPrimitives.ReadInt16LittleEndian(bytes);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static double DoubleLE(ReadOnlySpan<byte> bytes)
	{
		return BinaryPrimitives.ReadDoubleLittleEndian(bytes);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void WriteLE(Span<byte> bytes, int ordinal)
	{
		BinaryPrimitives.WriteInt32LittleEndian(bytes, ordinal);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void WriteLE(Span<byte> bytes, short ordinal)
	{
		BinaryPrimitives.WriteInt16LittleEndian(bytes, ordinal);
	}

	private static int FirstNonSpaceCharacter(Span<byte> buf)
	{
		for (int i = 0; i < buf.Length; i++)
		{
			if (buf[i] != 32)
			{
				return i;
			}
		}
		return buf.Length;
	}
}
