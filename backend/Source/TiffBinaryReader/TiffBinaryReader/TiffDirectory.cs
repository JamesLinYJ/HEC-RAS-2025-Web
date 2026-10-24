using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TiffBinaryReader.TagTypeEnums;
using Utility;
using Utility.Compression;
using Utility.Logging;
using Utility.Memory;

namespace TiffBinaryReader;

[DebuggerDisplay("Page {DirectoryIndex}")]
public class TiffDirectory
{
	private static readonly ILogger _logger = LogHelper.GetLogger("TiffDirectory");

	private byte[] _noDataCompressedTileBuffer;

	public int TileReadsSaved;

	private readonly object _logTileLock = new object();

	private const int LogFrequency = 100;

	private int _recentTilesRead;

	private double _recentMs;

	private ThreadLocal<LZWCodec> _lzwCodecs;

	public static readonly bool UseNewLzw = true;

	public TiffFile Tiff { get; private set; }

	public long DirectoryIndex { get; private set; }

	public TagMetadata Metadata { get; private set; }

	public bool CanReadData
	{
		get
		{
			if (Metadata.CanReadData)
			{
				return LoadResult?.Result ?? true;
			}
			return false;
		}
	}

	public OperationResult LoadResult { get; private set; }

	public bool IsNoDataShortCircuitEnabled => _noDataCompressedTileBuffer != null;

	private TiffDirectory(TiffFile parent, BinaryReader br, long dirIndex, long dirStartPosition)
	{
		Tiff = parent;
		DirectoryIndex = dirIndex;
		LoadResult = OperationResult.Success();
		Metadata = TagMetadata.Read(br, dirStartPosition, Tiff.IsBigTiff);
		long length = br.BaseStream.Length;
		PostLoad(length);
		if (CanReadData && Metadata.CompressionType == CompressionTypes.LZW && !UseNewLzw)
		{
			_lzwCodecs = new ThreadLocal<LZWCodec>(() =>
			{
				LZWCodec lZWCodec = new LZWCodec();
				lZWCodec.Init(Metadata);
				return lZWCodec;
			});
		}
	}

	private TiffDirectory(TiffFile parent, long dirIndex, TagMetadata metadata, long fileLength)
	{
		Tiff = parent;
		DirectoryIndex = dirIndex;
		Metadata = metadata;
		LoadResult = OperationResult.Success();
		PostLoad(fileLength);
		if (CanReadData && Metadata.CompressionType == CompressionTypes.LZW && !UseNewLzw)
		{
			_lzwCodecs = new ThreadLocal<LZWCodec>(() =>
			{
				LZWCodec lZWCodec = new LZWCodec();
				lZWCodec.Init(Metadata);
				return lZWCodec;
			});
		}
	}

	private void PostLoad(long fileLength)
	{
		if (Metadata.CanReadErrors != null && !Metadata.CanReadErrors.Result)
		{
			LoadResult.MergeIn(Metadata.CanReadErrors);
		}
		if (!Metadata.IsTiled || Metadata.TileOffsetTag == null || Metadata.TileByteCountTag == null)
		{
			return;
		}
		int tileCount = Metadata.TileCount;
		for (int i = 0; i < tileCount; i++)
		{
			long tileByteOffset = Metadata.TileByteOffset(i);
			long tileByteCount = Metadata.TileByteCount(i);
			if (tileByteOffset + tileByteCount > fileLength)
			{
				LoadResult.AddFailure(TiffMessageCodes.TileExceedsFileLength.Create(i, tileByteOffset, tileByteCount, fileLength));
				break;
			}
		}
	}

	internal static List<TiffDirectory> Parse(TiffFile parent, BinaryReader br, bool parsePyramidLevels = true)
	{
		List<TiffDirectory> tiffDirectories = new List<TiffDirectory>();
		br.BaseStream.Position = 0L;
		long toUInt64BitConverterTerm;
		if (parent.IsBigTiff)
		{
			byte[] bytes = br.ReadBytes(16);
			if (BitConverter.ToInt16(bytes, 4) != 8)
			{
				throw new Exception("Can't read bigtiff with non 8-byte offsets.");
			}
			if (BitConverter.ToInt16(bytes, 6) != 0)
			{
				throw new Exception("Magic number '0' not present as 6th byte in file, doesn't follow the BigTIFF specifications.");
			}
			toUInt64BitConverterTerm = (long)BitConverter.ToUInt64(bytes, 8);
		}
		else
		{
			toUInt64BitConverterTerm = BitConverter.ToUInt32(br.ReadBytes(8), 4);
		}
		long ordinal = 0L;
		while (toUInt64BitConverterTerm != 0L)
		{
			tiffDirectories.Add(new TiffDirectory(parent, br, ordinal++, toUInt64BitConverterTerm));
			if (!parsePyramidLevels)
			{
				break;
			}
			toUInt64BitConverterTerm = tiffDirectories.Last().Metadata.NextDirectoryOffset;
		}
		return tiffDirectories;
	}

