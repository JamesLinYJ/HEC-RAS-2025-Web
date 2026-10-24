using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.HighPerformance;

namespace Utility.Extensions;

public static class BinaryReaderWriterExtensions
{
	public static void WriteBuffer<T>(this BinaryWriter bw, Memory<T> mem) where T : unmanaged
	{
		if (mem.IsEmpty)
		{
			bw.Write(0);
			return;
		}
		bw.Write(mem.Length);
		ReadOnlySpan<byte> buffer = MemoryMarshal.AsMemory(ReadOnlyMemoryExtensions.Cast<T, byte>(mem)).Span;
		bw.Write(buffer);
	}

	public static T[] ReadBuffer<T>(this BinaryReader br) where T : unmanaged
	{
		if (typeof(T) == typeof(bool))
		{
			return (T[])(object)br.ReadArrayBool();
		}
		int readInt32 = br.ReadInt32();
		if (readInt32 == 0)
		{
			return new T[0];
		}
		T[] values = new T[readInt32];
		Span<byte> cast = MemoryMarshal.AsMemory(ReadOnlyMemoryExtensions.Cast<T, byte>(values)).Span;
		int read;
		for (int i = 0; i < cast.Length; i += read)
		{
			read = br.Read(cast.Slice(i));
			if (read == 0)
			{
				throw new EndOfStreamException($"Expected {cast.Length} bytes but stream ended after {i}.");
			}
		}
		return values;
	}

	public static void WriteJagged<T>(this BinaryWriter bw, T[][] data) where T : unmanaged
	{
		int count = data.Length;
		bw.Write(count);
		for (int i = 0; i < count; i++)
		{
			bw.WriteBuffer(data[i].AsMemory());
		}
	}

	public static T[][] ReadJagged<T>(this BinaryReader br) where T : unmanaged
	{
		int readInt32 = br.ReadInt32();
		if (readInt32 == 0)
		{
			return new T[0][];
		}
		T[][] rows = new T[readInt32][];
		for (int i = 0; i < readInt32; i++)
		{
			rows[i] = br.ReadBuffer<T>();
		}
		return rows;
	}

	public static void WriteArray<T>(this BinaryWriter bw, T[,] arr) where T : unmanaged
	{
		if (arr == null || arr.Length == 0)
		{
			bw.Write(0);
			bw.Write(0);
			bw.Flush();
			return;
		}
		if (typeof(T) == typeof(bool))
		{
			throw new NotImplementedException();
		}
		int length = arr.GetLength(0);
		int length2 = arr.GetLength(1);
		bw.Write(length);
		bw.Write(length2);
		int sizeOf = Marshal.SizeOf<T>(default);
		byte[] byteValues = new byte[length * length2 * sizeOf];
		Buffer.BlockCopy(arr, 0, byteValues, 0, byteValues.Length);
		bw.Write(byteValues);
		bw.Flush();
	}

	public static void ReadBuffer<T>(this BinaryReader br, out T[,] arr) where T : unmanaged
	{
		if (typeof(T) == typeof(bool))
		{
			throw new NotImplementedException();
		}
		int readInt32 = br.ReadInt32();
		int readInt322 = br.ReadInt32();
		arr = new T[readInt32, readInt322];
		if (readInt32 != 0 && readInt322 != 0)
		{
			int sizeOf = Marshal.SizeOf<T>(default);
			byte[] bytes = br.ReadBytes(readInt32 * readInt322 * sizeOf);
			Buffer.BlockCopy(bytes, 0, arr, 0, bytes.Length);
		}
	}

	public static void WriteArrayBool(this BinaryWriter bw, bool[] arr)
	{
		if (arr == null)
		{
			bw.Write(0);
			bw.Flush();
			return;
		}
		int arrLength = arr.Length;
		byte[] byteValues = new byte[arrLength];
		for (int i = 0; i < arrLength; i++)
		{
			if (arr[i])
			{
				byteValues[i] = 1;
			}
		}
		bw.WriteBuffer<byte>(byteValues);
	}

