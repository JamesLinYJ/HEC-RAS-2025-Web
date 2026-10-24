using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Hec.Dss;

internal class Compression
{
	internal static string UnCompress(string stringValue)
	{
		return UnCompress(Convert.FromBase64String(stringValue));
	}

	private static string UnCompress(byte[] b64)
	{
		MemoryStream stream = new MemoryStream(b64);
		MemoryStream memoryStream = new MemoryStream();
		using (GZipStream gZipStream = new GZipStream(stream, CompressionMode.Decompress))
		{
			gZipStream.CopyTo(memoryStream);
		}
		byte[] bytes = memoryStream.ToArray();
		return Encoding.UTF8.GetString(bytes);
	}

	private static void CopyTo(Stream src, Stream dest)
	{
		byte[] byteValues = new byte[4096];
		int count;
		while ((count = src.Read(byteValues, 0, byteValues.Length)) != 0)
		{
			dest.Write(byteValues, 0, count);
		}
	}

	internal static string Compress(string stringValue)
	{
		return Convert.ToBase64String(CompressInternal(stringValue));
	}

	private static byte[] CompressInternal(string textValue)
	{
		using MemoryStream src = new MemoryStream(Encoding.UTF8.GetBytes(textValue));
		using MemoryStream memoryStream = new MemoryStream();
		using (GZipStream dest = new GZipStream(memoryStream, CompressionMode.Compress))
		{
			CopyTo(src, dest);
		}
		return memoryStream.ToArray();
	}
}
