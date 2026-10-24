using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Utility.Logging;

public abstract class RollingFileSinkBase : LogSinkBase
{
	private const string ArchiveTimestampFormat = "yyyyMMdd_HHmmss";

	private readonly RollingFileSinkOptions _options;

	private readonly byte[] _buffer;

	private readonly int _autoFlushBytes;

	private readonly long _reopenIntervalMs;

	private FileStream _stream;

	private string _activePath;

	private long _fileLength;

	private int _bufferPosition;

	private long _nextOpenAttempt;

	private long _rollBlockedUntil;

	private bool _rolledOnStartup;

	private bool _disposed;

	public string ActivePath => _activePath ?? _options.Path;

	protected RollingFileSinkBase(RollingFileSinkOptions options)
	{
		_options = options ?? throw new ArgumentNullException("options");
		if (string.IsNullOrWhiteSpace(_options.Path))
		{
			throw new ArgumentException("A log file path is required.", "options");
		}
		_buffer = new byte[Math.Max(4096, _options.BufferBytes)];
		_autoFlushBytes = _buffer.Length / 2;
		_reopenIntervalMs = (long)Math.Max(0.0, _options.ReopenInterval.TotalMilliseconds);
		MinimumLevel = _options.MinimumLevel;
	}

	protected override void OnBatchWritten()
	{
		if (_bufferPosition >= _autoFlushBytes)
		{
			WriteBufferToFile();
		}
	}

	public override void Flush()
	{
		WriteBufferToFile();
		_stream?.Flush();
	}

