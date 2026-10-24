using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.Extensions.Logging;
using Utility.Compression;
using Utility.Files;
using Utility.Logging;
using Utility.Memory;

namespace TiffBinaryReader;

public class TiffFileWriter : IDisposable
{
	private struct QueuedTile(int dirIdx, int tIdx, IMemoryOwner<byte> owner, ReadOnlyMemory<byte> buf)
	{
		public int DirectoryIndex = dirIdx;

		public int TileIndex = tIdx;

		public IMemoryOwner<byte> Owner = owner;

		public ReadOnlyMemory<byte> Buffer = buf;
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("TiffFileWriter");

	private IList<TagMetadata> _dirDefs;

	private object _tilesWrittenLock = new object();

	private long[][] _tileByteLocations;

	private long[][] _tileByteCounts;

	private readonly Stream _fs;

	private readonly bool _cleanFs;

	private readonly string _filename;

	private long _multiWriteBytesWasted;

	private readonly ConcurrentQueue<QueuedTile> _queuedData = new ConcurrentQueue<QueuedTile>();

	private Pool<Stream> _fsReaderPool;

	private readonly object _writeLock = new object();

	private readonly CompressionLevel _lvl;

	private bool _isDisposed;

	public int DirectoryCount => _dirDefs.Count;

	public int MaxTileQueueDepth { get; set; } = 16;

	public TiffFileWriter(string filename, TagMetadata directoryToCreate, CompressionLevel lvl = CompressionLevel.Optimal)
		: this(filename, new TagMetadata[1] { directoryToCreate }, lvl)
	{
	}

	public TiffFileWriter(string filename, IList<TagMetadata> directoriesToCreate, CompressionLevel lvl = CompressionLevel.Optimal)
	{
		if (File.Exists(filename))
		{
			throw new ArgumentException("File " + Path.GetFileName(filename) + " already exists, cannot overwrite.");
		}
		FileHelpers.EnsurePathExists(filename);
		_filename = filename;
		_fs = new FileStream(filename, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite);
		_dirDefs = directoriesToCreate;
		_cleanFs = true;
		_lvl = lvl;
		Init();
	}

	public TiffFileWriter(Stream filestream, TagMetadata dir, CompressionLevel lvl = CompressionLevel.Optimal)
		: this(filestream, new TagMetadata[1] { dir }, lvl)
	{
	}

	public TiffFileWriter(Stream filestream, IList<TagMetadata> dirs, CompressionLevel lvl = CompressionLevel.Optimal)
	{
		_fs = filestream;
		_dirDefs = dirs;
		_cleanFs = false;
		_lvl = lvl;
		Init();
	}

	private void Init()
	{
		_tileByteLocations = new long[DirectoryCount][];
		_tileByteCounts = new long[DirectoryCount][];
		for (int i = 0; i < _dirDefs.Count; i++)
		{
			int tileCount = _dirDefs[i].TileCount;
			_tileByteLocations[i] = new long[tileCount];
			_tileByteCounts[i] = new long[tileCount];
		}
		Span<byte> byteSpan = stackalloc byte[16];
		byteSpan[0] = 73;
		byteSpan[1] = 73;
		byteSpan[2] = 43;
		byteSpan[3] = 0;
		byteSpan[4] = 8;
		byteSpan[5] = 0;
		byteSpan[6] = 0;
		byteSpan[7] = 0;
		_fs.Write(byteSpan);
		if (_fs is FileStream)
		{
			_fsReaderPool = new Pool<Stream>(() => new FileStream(_filename, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite));
		}
		else
		{
			_fsReaderPool = null;
		}
	}

