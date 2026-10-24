using System;
using System.Diagnostics;
using H5Assist.Identifiers;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Memory;

namespace H5Assist.Caching;

public class H5StreamCache<T> : IDisposable where T : unmanaged
{
	private readonly ILogger _logger = LogHelper.GetLogger("H5StreamCache");

	private readonly H5Writer _writer;

	private H5DatasetID _dset;

	private readonly ArrayCacheManager<int, T> _chunkCache = new ArrayCacheManager<int, T>();

	private readonly int _nrows;

	private readonly int _ncols;

	private readonly int _nRowsPerChunk;

	private readonly int _nElementsPerChunk;

	private readonly int _nChunks;

	private readonly Memory<T>[] _modifiedChunks;

	private readonly int[] _rowsWritten;

	public const int NeverSeen = 0;

	public const int InModifiedCache = 1;

	public const int WrittenToDisk = 2;

	public H5StreamCache(H5Writer writer, string dataset, int nRowsPerChunk, int nRows, int nColumns)
	{
		_writer = writer;
		_dset = writer.CreateAndOpen2dExtendibleDataset<T>(dataset, nRowsPerChunk, nColumns);
		_writer.SetExtent(_dset, nRows, nColumns);
		_nRowsPerChunk = nRowsPerChunk;
		_nrows = nRows;
		_ncols = nColumns;
		_nChunks = nRows / nRowsPerChunk;
		_nElementsPerChunk = _nRowsPerChunk * _ncols;
		if (nRows % nRowsPerChunk > 0)
		{
			_nChunks++;
		}
		_modifiedChunks = new Memory<T>[_nChunks];
		_rowsWritten = new int[_nrows];
	}

	public void SetRow(int row, Memory<T> data)
	{
		int chunkIndex = ChunkIndex(row);
		Memory<T> modifiedChunk = _modifiedChunks[chunkIndex];
		if (modifiedChunk.IsEmpty)
		{
			modifiedChunk = new T[_nElementsPerChunk];
			_modifiedChunks[chunkIndex] = modifiedChunk;
		}
		if (_rowsWritten[row] != 0)
		{
			throw new NotSupportedException("Not yet allowing multiple row writes.");
		}
		data.CopyTo(SliceChunkGlobalRow(modifiedChunk, row));
		_rowsWritten[row] = 1;
		if (IsChunkReadyToWrite(chunkIndex))
		{
			CommitChunk(chunkIndex);
		}
	}

	private void CommitChunk(int chunkR)
	{
		Memory<T> chunk = _modifiedChunks[chunkR];
		_chunkCache.Touch(chunkR, _nElementsPerChunk, (Memory<T> mem) =>
		{
			chunk.CopyTo(mem);
		});
		WriteChunk(chunk, chunkR);
		int chunkRTimesNRowsPerChunk = chunkR * _nRowsPerChunk;
		for (int chunkRTimesNRowsPerChunk2 = chunkRTimesNRowsPerChunk; chunkRTimesNRowsPerChunk2 < chunkRTimesNRowsPerChunk + _nRowsPerChunk && chunkRTimesNRowsPerChunk2 < _nrows; chunkRTimesNRowsPerChunk2++)
		{
			_rowsWritten[chunkRTimesNRowsPerChunk2] = 2;
		}
		_modifiedChunks[chunkR] = Memory<T>.Empty;
	}

	private void WriteChunk(Memory<T> data, int chunkIdx)
	{
		int[] chunkIndex = new int[2] { chunkIdx, 0 };
		_writer.WriteChunkDirect(_dset, chunkIndex, data);
		_logger.LogDebug("{Dataset}: Chunk {ChunkIndex} flushed to disk.", _dset.DatasetName, chunkIdx);
	}

	private bool IsChunkReadyToWrite(int chunkIdx)
	{
		int chunkIdxTimesNRowsPerChunk = chunkIdx * _nRowsPerChunk;
		for (int i = chunkIdxTimesNRowsPerChunk; i < chunkIdxTimesNRowsPerChunk + _nRowsPerChunk && i < _nrows; i++)
		{
			if (_rowsWritten[i] != 1)
			{
				return false;
			}
		}
		return true;
	}

	public void GetRow(int row, Memory<T> outBuffer)
	{
		if (row >= _nrows)
		{
			throw new ArgumentOutOfRangeException("row");
		}
		switch (_rowsWritten[row])
		{
		case 0:
			throw new Exception("Row not written yet.");
		case 1:
		{
			int chunkIndex2 = ChunkIndex(row);
			Memory<T> chunkBuf = _modifiedChunks[chunkIndex2];
			SliceChunkGlobalRow(chunkBuf, row).CopyTo(outBuffer);
			return;
		}
		}
		int chunkR = ChunkIndex(row);
		int startRow = chunkR * _nRowsPerChunk;
		_chunkCache.BuildOrUseExisting(chunkR, _nElementsPerChunk, (Memory<T> buf) =>
		{
			int[] chunkIndex = new int[2] { chunkR, 0 };
			Stopwatch stopwatch = Stopwatch.StartNew();
			_writer.ReadChunkDirect(_dset, chunkIndex, buf);
			stopwatch.Stop();
			_logger.LogDebug("{Dataset} Chunk {ChunkIndex} read in {Duration}.", _dset.DatasetName, chunkR, stopwatch.Elapsed);
		}, (Memory<T> buffer) =>
		{
			int start = (row - startRow) * _ncols;
			buffer.Slice(start, _ncols).CopyTo(outBuffer);
		});
	}

	private int ChunkIndex(int rowIdx)
	{
		return (int)Math.Floor((double)rowIdx / (double)_nRowsPerChunk);
	}

	private static bool IsOOB(long[] dsetDims, int rowIdx)
	{
		if (rowIdx < dsetDims[0])
		{
			return rowIdx < 0;
		}
		return true;
	}

	private Memory<T> SliceChunkLocalRow(Memory<T> chunkBuf, int row)
	{
		int start = row * _ncols;
		return chunkBuf.Slice(start, _ncols);
	}

	private Memory<T> SliceChunkGlobalRow(Memory<T> chunkBuf, int row)
	{
		int chunkIndexRowTerm = ChunkIndex(row) * _nRowsPerChunk;
		return SliceChunkLocalRow(chunkBuf, row - chunkIndexRowTerm);
	}

	private int LocalChunkRow(int row, out int chunkIdx)
	{
		chunkIdx = ChunkIndex(row);
		int chunkIdxTimesNRowsPerChunk = chunkIdx * _nRowsPerChunk;
		return row - chunkIdxTimesNRowsPerChunk;
	}

	public void Dispose()
	{
		for (int i = 0; i < _nChunks; i++)
		{
			if (IsChunkReadyToWrite(i))
			{
				CommitChunk(i);
			}
		}
		_dset.Dispose();
	}

	public void Flush()
	{
		for (int i = 0; i < _nChunks; i++)
		{
			if (IsChunkReadyToWrite(i))
			{
				CommitChunk(i);
			}
		}
		_writer.Flush();
	}
}
