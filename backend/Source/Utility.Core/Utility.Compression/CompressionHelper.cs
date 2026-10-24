using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;
using Utility.Progress;

namespace Utility.Compression;

public static class CompressionHelper
{
	public static void Decompress<T>(ReadOnlySpan<byte> compressedBuf, T[] outputBuffer) where T : unmanaged
	{
		Decompress(compressedBuf, outputBuffer.AsSpan());
	}

	public static void Decompress<T>(ReadOnlySpan<byte> compressedBuf, Memory<T> outputBuffer) where T : unmanaged
	{
		Decompress(compressedBuf, outputBuffer.Span);
	}

	public unsafe static void Decompress<T>(ReadOnlySpan<byte> compressedBuf, Span<T> outputBuffer) where T : unmanaged
	{
		if (compressedBuf[0] == 120)
		{
			compressedBuf = compressedBuf.Slice(2);
		}
		else if (compressedBuf[0] == 88 && compressedBuf[1] == 133)
		{
			compressedBuf = compressedBuf.Slice(2);
		}
		fixed (byte* pointer = compressedBuf)
		{
			using UnmanagedMemoryStream compressedStream = new UnmanagedMemoryStream(pointer, compressedBuf.Length);
			DecompressStream(compressedStream, outputBuffer);
		}
	}

	public static void Decompress<T>(Span<byte> compressedBuf, T[,] outputBuffer) where T : unmanaged
	{
		Decompress(compressedBuf, outputBuffer.AsMemory2D());
	}

	public unsafe static void Decompress<T>(Span<byte> compressedBuf, Memory2D<T> outputBuffer) where T : unmanaged
	{
		if (compressedBuf[0] == 120)
		{
			compressedBuf = compressedBuf.Slice(2);
		}
		else if (compressedBuf[0] == 88 && compressedBuf[1] == 133)
		{
			compressedBuf = compressedBuf.Slice(2);
		}
		fixed (byte* pointer = compressedBuf)
		{
			fixed (T* tPtr = outputBuffer.Span)
			{
				using UnmanagedMemoryStream compressedStream = new UnmanagedMemoryStream(pointer, compressedBuf.Length);
				DecompressStream(compressedStream, tPtr, (int)outputBuffer.Length);
			}
		}
	}

	private unsafe static void DecompressStream<T>(Stream compressedStream, Span<T> outputBuf) where T : unmanaged
	{
		fixed (T* tPtr = outputBuf)
		{
			DecompressStream(compressedStream, tPtr, outputBuf.Length);
		}
	}

	private unsafe static void DecompressStream<T>(Stream compressedStream, T* tPtr, int tLen) where T : unmanaged
	{
		using DeflateStream deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress);
		int tLenTerm = tLen * sizeof(T);
		int i = 0;
		Span<byte> byteSpan = new Span<byte>(tPtr, tLenTerm);
		int read;
		for (; i != tLenTerm; i += read)
		{
			Span<byte> buffer = byteSpan.Slice(i, tLenTerm - i);
			read = deflateStream.Read(buffer);
			if (read == 0)
			{
				break;
			}
		}
	}

	public static void DecompressInto(Stream compressedStream, Stream outStream)
	{
		using DeflateStream deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress);
		Span<byte> buffer = new byte[8192];
		while (true)
		{
			int read = deflateStream.Read(buffer);
			if (read == 0)
			{
				break;
			}
			outStream.Write(buffer.Slice(0, read));
		}
	}

	public static void DecompressPackbits<T>(ReadOnlySpan<byte> packedBuffer, Span<T> outputBuffer) where T : unmanaged
	{
		Span<byte> outSp = MemoryMarshal.AsBytes(outputBuffer);
		DecompressPackbits(packedBuffer, outSp);
	}

	public static void DecompressPackbits(ReadOnlySpan<byte> packedBuffer, Span<byte> outSp)
	{
		int i = 0;
		int ordinal = 0;
		for (; i < packedBuffer.Length; i++)
		{
			byte b = packedBuffer[i];
			if (b == 128)
			{
				continue;
			}
			if (b > 128)
			{
				int count = -(sbyte)b + 1;
				byte b2 = packedBuffer[i + 1];
				for (int j = 0; j < count; j++)
				{
					outSp[ordinal++] = b2;
				}
				i++;
			}
			else
			{
				for (int k = 0; k <= b; k++)
				{
					outSp[ordinal++] = packedBuffer[i + k + 1];
				}
				i += b + 1;
			}
		}
	}

	public static void ZipDirectory(string sourceDirectoryName, string destinationArchiveFileName, CompressionLevel baseCompressionLevel, Func<string, bool> includeFilePredicate, Func<string, bool> compressFilePredicate, ProgressReporter? reporter = null)
	{
		if (string.IsNullOrEmpty(sourceDirectoryName))
		{
			throw new ArgumentNullException("sourceDirectoryName");
		}
		if (string.IsNullOrEmpty(destinationArchiveFileName))
		{
			throw new ArgumentNullException("destinationArchiveFileName");
		}
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		reporter.ReportMessage("Zipping " + sourceDirectoryName);
		string[] files = Directory.GetFiles(sourceDirectoryName, "*", SearchOption.AllDirectories);
		string[] entryNames = GetEntryNames(files, sourceDirectoryName, includeBaseName: true);
		using FileStream stream = new FileStream(destinationArchiveFileName, FileMode.Create);
		using ZipArchive destination = new ZipArchive(stream, ZipArchiveMode.Create);
		int fileLength = files.Length;
		for (int i = 0; i < fileLength; i++)
		{
			string filesTerm = files[i];
			reporter.ReportProgressFraction(i, fileLength);
			reporter.ReportMessage("Zipping " + filesTerm);
			if (includeFilePredicate(filesTerm))
			{
				if (compressFilePredicate(filesTerm))
				{
					destination.CreateEntryFromFile(filesTerm, entryNames[i], baseCompressionLevel);
				}
				else
				{
					destination.CreateEntryFromFile(filesTerm, entryNames[i], CompressionLevel.NoCompression);
				}
			}
		}
	}

	private static string[] GetEntryNames(string[] names, string sourceFolder, bool includeBaseName)
	{
		if (names == null || names.Length == 0)
		{
			return new string[0];
		}
		if (includeBaseName)
		{
			sourceFolder = Path.GetDirectoryName(sourceFolder);
		}
		int sourceFolderLength = ((!string.IsNullOrEmpty(sourceFolder)) ? sourceFolder.Length : 0);
		if (sourceFolderLength > 0 && sourceFolder != null && sourceFolder[sourceFolderLength - 1] != Path.DirectorySeparatorChar && sourceFolder[sourceFolderLength - 1] != Path.AltDirectorySeparatorChar)
		{
			sourceFolderLength++;
		}
		string[] strings = new string[names.Length];
		for (int i = 0; i < names.Length; i++)
		{
			strings[i] = names[i].Substring(sourceFolderLength).Replace(Path.DirectorySeparatorChar, '/');
		}
		return strings;
	}
}