	internal static async Task<List<TiffDirectory>> ParseAsync(TiffFile parent, Stream stream, bool parsePyramidLevels = true, CancellationToken ct = default(CancellationToken))
	{
		List<TiffDirectory> retn = new List<TiffDirectory>();
		stream.Position = 0L;
		long toUInt64BitConverterTerm;
		if (parent.IsBigTiff)
		{
			byte[] startupBytes = new byte[16];
			await stream.ReadExactlyAsync(startupBytes, ct);
			if (BitConverter.ToInt16(startupBytes, 4) != 8)
			{
				throw new Exception("Can't read bigtiff with non 8-byte offsets.");
			}
			if (BitConverter.ToInt16(startupBytes, 6) != 0)
			{
				throw new Exception("Magic number '0' not present as 6th byte in file, doesn't follow the BigTIFF specifications.");
			}
			toUInt64BitConverterTerm = (long)BitConverter.ToUInt64(startupBytes, 8);
		}
		else
		{
			byte[] startupBytes = new byte[8];
			await stream.ReadExactlyAsync(startupBytes, ct);
			toUInt64BitConverterTerm = BitConverter.ToUInt32(startupBytes, 4);
		}
		long dirCount = 0L;
		long fileLength = stream.Length;
		while (toUInt64BitConverterTerm != 0L)
		{
			TagMetadata tagMetadata = await TagMetadata.ReadAsync(stream, toUInt64BitConverterTerm, parent.IsBigTiff, ct);
			retn.Add(new TiffDirectory(parent, dirCount++, tagMetadata, fileLength));
			if (!parsePyramidLevels)
			{
				break;
			}
			toUInt64BitConverterTerm = tagMetadata.NextDirectoryOffset;
		}
		return retn;
	}

	public T[] GetTile<T>(int tileIdx) where T : unmanaged
	{
		T[] tile = new T[Metadata.NumberCellsPerTile];
		GetTile<T>(tileIdx, tile);
		return tile;
	}

	public T[] GetTile<T>(int row, int col) where T : unmanaged
	{
		return GetTile<T>(row * Metadata.TilesWide + col);
	}

	public void GetTile<T>(int row, int col, Memory<T> buf) where T : unmanaged
	{
		GetTile(row * Metadata.TilesWide + col, buf);
	}