	public void WriteTileSerial<T>(int dirIdx, int tileIdx, Memory<T> data) where T : unmanaged
	{
		if (dirIdx < 0 || dirIdx >= DirectoryCount)
		{
			throw new ArgumentOutOfRangeException("dirIdx");
		}
		TagMetadata tagMetadata = _dirDefs[dirIdx];
		if (tileIdx < 0 || tileIdx >= tagMetadata.TileCount)
		{
			throw new ArgumentOutOfRangeException("tileIdx");
		}
		if (data.IsEmpty || data.Length != tagMetadata.NumberCellsPerTile * tagMetadata.SamplesPerPixel)
		{
			throw new ArgumentException("Data should be " + tagMetadata.NumberCellsPerTile + "x" + tagMetadata.SamplesPerPixel + " elements.");
		}
		WriteTileDirect(data, out var byteOffset, out var byteCount);
		_tileByteLocations[dirIdx][tileIdx] = byteOffset;
		_tileByteCounts[dirIdx][tileIdx] = byteCount;
	}

	private unsafe void WriteTileDirect<T>(Memory<T> data, out long byteOffset, out long byteCount) where T : unmanaged
	{
		Span<T> bufferSpan = data.Span;
		Memory<byte> buffer;
		IMemoryOwner<byte> cleanup;
		fixed (T* localValue = bufferSpan)
		{
			byte* localValue2;
			byte* pointer = (localValue2 = (byte*)localValue);
			int ordinal = bufferSpan.Length * sizeof(T);
			using UnmanagedMemoryStream dataStream = new UnmanagedMemoryStream(pointer, ordinal);
			CompressTileZLib(dataStream, _lvl, out buffer, out cleanup);
		}
		byteOffset = _fs.Length;
		byteCount = buffer.Length;
		_fs.Write(buffer.Span);
		cleanup?.Dispose();
	}

	public unsafe void WriteTileParallel<T>(int dirIdx, int tileIdx, Memory<T> data) where T : unmanaged
	{
		if (dirIdx < 0 || dirIdx >= DirectoryCount)
		{
			throw new ArgumentOutOfRangeException("dirIdx");
		}
		TagMetadata tagMetadata = _dirDefs[dirIdx];
		if (tileIdx < 0 || tileIdx >= tagMetadata.TileCount)
		{
			throw new ArgumentOutOfRangeException("tileIdx");
		}
		if (data.IsEmpty || data.Length != tagMetadata.NumberCellsPerTile * tagMetadata.SamplesPerPixel)
		{
			throw new ArgumentException("Data should have " + tagMetadata.NumberCellsPerTile + "x" + tagMetadata.SamplesPerPixel + " elements.");
		}
		IMemoryOwner<byte> cleanup = null;
		Span<T> bufferSpan = data.Span;
		Memory<byte> buffer;
		fixed (T* localValue = bufferSpan)
		{
			byte* pointer = (byte*)localValue;
			int ordinal = bufferSpan.Length * sizeof(T);
			using UnmanagedMemoryStream dataStream = new UnmanagedMemoryStream(pointer, ordinal);
			CompressTileZLib(dataStream, _lvl, out buffer, out cleanup);
		}
		QueueWrite(dirIdx, tileIdx, buffer, cleanup);
	}

	private void QueueWrite(int dirIdx, int tileIdx, Memory<byte> deflatedData, IMemoryOwner<byte> owner)
	{
		_queuedData.Enqueue(new QueuedTile(dirIdx, tileIdx, owner, deflatedData));
		CheckFlush();
	}

	public void GetTile<T>(int dirIdx, int tIdx, Memory<T> data) where T : unmanaged
	{
		if (!TryGetTile(dirIdx, tIdx, data))
		{
			throw new Exception("Attempting to read a tile that has not yet been written.");
		}
	}

	public bool TryGetTile<T>(int dirIdx, int tIdx, Memory<T> data) where T : unmanaged
	{
		long position2 = -1L;
		long unity = -1L;
		lock (_writeLock)
		{
			position2 = _tileByteLocations[dirIdx][tIdx];
			unity = _tileByteCounts[dirIdx][tIdx];
		}
		if (position2 <= 0 || unity <= 0)
		{
			CheckFlush(force: true);
			lock (_writeLock)
			{
				position2 = _tileByteLocations[dirIdx][tIdx];
				unity = _tileByteCounts[dirIdx][tIdx];
			}
			if (position2 <= 0 || unity <= 0)
			{
				return false;
			}
		}
		Memory<byte> mem;
		using (SharedMemoryPool<byte>.RentNoFill((int)unity, out mem))
		{
			lock (_writeLock)
			{
				long position = _fs.Position;
				_fs.Position = position2;
				if (_fs.Read(mem.Span) != unity)
				{
					throw new Exception("Tiff File corrupted - reached EOF prematurely.");
				}
				_fs.Position = position;
			}
			CompressionHelper.Decompress(mem.Span, data);
			return true;
		}
	}

