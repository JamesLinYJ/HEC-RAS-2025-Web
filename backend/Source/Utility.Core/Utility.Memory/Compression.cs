using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading;

namespace Utility.Memory;

public static class Compression
{
	private static ThreadLocal<byte[]> _compressionBuffers = new ThreadLocal<byte[]>(() => new byte[256]);

	public static void CompressTile(Stream dataStream, out byte[] buf, out int bufLen)
	{
		using MemoryStream memoryStream = new MemoryStream();
		using DeflateStream deflateStream = new DeflateStream(memoryStream, CompressionLevel.Fastest, leaveOpen: true);
		dataStream.CopyTo(deflateStream);
		deflateStream.Close();
		bufLen = (int)memoryStream.Length;
		buf = memoryStream.GetBuffer();
	}

	public static void CompressTile_Reuse_ZlibHeaderAdlerSpace(Stream dataStream, out byte[] buf, out int bufLen)
	{
		byte[] compressionBuffersTerm = _compressionBuffers.Value;
		int startPos = 2;
		using CustomMemoryStream customMemoryStream = new CustomMemoryStream(compressionBuffersTerm, startPos);
		using DeflateStream deflateStream = new DeflateStream(customMemoryStream, CompressionLevel.Fastest, leaveOpen: true);
		dataStream.CopyTo(deflateStream);
		deflateStream.Close();
		customMemoryStream.AddLength(4);
		bufLen = (int)customMemoryStream.Length;
		buf = customMemoryStream.Buffer;
		_compressionBuffers.Value = buf;
	}

	public static void DecompressTile<T>(Stream compStream, T[] buf) where T : unmanaged
	{
		byte[] byteValues = new byte[buf.Length * Marshal.SizeOf<T>()];
		using DeflateStream deflateStream = new DeflateStream(compStream, CompressionMode.Decompress);
		int count = deflateStream.Read(byteValues, 0, byteValues.Length);
		Buffer.BlockCopy(byteValues, 0, buf, 0, count);
	}
}