	public static bool[] ReadArrayBool(this BinaryReader br)
	{
		byte[] readBuffer = br.ReadBuffer<byte>();
		if (readBuffer == null)
		{
			return new bool[0];
		}
		bool[] boolValues = new bool[readBuffer.Length];
		for (int i = 0; i < boolValues.Length; i++)
		{
			if (readBuffer[i] > 0)
			{
				boolValues[i] = true;
			}
			else
			{
				boolValues[i] = false;
			}
		}
		return boolValues;
	}

	public static void WriteArrayString(this BinaryWriter bw, string[] arr)
	{
		if (arr == null)
		{
			bw.Write(0);
			bw.Flush();
			return;
		}
		int arrLength = arr.Length;
		bw.Write(arrLength);
		for (int i = 0; i < arrLength; i++)
		{
			bw.WriteStringA(arr[i]);
		}
	}

	public static string[] ReadArrayString(this BinaryReader br)
	{
		int readInt32 = br.ReadInt32();
		if (readInt32 == 0)
		{
			return new string[0];
		}
		string[] strings = new string[readInt32];
		for (int i = 0; i < readInt32; i++)
		{
			strings[i] = br.ReadStringA();
		}
		return strings;
	}

	public static void WriteStringA(this BinaryWriter bw, string s)
	{
		ASCIIEncoding aSCIIEncoding = new ASCIIEncoding();
		byte[] bytes = ((s == null) ? Array.Empty<byte>() : aSCIIEncoding.GetBytes(s));
		bw.WriteBuffer<byte>(bytes);
	}

	public static string ReadStringA(this BinaryReader br)
	{
		byte[] bytes = br.ReadBuffer<byte>();
		return new ASCIIEncoding().GetString(bytes);
	}

	public static async Task<string> ReadStringAsync(this BinaryReader br)
	{
		byte[] lengthBuffer = new byte[sizeof(int)];
		await br.BaseStream.ReadExactlyAsync(lengthBuffer).ConfigureAwait(false);
		int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
		if (length < 0)
		{
			throw new InvalidDataException("A string length cannot be negative.");
		}
		byte[] buffer = new byte[length];
		await br.BaseStream.ReadExactlyAsync(buffer).ConfigureAwait(false);
		return Encoding.ASCII.GetString(buffer);
	}

	public static void Write(this BinaryWriter bw, DateTime dt)
	{
		bw.WriteStringA(dt.ToString());
	}

	public static DateTime ReadDate(this BinaryReader br)
	{
		return DateTime.Parse(br.ReadStringA());
	}

	public static void WriteNullable(this BinaryWriter bw, bool? nullable)
	{
		bw.Write(nullable.HasValue);
		if (nullable.HasValue)
		{
			bw.Write(nullable.Value);
		}
		else
		{
			bw.Write(value: false);
		}
	}

	public static void WriteNullable(this BinaryWriter bw, float? nullable)
	{
		bw.Write(nullable.HasValue);
		if (nullable.HasValue)
		{
			bw.Write(nullable.Value);
		}
		else
		{
			bw.Write(0f);
		}
	}

	public static void ReadNullable(this BinaryReader br, out bool? readBoolean22)
	{
		bool readBoolean = br.ReadBoolean();
		bool readBoolean2 = br.ReadBoolean();
		if (!readBoolean)
		{
			readBoolean22 = null;
		}
		else
		{
			readBoolean22 = readBoolean2;
		}
	}

	public static void ReadNullable(this BinaryReader br, out float? readSingle2)
	{
		bool readBoolean = br.ReadBoolean();
		float readSingle = br.ReadSingle();
		if (!readBoolean)
		{
			readSingle2 = null;
		}
		else
		{
			readSingle2 = readSingle;
		}
	}
}
