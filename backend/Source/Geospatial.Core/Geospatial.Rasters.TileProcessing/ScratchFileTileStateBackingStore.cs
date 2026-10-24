using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Geospatial.Rasters.TileProcessing;

public sealed class ScratchFileTileStateBackingStore : ITileStateBackingStore, IDisposable
{
	private readonly record struct Slot(long Offset, int Length);

	private static readonly ILogger _logger = LogHelper.GetLogger("ScratchFileTileStateBackingStore");

	private readonly string _path;

	private readonly bool _ownsFile;

	private readonly FileStream _file;

	private readonly Dictionary<long, Slot> _index = new Dictionary<long, Slot>();

	public string FilePath => _path;

	public int TileCount => _index.Count;

	public long FileLength => _file.Length;

	public ScratchFileTileStateBackingStore(string path = null)
	{
		_ownsFile = string.IsNullOrEmpty(path);
		_path = (_ownsFile ? Path.Combine(Path.GetTempPath(), $"ras-tilestate-{Guid.NewGuid():N}.scratch") : path);
		_file = new FileStream(_path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.RandomAccess);
	}

	public void Write(long tileIndex, ReadOnlySpan<byte> data)
	{
		long offset = _file.Seek(0L, SeekOrigin.End);
		_file.Write(data);
		_index[tileIndex] = new Slot(offset, data.Length);
	}

	public bool TryRead(long tileIndex, out byte[] data)
	{
		if (!_index.TryGetValue(tileIndex, out var localValue))
		{
			data = null;
			return false;
		}
		data = new byte[localValue.Length];
		_file.Seek(localValue.Offset, SeekOrigin.Begin);
		int read;
		for (int i = 0; i < localValue.Length; i += read)
		{
			read = _file.Read(data, i, localValue.Length - i);
			if (read <= 0)
			{
				throw new EndOfStreamException($"Truncated scratch state for tile {tileIndex}.");
			}
		}
		return true;
	}

	public bool Contains(long tileIndex)
	{
		return _index.ContainsKey(tileIndex);
	}

	public void Remove(long tileIndex)
	{
		_index.Remove(tileIndex);
	}

	public void Dispose()
	{
		try
		{
			_file.Dispose();
		}
		finally
		{
			if (_ownsFile && File.Exists(_path))
			{
				try
				{
					File.Delete(_path);
				}
				catch (Exception exception)
				{
					_logger.LogWarning(exception, "Failed to delete scratch state file {Path}", _path);
				}
			}
		}
	}
}