	public void GetTile<T>(int tileIdx, Memory<T> outputBuffer) where T : unmanaged
	{
		if (!CanReadData)
		{
			throw new Exception("Invalid file, check CanReadData");
		}
		if (outputBuffer.Length != Metadata.NumberCellsPerTile)
		{
			throw new ArgumentException();
		}
		if (tileIdx >= Metadata.TileCount)
		{
			throw new ArgumentOutOfRangeException("tileIdx");
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		long position = Metadata.TileByteOffset(tileIdx);
		int tileByteCount = (int)Metadata.TileByteCount(tileIdx);
		Memory<byte> mem;
		using (SharedMemoryPool<byte>.RentNoFill(tileByteCount, out mem))
		{
			Span<byte> memSpan = mem.Span;
			using (PoolHandle<Stream> poolHandle = Tiff.FSPool.Acquire())
			{
				Stream data = poolHandle.Data;
				data.Position = position;
				if (data.Read(memSpan) != tileByteCount)
				{
					throw new Exception("Tiff File corrupted - reached EOF prematurely.");
				}
			}
			T? noDataAs = Metadata.NoDataAs<T>();
			if (noDataAs.HasValue && TryMatchNoDataBuffer(memSpan))
			{
				Interlocked.Increment(ref TileReadsSaved);
				outputBuffer.Span.Fill(noDataAs.Value);
				return;
			}
			if (Metadata.IsCompressed)
			{
				if (Metadata.ParsedType == typeof(T))
				{
					Decompress(memSpan, outputBuffer);
				}
				else
				{
					DecompressTypeSwitch(memSpan, outputBuffer, Metadata.ParsedType);
				}
			}
			else if (Metadata.ParsedType == typeof(T))
			{
				UnsafeCopy(memSpan, outputBuffer.Span);
			}
			else
			{
				RawTypeSwitch(memSpan, outputBuffer, Metadata.ParsedType);
			}
			if (noDataAs.HasValue)
			{
				T outputBufferSpan2 = noDataAs.Value;
				int tileIdxWithTilesWide = tileIdx % Metadata.TilesWide;
				int tileIdxOverTilesWide = tileIdx / Metadata.TilesWide;
				int tileWidth = Metadata.TileWidth;
				Span<T> outputBufferSpan = outputBuffer.Span;
				if (tileIdxWithTilesWide == Metadata.TilesWide - 1)
				{
					int tilesWideMetadataTerm = (int)(Metadata.TilesWide * tileWidth - Metadata.ImageWidth);
					if (tilesWideMetadataTerm > 0)
					{
						for (int i = 0; i < tileWidth; i++)
						{
							for (int j = tileWidth - tilesWideMetadataTerm; j < tileWidth; j++)
							{
								outputBufferSpan[i * tileWidth + j] = outputBufferSpan2;
							}
						}
					}
				}
				if (tileIdxOverTilesWide == Metadata.TilesTall - 1)
				{
					int tilesTallMetadataTerm = (int)(Metadata.TilesTall * tileWidth - Metadata.ImageHeight);
					if (tilesTallMetadataTerm > 0)
					{
						for (int k = tileWidth - tilesTallMetadataTerm; k < tileWidth; k++)
						{
							for (int l = 0; l < tileWidth; l++)
							{
								outputBufferSpan[k * tileWidth + l] = outputBufferSpan2;
							}
						}
					}
				}
			}
			stopwatch.Stop();
			lock (_logTileLock)
			{
				_recentTilesRead++;
				_recentMs += stopwatch.Elapsed.TotalMilliseconds;
				if (_recentTilesRead >= 100)
				{
					_logger?.LogTrace("Average tile read time: {Duration} ms ({Filename})", Math.Round(_recentMs / (double)_recentTilesRead, 2), Tiff.Filename);
					_recentTilesRead = 0;
					_recentMs = 0.0;
				}
			}
		}
	}

	private void CheckDeflatePredictors<T>(Span<T> decompressedBuffer) where T : unmanaged
	{
		if (Metadata.Predictor == Predictors.FloatingPoint)
		{
			if (Metadata.SamplesPerPixel != 1)
			{
				throw new Exception("Cannot apply a predictor when SamplesPerPixel != 1");
			}
			Span<byte> data = MemoryMarshal.AsBytes(decompressedBuffer);
			if (Metadata.IsTiled)
			{
				LzwDecoder.UndoFpPredictor_1SPP(data, Metadata.BytesPerPixel, Metadata.TileWidth);
			}
			else
			{
				LzwDecoder.UndoFpPredictor_1SPP(data, Metadata.BytesPerPixel, (int)Metadata.ImageWidth);
			}
		}
		else if (Metadata.Predictor == Predictors.Horizontal)
		{
			if (Metadata.SamplesPerPixel != 1)
			{
				throw new Exception("Cannot apply a predictor when SamplesPerPixel != 1");
			}
			Span<byte> data2 = MemoryMarshal.AsBytes(decompressedBuffer);
			if (Metadata.IsTiled)
			{
				LzwDecoder.UndoHorizontalPredictor_1SPP(data2, Metadata.BytesPerPixel, Metadata.TileWidth);
			}
			else
			{
				LzwDecoder.UndoHorizontalPredictor_1SPP(data2, Metadata.BytesPerPixel, (int)Metadata.ImageWidth);
			}
		}
	}

	private unsafe static void UnsafeCopy<T>(Span<byte> fromData, Span<T> toData) where T : unmanaged
	{
		int toDataTerm = toData.Length * sizeof(T);
		fixed (byte* source = fromData)
		{
			fixed (T* destination = toData)
			{
				Buffer.MemoryCopy(source, destination, toDataTerm, toDataTerm);
			}
		}
	}

	private unsafe void Decompress<T>(Span<byte> comprSpan, Memory<T> outputBuffer) where T : unmanaged
	{
		if (Metadata.CompressionType == CompressionTypes.Deflate)
		{
			CompressionHelper.Decompress(comprSpan, outputBuffer.Span);
			CheckDeflatePredictors(outputBuffer.Span);
			return;
		}
		if (Metadata.CompressionType == CompressionTypes.LZW)
		{
			using (MemoryHandle memoryHandle = outputBuffer.Pin())
			{
				Span<byte> byteSpan = new Span<byte>(memoryHandle.Pointer, outputBuffer.Length * sizeof(T));
				if (UseNewLzw)
				{
					int predictor = (int)Metadata.Predictor;
					LzwDecoder.Options options = LzwDecoder.Options.Tiff(Metadata.SamplesPerPixel, sizeof(T) * 8, predictor);
					LzwDecoder.Decode(comprSpan, byteSpan, options, Metadata.TileWidth);
				}
				else
				{
					_lzwCodecs.Value.DecodeTile(comprSpan, byteSpan);
				}
				return;
			}
		}
		if (Metadata.CompressionType == CompressionTypes.PackBits)
		{
			CompressionHelper.DecompressPackbits(comprSpan, outputBuffer.Span);
			return;
		}
		if (Metadata.CompressionType == CompressionTypes.Uncompressed)
		{
			using (MemoryHandle memoryHandle2 = outputBuffer.Pin())
			{
				Span<byte> destination = new Span<byte>(memoryHandle2.Pointer, outputBuffer.Length * sizeof(T));
				comprSpan.CopyTo(destination);
				return;
			}
		}
		throw new NotImplementedException();
	}

	private void RawTypeSwitch<T>(Span<byte> rawSpan, Memory<T> outputBuffer, Type nativeType) where T : unmanaged
	{
		Type typeFromHandle = typeof(T);
		int length = outputBuffer.Length;
		if (nativeType == typeof(float))
		{
			Span<float> cast = MemoryMarshal.Cast<byte, float>(rawSpan);
			if (typeFromHandle == typeof(double))
			{
				Span<double> doubleSpan = (outputBuffer as Memory<double>?).GetValueOrDefault().Span;
				for (int i = 0; i < length; i++)
				{
					doubleSpan[i] = cast[i];
				}
				return;
			}
			throw new Exception("Cannot convert from source type " + nativeType.Name + " to " + typeFromHandle.Name);
		}
		if (nativeType == typeof(double))
		{
			Span<double> castSpan = MemoryMarshal.Cast<byte, double>(rawSpan);
			if (typeFromHandle == typeof(float))
			{
				Span<float> floatSpan = (outputBuffer as Memory<float>?).GetValueOrDefault().Span;
				for (int j = 0; j < length; j++)
				{
					floatSpan[j] = (float)castSpan[j];
				}
				return;
			}
			throw new Exception("Cannot convert from source type " + nativeType.Name + " to " + typeFromHandle.Name);
		}
		Memory<short> valueOrDefault;
		Memory<int> valueOrDefault2;
		Memory<long> valueOrDefault3;
		if (nativeType == typeof(byte))
		{
			Span<byte> rawSpan2 = rawSpan;
			if (typeFromHandle == typeof(short))
			{
				valueOrDefault = (outputBuffer as Memory<short>?).GetValueOrDefault();
				Span<short> valueOrDefaultSpan = valueOrDefault.Span;
				for (int k = 0; k < length; k++)
				{
					valueOrDefaultSpan[k] = rawSpan2[k];
				}
				return;
			}
			if (typeFromHandle == typeof(int))
			{
				valueOrDefault2 = (outputBuffer as Memory<int>?).GetValueOrDefault();
				Span<int> valueOrDefault2Span = valueOrDefault2.Span;
				for (int l = 0; l < length; l++)
				{
					valueOrDefault2Span[l] = rawSpan2[l];
				}
				return;
			}
			if (typeFromHandle == typeof(long))
			{
				valueOrDefault3 = (outputBuffer as Memory<long>?).GetValueOrDefault();
				Span<long> valueOrDefault3Span = valueOrDefault3.Span;
				for (int m = 0; m < length; m++)
				{
					valueOrDefault3Span[m] = rawSpan2[m];
				}
				return;
			}
			throw new Exception("Cannot convert from source type " + nativeType.Name + " to " + typeFromHandle.Name);
		}
		Memory<byte> valueOrDefault4;
		if (nativeType == typeof(short))
		{
			Span<short> cast2 = MemoryMarshal.Cast<byte, short>(rawSpan);
			if (typeFromHandle == typeof(byte))
			{
				valueOrDefault4 = (outputBuffer as Memory<byte>?).GetValueOrDefault();
				Span<byte> valueOrDefault4Span = valueOrDefault4.Span;
				for (int n = 0; n < length; n++)
				{
					valueOrDefault4Span[n] = (byte)cast2[n];
				}
				return;
			}
			if (typeFromHandle == typeof(int))
			{
				valueOrDefault2 = (outputBuffer as Memory<int>?).GetValueOrDefault();
				Span<int> valueOrDefault22 = valueOrDefault2.Span;
				for (int intervalIndex = 0; intervalIndex < length; intervalIndex++)
				{
					valueOrDefault22[intervalIndex] = cast2[intervalIndex];
				}
				return;
			}
			if (typeFromHandle == typeof(long))
			{
				valueOrDefault3 = (outputBuffer as Memory<long>?).GetValueOrDefault();
				Span<long> valueOrDefault32 = valueOrDefault3.Span;
				for (int intervalIndex2 = 0; intervalIndex2 < length; intervalIndex2++)
				{
					valueOrDefault32[intervalIndex2] = cast2[intervalIndex2];
				}
				return;
			}
			throw new Exception("Cannot convert from source type " + nativeType.Name + " to " + typeFromHandle.Name);
		}
		if (nativeType == typeof(int))
		{
			Span<int> cast3 = MemoryMarshal.Cast<byte, int>(rawSpan);
			if (typeFromHandle == typeof(byte))
			{
				valueOrDefault4 = (outputBuffer as Memory<byte>?).GetValueOrDefault();
				Span<byte> valueOrDefault42 = valueOrDefault4.Span;
				for (int intervalIndex3 = 0; intervalIndex3 < length; intervalIndex3++)
				{
					valueOrDefault42[intervalIndex3] = (byte)cast3[intervalIndex3];
				}
				return;
			}
			if (typeFromHandle == typeof(short))
			{
				valueOrDefault = (outputBuffer as Memory<short>?).GetValueOrDefault();
				Span<short> valueOrDefault5 = valueOrDefault.Span;
				for (int intervalIndex4 = 0; intervalIndex4 < length; intervalIndex4++)
				{
					valueOrDefault5[intervalIndex4] = (short)cast3[intervalIndex4];
				}
				return;
			}
			if (typeFromHandle == typeof(long))
			{
				valueOrDefault3 = (outputBuffer as Memory<long>?).GetValueOrDefault();
				Span<long> valueOrDefault33 = valueOrDefault3.Span;
				for (int intervalIndex5 = 0; intervalIndex5 < length; intervalIndex5++)
				{
					valueOrDefault33[intervalIndex5] = cast3[intervalIndex5];
				}
				return;
			}
			throw new Exception("Cannot convert from source type " + nativeType.Name + " to " + typeFromHandle.Name);
		}
		if (nativeType == typeof(long))
		{
			Span<long> cast4 = MemoryMarshal.Cast<byte, long>(rawSpan);
			if (typeFromHandle == typeof(byte))
			{
				valueOrDefault4 = (outputBuffer as Memory<byte>?).GetValueOrDefault();
				Span<byte> valueOrDefault43 = valueOrDefault4.Span;
				for (int intervalIndex6 = 0; intervalIndex6 < length; intervalIndex6++)
				{
					valueOrDefault43[intervalIndex6] = (byte)cast4[intervalIndex6];
				}
				return;
			}
			if (typeFromHandle == typeof(short))
			{
				valueOrDefault = (outputBuffer as Memory<short>?).GetValueOrDefault();
				Span<short> valueOrDefault6 = valueOrDefault.Span;
				for (int intervalIndex7 = 0; intervalIndex7 < length; intervalIndex7++)
				{
					valueOrDefault6[intervalIndex7] = (short)cast4[intervalIndex7];
				}
				return;
			}
			if (typeFromHandle == typeof(int))
			{
				valueOrDefault2 = (outputBuffer as Memory<int>?).GetValueOrDefault();
				Span<int> valueOrDefault23 = valueOrDefault2.Span;
				for (int intervalIndex8 = 0; intervalIndex8 < length; intervalIndex8++)
				{
					valueOrDefault23[intervalIndex8] = (int)cast4[intervalIndex8];
				}
				return;
			}
			throw new Exception("Cannot convert from source type " + nativeType.Name + " to " + typeFromHandle.Name);
		}
		throw new Exception("Cannot convert from source type " + nativeType.Name + " to " + typeFromHandle.Name);
	}

	private void DecompressTypeSwitch<T>(Span<byte> comprSpan, Memory<T> outputBuffer, Type nativeType) where T : unmanaged
	{
		if (Metadata.CompressionType != CompressionTypes.Deflate)
		{
			throw new NotImplementedException();
		}
		Type typeFromHandle = typeof(T);
		int length = outputBuffer.Length;
		Memory<float> mem;
		if (nativeType == typeof(float))
		{
			using (SharedMemoryPool<float>.RentNoFill(length, out mem))
			{
				CompressionHelper.Decompress(comprSpan, mem);
				CheckDeflatePredictors(mem.Span);
				if (typeFromHandle == typeof(double))
				{
					Span<float> memSpan = mem.Span;
					Span<double> doubleSpan = (outputBuffer as Memory<double>?).GetValueOrDefault().Span;
					for (int i = 0; i < length; i++)
					{
						doubleSpan[i] = memSpan[i];
					}
					return;
				}
				throw new Exception("Cannot convert from source type " + nativeType.Name + " to " + typeFromHandle.Name);
			}
		}
		Memory<double> mem2;
		if (nativeType == typeof(double))
		{
			using (SharedMemoryPool<double>.RentNoFill(length, out mem2))
			{
				CompressionHelper.Decompress(comprSpan, mem2);
				CheckDeflatePredictors(mem2.Span);
				if (typeFromHandle == typeof(float))
				{
					Span<double> mem2Span = mem2.Span;
					Span<float> floatSpan = (outputBuffer as Memory<float>?).GetValueOrDefault().Span;
					for (int j = 0; j < length; j++)
					{
						floatSpan[j] = (float)mem2Span[j];
					}
					return;
				}
				throw new Exception("Cannot convert from source type " + nativeType.Name + " to " + typeFromHandle.Name);
			}
		}
		Memory<byte> mem3;
		Memory<short> valueOrDefault;
		Memory<int> valueOrDefault2;
		Memory<long> valueOrDefault3;
		if (nativeType == typeof(byte))
		{
			using (SharedMemoryPool<byte>.RentNoFill(length, out mem3))
			{
				CompressionHelper.Decompress(comprSpan, mem3);
				CheckDeflatePredictors(mem3.Span);
				if (typeFromHandle == typeof(short))
				{
					Span<byte> mem3Span = mem3.Span;
					valueOrDefault = (outputBuffer as Memory<short>?).GetValueOrDefault();
					Span<short> valueOrDefaultSpan = valueOrDefault.Span;
					for (int k = 0; k < length; k++)
					{
						valueOrDefaultSpan[k] = mem3Span[k];
					}
				}
				else if (typeFromHandle == typeof(int))
				{
					Span<byte> mem32 = mem3.Span;
					valueOrDefault2 = (outputBuffer as Memory<int>?).GetValueOrDefault();
					Span<int> valueOrDefault2Span = valueOrDefault2.Span;
					for (int l = 0; l < length; l++)
					{
						valueOrDefault2Span[l] = mem32[l];
					}
				}
				else
				{
					if (!(typeFromHandle == typeof(long)))
					{
						throw new Exception("Cannot convert from source type " + nativeType.Name + " to " + typeFromHandle.Name);
					}
					Span<byte> mem33 = mem3.Span;
					valueOrDefault3 = (outputBuffer as Memory<long>?).GetValueOrDefault();
					Span<long> valueOrDefault3Span = valueOrDefault3.Span;
					for (int m = 0; m < length; m++)
					{
						valueOrDefault3Span[m] = mem33[m];
					}
				}
				return;
			}
		}
		Memory<short> mem4;
		Memory<byte> valueOrDefault4;
		if (nativeType == typeof(short))
		{
			using (SharedMemoryPool<short>.RentNoFill(length, out mem4))
			{
				CompressionHelper.Decompress(comprSpan, mem4);
				CheckDeflatePredictors(mem4.Span);
				if (typeFromHandle == typeof(byte))
				{
					Span<short> mem4Span = mem4.Span;
					valueOrDefault4 = (outputBuffer as Memory<byte>?).GetValueOrDefault();
					Span<byte> valueOrDefault4Span = valueOrDefault4.Span;
					for (int n = 0; n < length; n++)
					{
						valueOrDefault4Span[n] = (byte)mem4Span[n];
					}
				}
				else if (typeFromHandle == typeof(int))
				{
					Span<short> mem42 = mem4.Span;
					valueOrDefault2 = (outputBuffer as Memory<int>?).GetValueOrDefault();
					Span<int> valueOrDefault22 = valueOrDefault2.Span;
					for (int intervalIndex = 0; intervalIndex < length; intervalIndex++)
					{
						valueOrDefault22[intervalIndex] = mem42[intervalIndex];
					}
				}
				else
				{
					if (!(typeFromHandle == typeof(long)))
					{
						throw new Exception("Cannot convert from source type " + nativeType.Name + " to " + typeFromHandle.Name);
					}
					Span<short> mem43 = mem4.Span;
					valueOrDefault3 = (outputBuffer as Memory<long>?).GetValueOrDefault();
					Span<long> valueOrDefault32 = valueOrDefault3.Span;
					for (int intervalIndex2 = 0; intervalIndex2 < length; intervalIndex2++)
					{
						valueOrDefault32[intervalIndex2] = mem43[intervalIndex2];
					}
				}
				return;
			}
		}
		Memory<int> mem5;
		if (nativeType == typeof(int))
		{
			using (SharedMemoryPool<int>.RentNoFill(length, out mem5))
			{
				CompressionHelper.Decompress(comprSpan, mem5);
				CheckDeflatePredictors(mem5.Span);
				if (typeFromHandle == typeof(byte))
				{
					Span<int> mem5Span = mem5.Span;
					valueOrDefault4 = (outputBuffer as Memory<byte>?).GetValueOrDefault();
					Span<byte> valueOrDefault42 = valueOrDefault4.Span;
					for (int intervalIndex3 = 0; intervalIndex3 < length; intervalIndex3++)
					{
						valueOrDefault42[intervalIndex3] = (byte)mem5Span[intervalIndex3];
					}
				}
				else if (typeFromHandle == typeof(short))
				{
					Span<int> mem52 = mem5.Span;
					valueOrDefault = (outputBuffer as Memory<short>?).GetValueOrDefault();
					Span<short> valueOrDefault5 = valueOrDefault.Span;
					for (int intervalIndex4 = 0; intervalIndex4 < length; intervalIndex4++)
					{
						valueOrDefault5[intervalIndex4] = (short)mem52[intervalIndex4];
					}
				}
				else
				{
					if (!(typeFromHandle == typeof(long)))
					{
						throw new Exception("Cannot convert from source type " + nativeType.Name + " to " + typeFromHandle.Name);
					}
					Span<int> mem53 = mem5.Span;
					valueOrDefault3 = (outputBuffer as Memory<long>?).GetValueOrDefault();
					Span<long> valueOrDefault33 = valueOrDefault3.Span;
					for (int intervalIndex5 = 0; intervalIndex5 < length; intervalIndex5++)
					{
						valueOrDefault33[intervalIndex5] = mem53[intervalIndex5];
					}
				}
				return;
			}
		}
		Memory<long> mem6;
		if (nativeType == typeof(long))
		{
			using (SharedMemoryPool<long>.RentNoFill(length, out mem6))
			{
				CompressionHelper.Decompress(comprSpan, mem6);
				CheckDeflatePredictors(mem6.Span);
				if (typeFromHandle == typeof(byte))
				{
					Span<long> mem6Span = mem6.Span;
					valueOrDefault4 = (outputBuffer as Memory<byte>?).GetValueOrDefault();
					Span<byte> valueOrDefault43 = valueOrDefault4.Span;
					for (int intervalIndex6 = 0; intervalIndex6 < length; intervalIndex6++)
					{
						valueOrDefault43[intervalIndex6] = (byte)mem6Span[intervalIndex6];
					}
				}
				else if (typeFromHandle == typeof(short))
				{
					Span<long> mem62 = mem6.Span;
					valueOrDefault = (outputBuffer as Memory<short>?).GetValueOrDefault();
					Span<short> valueOrDefault6 = valueOrDefault.Span;
					for (int intervalIndex7 = 0; intervalIndex7 < length; intervalIndex7++)
					{
						valueOrDefault6[intervalIndex7] = (short)mem62[intervalIndex7];
					}
				}
				else
				{
					if (!(typeFromHandle == typeof(int)))
					{
						throw new Exception("Cannot convert from source type " + nativeType.Name + " to " + typeFromHandle.Name);
					}
					Span<long> mem63 = mem6.Span;
					valueOrDefault2 = (outputBuffer as Memory<int>?).GetValueOrDefault();
					Span<int> valueOrDefault23 = valueOrDefault2.Span;
					for (int intervalIndex8 = 0; intervalIndex8 < length; intervalIndex8++)
					{
						valueOrDefault23[intervalIndex8] = (int)mem63[intervalIndex8];
					}
				}
				return;
			}
		}
		throw new Exception("Cannot convert from source type " + nativeType.Name + " to " + typeFromHandle.Name);
	}

	private bool TryMatchNoDataBuffer(Span<byte> compressedBuf)
	{
		if (_noDataCompressedTileBuffer != null && compressedBuf.Length == _noDataCompressedTileBuffer.Length)
		{
			return Buffers.Compare(compressedBuf, _noDataCompressedTileBuffer);
		}
		return false;
	}

	public byte[] GetCompressedTile(int tileIdx)
	{
		byte[] buf = null;
		GetCompressedTile(tileIdx, ref buf);
		return buf;
	}

	public void GetCompressedTile(int tileIdx, ref byte[] buf)
	{
		if (!CanReadData)
		{
			buf = null;
			return;
		}
		if (tileIdx >= Metadata.TileCount)
		{
			throw new ArgumentOutOfRangeException("tileIdx");
		}
		long position = Metadata.TileByteOffset(tileIdx);
		int tileByteCount = (int)Metadata.TileByteCount(tileIdx);
		Buffers.EnsureArraySize(ref buf, tileByteCount);
		using PoolHandle<Stream> poolHandle = Tiff.FSPool.Acquire();
		Stream data = poolHandle.Data;
		data.Position = position;
		if (data.Read(buf, 0, tileByteCount) != tileByteCount)
		{
			throw new Exception("Tiff File corrupted - reached EOF prematurely.");
		}
	}

	public bool TryEnableNoDataShortCircuit<T>() where T : unmanaged, IEquatable<T>
	{
		_noDataCompressedTileBuffer = null;
		if (!Metadata.IsTiled)
		{
			return false;
		}
		if (!Metadata.CanReadData)
		{
			return false;
		}
		if (!Metadata.IsCompressed)
		{
			return false;
		}
		T? noDataAs = Metadata.NoDataAs<T>();
		if (!noDataAs.HasValue)
		{
			return false;
		}
		return TryGetCompressedNodataTile(noDataAs.Value, ref _noDataCompressedTileBuffer);
	}

	private bool TryGetCompressedNodataTile<T>(T nd, ref byte[] compressedBuf) where T : unmanaged, IEquatable<T>
	{
		long maxValue = long.MaxValue;
		int tileIdx = -1;
		for (int i = 0; i < Metadata.TileCount; i++)
		{
			long tileByteCount = Metadata.TileByteCount(i);
			if (tileByteCount < maxValue)
			{
				tileIdx = i;
				maxValue = tileByteCount;
			}
		}
		T[] tile = GetTile<T>(tileIdx);
		for (int j = 0; j < tile.Length; j++)
		{
			if (!tile[j].Equals(nd))
			{
				return false;
			}
		}
		compressedBuf = GetCompressedTile(tileIdx);
		return true;
	}

	public void GetValidRegion(int tileNumber, out int validWidth, out int validHeight)
	{
		int tileWidth = Metadata.TileWidth;
		int tileHeight = Metadata.TileHeight;
		int tilesWide = Metadata.TilesWide;
		_ = Metadata.TilesTall;
		int imageWidth = (int)Metadata.ImageWidth;
		int imageHeight = (int)Metadata.ImageHeight;
		int tileNumberWithTilesWide = tileNumber % tilesWide;
		int tileNumberOverTilesWide = tileNumber / tilesWide;
		int tileWidthImageWidthTerm = (tileNumberWithTilesWide + 1) * tileWidth - imageWidth;
		int tileHeightImageHeightTerm = (tileNumberOverTilesWide + 1) * tileHeight - imageHeight;
		if (tileWidthImageWidthTerm > 0)
		{
			validWidth = tileWidth - tileWidthImageWidthTerm;
		}
		else
		{
			validWidth = tileWidth;
		}
		if (tileHeightImageHeightTerm > 0)
		{
			validHeight = tileHeight - tileHeightImageHeightTerm;
		}
		else
		{
			validHeight = tileHeight;
		}
	}

	public T[] GetTileVB<T>(int tIdx) where T : struct
	{
		if (typeof(T) == typeof(float))
		{
			return (T[])(object)GetTile<float>(tIdx);
		}
		if (typeof(T) == typeof(int))
		{
			return (T[])(object)GetTile<int>(tIdx);
		}
		if (typeof(T) == typeof(short))
		{
			return (T[])(object)GetTile<short>(tIdx);
		}
		if (typeof(T) == typeof(byte))
		{
			return (T[])(object)GetTile<byte>(tIdx);
		}
		throw new NotImplementedException();
	}

	public bool IsEntirelyNoData<T>() where T : unmanaged, IEquatable<T>
	{
		if (!IsNoDataShortCircuitEnabled && !TryEnableNoDataShortCircuit<T>())
		{
			return false;
		}
		if (!Metadata.IsTiled)
		{
			return false;
		}
		long noDataCompressedTileBufferLength = _noDataCompressedTileBuffer.Length;
		for (int i = 0; i < Metadata.TileCount; i++)
		{
			if (Metadata.TileByteCount(i) != noDataCompressedTileBufferLength)
			{
				return false;
			}
		}
		byte[] buf = new byte[noDataCompressedTileBufferLength];
		for (int j = 0; j < Metadata.TileCount; j++)
		{
			GetCompressedTile(j, ref buf);
			if (!TryMatchNoDataBuffer(buf))
			{
				return false;
			}
		}
		return true;
	}
}