	private void CheckFlush(bool force = false)
	{
		int count = _queuedData.Count;
		if (count == 0 || (count < MaxTileQueueDepth && !force))
		{
			return;
		}
		lock (_writeLock)
		{
			count = _queuedData.Count;
			if (count == 0 || (count < MaxTileQueueDepth && !force))
			{
				return;
			}
			QueuedTile result;
			while (_queuedData.TryDequeue(out result))
			{
				long tileByteCount = _tileByteCounts[result.DirectoryIndex][result.TileIndex];
				if (tileByteCount > 0)
				{
					_logger.LogDebug("Multiple tile writes leaving dead space in tile {directory}/{tile}: {bytes} bytes.", result.DirectoryIndex, result.TileIndex, tileByteCount);
					_multiWriteBytesWasted += tileByteCount;
				}
				_tileByteCounts[result.DirectoryIndex][result.TileIndex] = result.Buffer.Length;
				_tileByteLocations[result.DirectoryIndex][result.TileIndex] = _fs.Length;
				_fs.Write(result.Buffer.Span);
				result.Owner.Dispose();
			}
		}
	}

	private static void CompressTileZLib(Stream dataStream, CompressionLevel level, out Memory<byte> buffer, out IMemoryOwner<byte> cleanup)
	{
		using SharedMemoryStream sharedMemoryStream = new SharedMemoryStream();
		using ZLibStream zLibStream = new ZLibStream(sharedMemoryStream, level, leaveOpen: true);
		dataStream.CopyTo(zLibStream);
		zLibStream.Close();
		sharedMemoryStream.TakeOwnership(out cleanup, out buffer);
	}

	public bool TileIsWritten(int dirIdx, int tileIdx)
	{
		return _tileByteLocations[dirIdx][tileIdx] > 0;
	}

	protected virtual void Dispose(bool disposing)
	{
		if (_isDisposed)
		{
			return;
		}
		if (disposing)
		{
			CheckFlush(force: true);
			for (int i = 0; i < _dirDefs.Count; i++)
			{
				long byteOffset = 0L;
				long byteCount = 0L;
				for (int j = 0; j < _tileByteLocations[i].Length; j++)
				{
					if (!TileIsWritten(i, j))
					{
						if (byteOffset == 0L)
						{
							Memory<byte> uncompressedNoDataTile = _dirDefs[i].GetUncompressedNoDataTile();
							WriteTileDirect(uncompressedNoDataTile, out byteOffset, out byteCount);
						}
						_tileByteLocations[i][j] = byteOffset;
						_tileByteCounts[i][j] = byteCount;
					}
				}
			}
			for (int k = 0; k < DirectoryCount; k++)
			{
				long offset = _dirDefs[k].AppendDirectory(_fs, null, _tileByteLocations[k], _tileByteCounts[k]);
				if (k == DirectoryCount - 1)
				{
					break;
				}
				long length = _fs.Length;
				_fs.Seek(offset, SeekOrigin.Begin);
				byte[] bytes = BitConverter.GetBytes(length);
				_fs.Write(bytes, 0, bytes.Length);
				_fs.Seek(0L, SeekOrigin.End);
			}
			_fs.Position = 8L;
			byte[] bytes2 = BitConverter.GetBytes((ulong)_dirDefs.First().DirectoryOffset);
			_fs.Write(bytes2, 0, bytes2.Length);
			if (_cleanFs)
			{
				_fs.Dispose();
			}
			_fsReaderPool?.ClearAll((Stream x) =>
			{
				x.Dispose();
			});
			_dirDefs = null;
			_tileByteCounts = null;
			_tileByteLocations = null;
		}
		_isDisposed = true;
	}

	public void Dispose()
	{
		Dispose(disposing: true);
	}
}
