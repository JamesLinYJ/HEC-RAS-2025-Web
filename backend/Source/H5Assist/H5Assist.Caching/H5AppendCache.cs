using System;
using System.Diagnostics;
using H5Assist.Identifiers;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Memory;

namespace H5Assist.Caching;

public class H5AppendCache<T> : IDisposable where T : unmanaged
{
	private readonly ILogger _logger = LogHelper.GetLogger("H5AppendCache");

	private readonly H5Writer _writer;

	private readonly H5DatasetID _dset;

	private readonly ArrayCacheManager<int, T> _chunkCache = new ArrayCacheManager<int, T>();

	private readonly int _ncols;

	private readonly int _nRowsPerChunk;

	private readonly int _nElementsPerChunk;

	private readonly Memory<T> _inProgressChunk;

	private readonly object _lock = new object();

	private int _rowCount;

	public H5AppendCache(H5Writer writer, string dataset, int nRowsPerChunk, int nColumns)
	{
		_writer = writer;
		_dset = writer.CreateAndOpen2dExtendibleDataset<T>(dataset, nRowsPerChunk, nColumns);
		_nRowsPerChunk = nRowsPerChunk;
		_ncols = nColumns;
		_nElementsPerChunk = _nRowsPerChunk * _ncols;
		_inProgressChunk = new T[_nElementsPerChunk];
	}

	public void AddRow(Memory<T> data)
	{
		int chunkR = ChunkIndex(_rowCount);
		int row = _rowCount % _nRowsPerChunk;
		data.CopyTo(SliceChunkLocalRow(_inProgressChunk, row));
		if (IsChunkReadyToWrite())
		{
			lock (_lock)
			{
				CommitChunk(chunkR);
			}
		}
		_rowCount++;
	}

	private void EnsureDatasetExtents(int rowCount)
	{
		_writer.SetExtent(_dset, new long[2] { rowCount, _ncols });
		_logger.LogDebug("Setting {DatasetName} extents: [{Rows}, {Columns}]", _dset.DatasetName, rowCount, _ncols);
	}

	private void CommitChunk(int chunkR)
	{
		_chunkCache.Touch(chunkR, _nElementsPerChunk, (Memory<T> mem) =>
		{
			_inProgressChunk.CopyTo(mem);
		});
		EnsureDatasetExtents(_rowCount);
		int[] chunkIndex = new int[2] { chunkR, 0 };
		_writer.WriteChunkDirect(_dset, chunkIndex, _inProgressChunk);
		_logger.LogDebug("{Dataset}: Chunk {ChunkIndex} flushed to disk.", _dset.DatasetName, chunkR);
	}

	private bool IsChunkReadyToWrite()
	{
		return _rowCount % _nRowsPerChunk == _nRowsPerChunk - 1;
	}

	public void GetRow(int row, Memory<T> outBuffer)
	{
		if (row >= _rowCount)
		{
			throw new ArgumentOutOfRangeException("row");
		}
		int readChunk = ChunkIndex(row);
		int chunkIndex2 = ChunkIndex(_rowCount);
		int readChunkTimesNRowsPerChunk = readChunk * _nRowsPerChunk;
		int startEl = (row - readChunkTimesNRowsPerChunk) * _ncols;
		if (readChunk == chunkIndex2)
		{
			lock (_lock)
			{
				chunkIndex2 = ChunkIndex(_rowCount);
				if (readChunk == chunkIndex2)
				{
					_inProgressChunk.Slice(startEl, _ncols).CopyTo(outBuffer);
					return;
				}
				_logger.LogDebug("Cache fallthrough - AddRow appended while attempting to read this row.");
			}
		}
		_chunkCache.BuildOrUseExisting(readChunk, _nElementsPerChunk, (Memory<T> buf) =>
		{
			int[] chunkIndex = new int[2] { readChunk, 0 };
			Stopwatch stopwatch = Stopwatch.StartNew();
			_writer.ReadChunkDirect(_dset, chunkIndex, buf);
			stopwatch.Stop();
			_logger.LogDebug("{Dataset} Chunk {ChunkIndex} read in {Duration}.", _dset.DatasetName, readChunk, stopwatch.Elapsed);
		}, (Memory<T> chunkBuf) =>
		{
			chunkBuf.Slice(startEl, _ncols).CopyTo(outBuffer);
		});
	}

	private int ChunkIndex(int rowIdx)
	{
		return rowIdx / _nRowsPerChunk;
	}

	private Memory<T> SliceChunkLocalRow(Memory<T> chunkBuf, int row)
	{
		int start = row * _ncols;
		return chunkBuf.Slice(start, _ncols);
	}

	private int LocalChunkRow(int row, out int chunkIdx)
	{
		chunkIdx = ChunkIndex(row);
		int chunkIdxTimesNRowsPerChunk = chunkIdx * _nRowsPerChunk;
		return row - chunkIdxTimesNRowsPerChunk;
	}

	public void Dispose()
	{
		Flush();
		_dset.Dispose();
	}

	public void Flush()
	{
		int localChunkRow = LocalChunkRow(_rowCount, out var chunkIdx);
		if (localChunkRow > 0)
		{
			int start = localChunkRow * _ncols;
			_inProgressChunk.Slice(start).Span.Fill(default);
			CommitChunk(chunkIdx);
		}
		_writer.Flush();
	}
}
