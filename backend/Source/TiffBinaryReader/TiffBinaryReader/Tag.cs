using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace TiffBinaryReader;

[DebuggerDisplay("{TagType.Name}")]
public class Tag
{
	public TagType TagType;

	public DataType ActualDataType;

	private readonly Memory<byte> _data;

	public int ValueCount => _data.Length / SizeOfType;

	private int SizeOfType => ActualDataType.ByteCount;

	public static Tag CreateTag<T>(TagType type, DataType actualDT, T[] values) where T : unmanaged
	{
		throw new NotImplementedException();
	}

	public static Tag CreateTag<T>(TagType type, DataType actualDT, T localValue) where T : unmanaged
	{
		if (actualDT.Type != typeof(T))
		{
			throw new ArgumentException("Type mismatch");
		}
		byte[] byteValues = new byte[Unsafe.SizeOf<T>()];
		MemoryMarshal.Write(byteValues, in localValue);
		return new Tag(type, actualDT, byteValues);
	}

	public static Tag CreateTag(TagType type, DataType actualDT, string stringValue)
	{
		if (actualDT.Type != typeof(byte))
		{
			throw new ArgumentException("Type mismatch");
		}
		byte[] bytes = Encoding.ASCII.GetBytes(stringValue);
		return new Tag(type, actualDT, bytes);
	}

	public Tag(TagType type, DataType actualDT, Memory<byte> data)
	{
		if (data.Length % actualDT.ByteCount != 0)
		{
			throw new ArgumentException();
		}
		TagType = type;
		ActualDataType = actualDT;
		_data = data;
	}

	public T ValueAs<T>(int index = 0) where T : unmanaged
	{
		if (ActualDataType.Type == typeof(T))
		{
			return MemoryMarshal.Cast<byte, T>(_data.Span)[index];
		}
		int start = index * SizeOfType;
		return ValueConverter.To<T>(ActualDataType.BitConvert(_data.Slice(start).Span));
	}

	public string ValueAsString()
	{
		Span<byte> byteSpan = _data.Span;
		if (byteSpan[byteSpan.Length - 1] == 0)
		{
			return Encoding.ASCII.GetString(_data.Span.Slice(0, _data.Length - 1));
		}
		return Encoding.ASCII.GetString(_data.Span);
	}

	public Memory<byte> GetBytes()
	{
		return _data;
	}

	public Span<T> ValuesAs<T>() where T : unmanaged
	{
		if (ActualDataType.Type == typeof(T))
		{
			return MemoryMarshal.Cast<byte, T>(_data.Span);
		}
		T[] values = new T[_data.Length / SizeOfType];
		for (int i = 0; i < values.Length; i++)
		{
			values[i] = ValueAs<T>(i);
		}
		return values;
	}

	public bool IsNativeAs<T>() where T : unmanaged
	{
		return ActualDataType.Type == typeof(T);
	}

	public void TryGetAs(long[] buffer)
	{
		if (IsNativeAs<long>())
		{
			Span<long> longs = ValuesAs<long>();
			for (int i = 0; i < ValueCount; i++)
			{
				buffer[i] = longs[i];
			}
		}
		else if (IsNativeAs<ulong>())
		{
			Span<ulong> ulongs = ValuesAs<ulong>();
			for (int j = 0; j < ValueCount; j++)
			{
				buffer[j] = (long)ulongs[j];
			}
		}
		else if (IsNativeAs<uint>())
		{
			Span<uint> uints = ValuesAs<uint>();
			for (int k = 0; k < ValueCount; k++)
			{
				buffer[k] = uints[k];
			}
		}
		else if (IsNativeAs<int>())
		{
			Span<int> intSpan = ValuesAs<int>();
			for (int l = 0; l < ValueCount; l++)
			{
				buffer[l] = intSpan[l];
			}
		}
		else
		{
			ValuesAs<long>().CopyTo(buffer);
		}
	}
}
