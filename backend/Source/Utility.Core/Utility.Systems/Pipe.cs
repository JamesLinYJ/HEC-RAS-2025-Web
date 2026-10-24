using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Utility.Systems;

public static class Pipe
{
	public static class PipeCommands
	{
		public const string GenerateMesh = "GenerateMesh";

		public const string StartRASMapper = "Mapper";

		public const string ReadHDF5 = "ReadHDF5";

		public const string StreamGeometry = "StreamGeometry";

		public const string StreamResult = "StreamResult";

		public const string StreamResultAsync = "StreamResultAsync";

		public const string StreamHDF5Async = "StreamHDF5Async";

		public const string ImportMetRasterData = "ImportMetRasterData";
	}

	public static NamedPipeServerStream StartServerWaitConnection(string name)
	{
		NamedPipeServerStream namedPipeServerStream = new NamedPipeServerStream(name);
		namedPipeServerStream.WaitForConnection();
		return namedPipeServerStream;
	}

	public static NamedPipeServerStream StartAsyncServerWaitConnection(string name)
	{
		NamedPipeServerStream namedPipeServerStream = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
		namedPipeServerStream.WaitForConnection();
		return namedPipeServerStream;
	}

	public static NamedPipeClientStream StartClientWaitConnection(string name)
	{
		NamedPipeClientStream namedPipeClientStream = new NamedPipeClientStream(name);
		namedPipeClientStream.Connect();
		return namedPipeClientStream;
	}

	public static async Task<NamedPipeClientStream> StartAsyncClientWaitConnection(string name)
	{
		NamedPipeClientStream client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
		await client.ConnectAsync();
		return client;
	}

	public static string FormatArgs(string command, string pipeName)
	{
		return "-c=" + command + " -n=" + pipeName;
	}

	public static T[] ReadArray<T>(BinaryReader reader) where T : struct
	{
		if (typeof(T) == typeof(bool))
		{
			return (T[])(object)ReadArrayBool(reader);
		}
		if (typeof(T) == typeof(DateTime))
		{
			return (from l in ReadArray<long>(reader)
				select DateTime.FromBinary(l)).ToArray() as T[];
		}
		int readInt32 = reader.ReadInt32();
		if (readInt32 == 0)
		{
			return new T[0];
		}
		int sizeOf = Marshal.SizeOf<T>(default);
		byte[] bytes = reader.ReadBytes(readInt32 * sizeOf);
		T[] values = new T[readInt32];
		Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
		return values;
	}

	public static bool[] ReadArrayBool(BinaryReader reader)
	{
		byte[] byteValues = ReadArray<byte>(reader);
		if (byteValues == null)
		{
			return new bool[0];
		}
		bool[] boolValues = new bool[byteValues.Length];
		for (int i = 0; i < boolValues.Length; i++)
		{
			if (byteValues[i] > 0)
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

	public static void WriteArray<T>(BinaryWriter writer, Memory<T> arr) where T : struct
	{
		WriteArray(writer, arr.ToArray());
	}

	public static void WriteArray<T>(BinaryWriter writer, T[] arr) where T : struct
	{
		if (arr == null || arr.Length == 0)
		{
			writer.Write(0);
			writer.Flush();
			return;
		}
		if (typeof(T) == typeof(bool))
		{
			WriteArrayBool(writer, (bool[])(object)arr);
			return;
		}
		if (typeof(T) == typeof(DateTime))
		{
			WriteArray(writer, (arr as DateTime[]).Select((DateTime dt) => dt.ToBinary()).ToArray());
			return;
		}
		int arrLength = arr.Length;
		writer.Write(arrLength);
		int sizeOf = Marshal.SizeOf<T>(default);
		byte[] byteValues = new byte[arrLength * sizeOf];
		Buffer.BlockCopy(arr, 0, byteValues, 0, byteValues.Length);
		writer.Write(byteValues);
		writer.Flush();
	}

	public static void WriteSqueezedArray<T>(BinaryWriter writer, T[,] arr) where T : struct
	{
		if (arr == null || arr.Length == 0)
		{
			writer.Write(0);
			writer.Flush();
			return;
		}
		if (typeof(T) == typeof(bool))
		{
			WriteArrayBool(writer, (bool[])(object)arr);
			return;
		}
		int length = arr.Length;
		writer.Write(length);
		int sizeOf = Marshal.SizeOf<T>(default);
		byte[] byteValues = new byte[length * sizeOf];
		Buffer.BlockCopy(arr, 0, byteValues, 0, byteValues.Length);
		writer.Write(byteValues);
		writer.Flush();
	}

	public static void WriteArrayBool(BinaryWriter writer, bool[] arr)
	{
		if (arr == null)
		{
			writer.Write(0);
			writer.Flush();
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
		WriteArray(writer, byteValues);
	}

	public static void WriteString(BinaryWriter writer, string s)
	{
		byte[] bytes = new ASCIIEncoding().GetBytes(s);
		WriteArray(writer, bytes);
	}

	public static string ReadString(BinaryReader reader)
	{
		byte[] bytes = ReadArray<byte>(reader);
		return new ASCIIEncoding().GetString(bytes);
	}

	public static void WriteStringArray(BinaryWriter writer, IList<string> arr)
	{
		int count = arr.Count;
		writer.Write(count);
		if (arr.Count != 0)
		{
			ASCIIEncoding aSCIIEncoding = new ASCIIEncoding();
			for (int i = 0; i < arr.Count; i++)
			{
				string s = arr[i];
				byte[] bytes = aSCIIEncoding.GetBytes(s);
				WriteArray(writer, bytes);
			}
		}
	}

	public static string[] ReadStringArray(BinaryReader reader)
	{
		int readInt32 = reader.ReadInt32();
		if (readInt32 == 0)
		{
			return Array.Empty<string>();
		}
		ASCIIEncoding aSCIIEncoding = new ASCIIEncoding();
		string[] strings = new string[readInt32];
		for (int i = 0; i < readInt32; i++)
		{
			byte[] bytes = ReadArray<byte>(reader);
			strings[i] = aSCIIEncoding.GetString(bytes);
		}
		return strings;
	}

	public static void WriteDate(BinaryWriter bw, DateTime dt)
	{
		bw.Write(dt.ToFileTimeUtc());
	}

	public static DateTime ReadDate(BinaryReader br)
	{
		return DateTime.FromFileTimeUtc(br.ReadInt64());
	}
}
