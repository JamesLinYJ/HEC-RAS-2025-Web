using System;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text;

namespace Utility.LightweightPlotting;

public static class PngEncoder
{
	public static byte[] EncodeArgb(int[] pixels, int width, int height)
	{
		if (pixels == null)
		{
			throw new ArgumentNullException("pixels");
		}
		if (width <= 0 || height <= 0)
		{
			throw new ArgumentException($"Invalid dimensions {width}x{height}.");
		}
		if (pixels.Length != width * height)
		{
			throw new ArgumentException($"Expected {width * height} pixels, got {pixels.Length}.", "pixels");
		}
		int widthTerm = 1 + width * 4;
		byte[] byteValues = new byte[widthTerm * height];
		for (int i = 0; i < height; i++)
		{
			int ordinal = i * widthTerm;
			int widthTerm2 = i * width;
			for (int j = 0; j < width; j++)
			{
				int pixel = pixels[widthTerm2 + j];
				int ordinal2 = ordinal + 1 + j * 4;
				byteValues[ordinal2] = (byte)(pixel >> 16);
				byteValues[ordinal2 + 1] = (byte)(pixel >> 8);
				byteValues[ordinal2 + 2] = (byte)pixel;
				byteValues[ordinal2 + 3] = (byte)(pixel >> 24);
			}
		}
		byte[] data;
		using (MemoryStream memoryStream = new MemoryStream())
		{
			using (ZLibStream zLibStream = new ZLibStream(memoryStream, CompressionLevel.Optimal, leaveOpen: true))
			{
				zLibStream.Write(byteValues, 0, byteValues.Length);
			}
			data = memoryStream.ToArray();
		}
		byte[] byteValuesArray = new byte[13];
		WriteUInt32BE(byteValuesArray, 0, (uint)width);
		WriteUInt32BE(byteValuesArray, 4, (uint)height);
		byteValuesArray[8] = 8;
		byteValuesArray[9] = 6;
		byteValuesArray[10] = 0;
		byteValuesArray[11] = 0;
		byteValuesArray[12] = 0;
		using MemoryStream memoryStream2 = new MemoryStream();
		memoryStream2.Write(new byte[8] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
		WriteChunk(memoryStream2, "IHDR", byteValuesArray);
		WriteChunk(memoryStream2, "IDAT", data);
		WriteChunk(memoryStream2, "IEND", Array.Empty<byte>());
		return memoryStream2.ToArray();
	}

	private static void WriteChunk(Stream s, string type, byte[] data)
	{
		byte[] byteValues = new byte[4];
		WriteUInt32BE(byteValues, 0, (uint)data.Length);
		s.Write(byteValues, 0, 4);
		byte[] bytes = Encoding.ASCII.GetBytes(type);
		s.Write(bytes, 0, 4);
		s.Write(data, 0, data.Length);
		Crc32 crc = new Crc32();
		crc.Append(bytes);
		crc.Append(data);
		byte[] currentHash = crc.GetCurrentHash();
		s.WriteByte(currentHash[3]);
		s.WriteByte(currentHash[2]);
		s.WriteByte(currentHash[1]);
		s.WriteByte(currentHash[0]);
	}

	private static void WriteUInt32BE(byte[] buf, int off, uint v)
	{
		buf[off] = (byte)(v >> 24);
		buf[off + 1] = (byte)(v >> 16);
		buf[off + 2] = (byte)(v >> 8);
		buf[off + 3] = (byte)v;
	}
}
