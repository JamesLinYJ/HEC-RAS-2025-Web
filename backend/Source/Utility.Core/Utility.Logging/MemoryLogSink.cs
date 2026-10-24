using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Utility.Logging;

public sealed class MemoryLogSink : LogSinkBase
{
	private readonly RetainedLogEntry[] _entries;

	private readonly object _gate = new object();

	private int _next;

	private int _count;

	public int Count
	{
		get
		{
			lock (_gate)
			{
				return _count;
			}
		}
	}

	public MemoryLogSink(int capacity = 512, LogLevel minimumLevel = LogLevel.Trace)
	{
		_entries = new RetainedLogEntry[Math.Max(1, capacity)];
		MinimumLevel = minimumLevel;
	}

	protected override void Write(in LogRecord record)
	{
		RetainedLogEntry retainedLogEntry = new RetainedLogEntry(record.TimestampUtc, record.Level, record.Category, record.MessageToString(), record.Exception);
		lock (_gate)
		{
			_entries[_next] = retainedLogEntry;
			_next = (_next + 1) % _entries.Length;
			if (_count < _entries.Length)
			{
				_count++;
			}
		}
	}

	public List<RetainedLogEntry> Snapshot()
	{
		lock (_gate)
		{
			List<RetainedLogEntry> retainedLogEntries = new List<RetainedLogEntry>(_count);
			int nextCountTerm = (_next - _count + _entries.Length) % _entries.Length;
			for (int i = 0; i < _count; i++)
			{
				retainedLogEntries.Add(_entries[(nextCountTerm + i) % _entries.Length]);
			}
			return retainedLogEntries;
		}
	}

	public void Clear()
	{
		lock (_gate)
		{
			Array.Clear(_entries);
			_next = 0;
			_count = 0;
		}
	}
}
