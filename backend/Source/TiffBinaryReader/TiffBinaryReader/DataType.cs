using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace TiffBinaryReader;

[DebuggerDisplay("{Description}")]
public class DataType
{
	public ushort UID { get; private set; }

	public string Description { get; private set; }

	public int ByteCount { get; private set; }

	public Type Type { get; private set; }

	public DataType(ushort uid, string desc, Type type, int byteCount)
	{
		UID = uid;
		Description = desc;
		ByteCount = byteCount;
		Type = type;
	}

	public bool IsEqual<T>() where T : unmanaged
	{
		return typeof(T) == Type;
	}

	public object BitConvert(Span<byte> tagData)
	{
		if (Type == typeof(byte))
		{
			return (byte)BitConverter.ToChar(tagData);
		}
		if (Type == typeof(ushort))
		{
			return BitConverter.ToUInt16(tagData);
		}
		if (Type == typeof(uint))
		{
			return BitConverter.ToUInt32(tagData);
		}
		if (Type == typeof(sbyte))
		{
			return (sbyte)BitConverter.ToChar(tagData);
		}
		if (Type == typeof(short))
		{
			return BitConverter.ToInt16(tagData);
		}
		if (Type == typeof(int))
		{
			return BitConverter.ToInt32(tagData);
		}
		if (Type == typeof(float))
		{
			return BitConverter.ToSingle(tagData);
		}
		if (Type == typeof(double))
		{
			return BitConverter.ToDouble(tagData);
		}
		if (Type == typeof(long))
		{
			return BitConverter.ToInt64(tagData);
		}
		if (Type == typeof(ulong))
		{
			return BitConverter.ToUInt64(tagData);
		}
		return "";
	}

	private byte[] GetBytesInternal(object[] values)
	{
		List<byte> byteList = new List<byte>();
		if (Type == typeof(byte))
		{
			for (int i = 0; i < values.Length; i++)
			{
				byteList.Add((byte)values[i]);
			}
		}
		else if (Type == typeof(ushort))
		{
			for (int j = 0; j < values.Length; j++)
			{
				byteList.AddRange(BitConverter.GetBytes((ushort)values[j]));
			}
		}
		else if (Type == typeof(uint))
		{
			for (int k = 0; k < values.Length; k++)
			{
				byteList.AddRange(BitConverter.GetBytes((uint)values[k]));
			}
		}
		else if (Type == typeof(sbyte))
		{
			for (int l = 0; l < values.Length; l++)
			{
				byteList.Add((byte)values[l]);
			}
		}
		else if (Type == typeof(short))
		{
			for (int m = 0; m < values.Length; m++)
			{
				byteList.AddRange(BitConverter.GetBytes((short)values[m]));
			}
		}
		else if (Type == typeof(int))
		{
			for (int n = 0; n < values.Length; n++)
			{
				byteList.AddRange(BitConverter.GetBytes((int)values[n]));
			}
		}
		else if (Type == typeof(float))
		{
			for (int intervalIndex = 0; intervalIndex < values.Length; intervalIndex++)
			{
				byteList.AddRange(BitConverter.GetBytes((float)values[intervalIndex]));
			}
		}
		else if (Type == typeof(double))
		{
			for (int intervalIndex2 = 0; intervalIndex2 < values.Length; intervalIndex2++)
			{
				byteList.AddRange(BitConverter.GetBytes((double)values[intervalIndex2]));
			}
		}
		else if (Type == typeof(long))
		{
			for (int intervalIndex3 = 0; intervalIndex3 < values.Length; intervalIndex3++)
			{
				byteList.AddRange(BitConverter.GetBytes((long)values[intervalIndex3]));
			}
		}
		else if (Type == typeof(ulong))
		{
			for (int intervalIndex4 = 0; intervalIndex4 < values.Length; intervalIndex4++)
			{
				byteList.AddRange(BitConverter.GetBytes((ulong)values[intervalIndex4]));
			}
		}
		if (byteList.Count / ByteCount != values.Length)
		{
			throw new Exception("Data type issue in GetBytes");
		}
		return byteList.ToArray();
	}

	public object[] Parse(byte[] tagData, int offset, uint count)
	{
		if (this == DataTypes.Ascii)
		{
			return new object[1] { Encoding.ASCII.GetString(tagData, offset, (int)(count - 1)) };
		}
		Span<byte> tagDataSpan = tagData.AsSpan();
		object[] objectValues = new object[count];
		for (int i = 0; i < count; i++)
		{
			int byteCountTerm = i * ByteCount;
			objectValues[i] = BitConvert(tagDataSpan.Slice(offset + byteCountTerm));
		}
		return objectValues;
	}
}
