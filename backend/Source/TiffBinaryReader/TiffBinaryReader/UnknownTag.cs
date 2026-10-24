using System;
using System.Text;

namespace TiffBinaryReader;

public sealed class UnknownTag
{
	public ushort Id { get; }

	public ushort DataTypeId { get; }

	public long ValueCount { get; }

	public byte[] Bytes { get; }

	public DataType DataType { get; }

	public string HexId => "0x" + Id.ToString("X4");

	public string TypeName => DataType?.Description ?? $"type {DataTypeId}";

	public UnknownTag(ushort id, ushort dataTypeId, long valueCount, byte[] bytes)
	{
		Id = id;
		DataTypeId = dataTypeId;
		ValueCount = valueCount;
		Bytes = bytes ?? Array.Empty<byte>();
		DataType = DataTypes.Get(dataTypeId);
	}

	public string FormatValue()
	{
		if (Bytes.Length == 0)
		{
			if (ValueCount <= 0)
			{
				return "(empty)";
			}
			return $"({ValueCount} value(s), not read)";
		}
		if (DataType == null)
		{
			return BitConverter.ToString(Bytes);
		}
		try
		{
			if (DataType.UID == DataTypes.Ascii.UID)
			{
				return Encoding.ASCII.GetString(Bytes).TrimEnd('\0');
			}
			object[] values = DataType.Parse(Bytes, 0, (uint)ValueCount);
			return string.Join(", ", values);
		}
		catch
		{
			return BitConverter.ToString(Bytes);
		}
	}
}