	public override void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		try
		{
			WriteBufferToFile();
		}
		finally
		{
			_stream?.Dispose();
			_stream = null;
		}
	}

	protected void Append(ReadOnlySpan<char> charSpan)
	{
		if (charSpan.IsEmpty)
		{
			return;
		}
		int maxByteCount = Encoding.UTF8.GetMaxByteCount(charSpan.Length);
		if (maxByteCount > _buffer.Length)
		{
			AppendOversized(charSpan);
			return;
		}
		if (maxByteCount > _buffer.Length - _bufferPosition)
		{
			WriteBufferToFile();
		}
		_bufferPosition += Encoding.UTF8.GetBytes(charSpan, _buffer.AsSpan(_bufferPosition));
	}

	protected void AppendBytes(ReadOnlySpan<byte> bytes)
	{
		if (bytes.IsEmpty)
		{
			return;
		}
		if (bytes.Length > _buffer.Length)
		{
			WriteBufferToFile();
			WriteToFile(bytes);
			return;
		}
		if (bytes.Length > _buffer.Length - _bufferPosition)
		{
			WriteBufferToFile();
		}
		bytes.CopyTo(_buffer.AsSpan(_bufferPosition));
		_bufferPosition += bytes.Length;
	}

	private void AppendOversized(ReadOnlySpan<char> localValue)
	{
		WriteBufferToFile();
		int maxByteCount = Encoding.UTF8.GetMaxByteCount(localValue.Length);
		byte[] rent = ArrayPool<byte>.Shared.Rent(maxByteCount);
		try
		{
			int bytes = Encoding.UTF8.GetBytes(localValue, rent);
			WriteToFile(rent.AsSpan(0, bytes));
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(rent);
		}
	}

	private void WriteBufferToFile()
	{
		if (_bufferPosition != 0)
		{
			int bufferPosition = _bufferPosition;
			_bufferPosition = 0;
			WriteToFile(_buffer.AsSpan(0, bufferPosition));
		}
	}

	private void WriteToFile(ReadOnlySpan<byte> bytes)
	{
		if (!bytes.IsEmpty && EnsureOpen())
		{
			_stream.Write(bytes);
			_fileLength += bytes.Length;
			if (_options.MaxFileSizeBytes > 0 && _fileLength >= _options.MaxFileSizeBytes)
			{
				Roll();
			}
		}
	}

	private bool EnsureOpen()
	{
		if (_stream != null)
		{
			return true;
		}
		if (_disposed)
		{
			return false;
		}
		if (Environment.TickCount64 < _nextOpenAttempt)
		{
			return false;
		}
		_nextOpenAttempt = Environment.TickCount64 + _reopenIntervalMs;
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_options.Path)));
		}
		catch (Exception ex)
		{
			LogDispatcher.SelfLog?.Invoke("Could not create log directory for '" + _options.Path + "': " + ex.Message);
			return false;
		}
		if (_options.RollOnStartup && !_rolledOnStartup)
		{
			_rolledOnStartup = true;
			ArchiveExisting(_options.Path);
		}
		FileShare share = ((!_options.SharedAccess) ? FileShare.Read : (FileShare.ReadWrite | FileShare.Delete));
		for (int i = 0; i <= Math.Max(0, _options.MaxAlternatePaths); i++)
		{
			string appendIds = ((i == 0) ? _options.Path : AlternatePath(_options.Path, i));
			try
			{
				_stream = new FileStream(appendIds, FileMode.Append, FileAccess.Write, share, 1, FileOptions.None);
				_activePath = appendIds;
				_fileLength = _stream.Length;
				return true;
			}
			catch (Exception ex2) when (ex2 is IOException || ex2 is UnauthorizedAccessException)
			{
				if (i == Math.Max(0, _options.MaxAlternatePaths))
				{
					LogDispatcher.SelfLog?.Invoke("Could not open log file '" + appendIds + "': " + ex2.Message);
				}
			}
		}
		return false;
	}

	private void Roll()
	{
		if (Environment.TickCount64 >= _rollBlockedUntil)
		{
			string activePath = _activePath;
			_stream?.Flush();
			_stream?.Dispose();
			_stream = null;
			if (!ArchiveExisting(activePath))
			{
				_rollBlockedUntil = Environment.TickCount64 + _reopenIntervalMs;
				_nextOpenAttempt = 0L;
				EnsureOpen();
			}
			else
			{
				_nextOpenAttempt = 0L;
				_fileLength = 0L;
				EnsureOpen();
			}
		}
	}

	private bool ArchiveExisting(string path)
	{
		if (!File.Exists(path))
		{
			return true;
		}
		string destFileName = ArchivePath(path);
		try
		{
			File.Move(path, destFileName);
		}
		catch (Exception ex)
		{
			LogDispatcher.SelfLog?.Invoke("Could not archive log file '" + path + "': " + ex.Message);
			return false;
		}
		PruneArchives(path);
		return true;
	}

	private static string ArchivePath(string path)
	{
		string directoryName = Path.GetDirectoryName(Path.GetFullPath(path));
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
		string extension = Path.GetExtension(path);
		string now = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
		string path2 = Path.Combine(directoryName, fileNameWithoutExtension + "_" + now + extension);
		int unity = 1;
		while (File.Exists(path2) && unity < 1000)
		{
			path2 = Path.Combine(directoryName, $"{fileNameWithoutExtension}_{now}_{unity:D3}{extension}");
			unity++;
		}
		return path2;
	}

	private static string AlternatePath(string path, int index)
	{
		string? directoryName = Path.GetDirectoryName(Path.GetFullPath(path));
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
		string extension = Path.GetExtension(path);
		return Path.Combine(directoryName, $"{fileNameWithoutExtension}-alt{index}{extension}");
	}

	private void PruneArchives(string path)
	{
		if (_options.MaxRetainedFiles <= 0)
		{
			return;
		}
		try
		{
			string? directoryName = Path.GetDirectoryName(Path.GetFullPath(path));
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
			string extension = Path.GetExtension(path);
			List<string> files = new List<string>(Directory.GetFiles(directoryName, fileNameWithoutExtension + "_*" + extension));
			files.Sort(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < files.Count - _options.MaxRetainedFiles; i++)
			{
				File.Delete(files[i]);
			}
		}
		catch (Exception ex)
		{
			LogDispatcher.SelfLog?.Invoke("Could not prune log archives for '" + path + "': " + ex.Message);
		}
	}
}
