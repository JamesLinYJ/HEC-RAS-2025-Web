using System;
using Microsoft.Extensions.Logging;

namespace Utility.Logging;

public abstract class LogSinkBase : ILogSink, IDisposable
{
	private volatile int _minimumLevel;

	public LogLevel MinimumLevel
	{
		get
		{
			return (LogLevel)_minimumLevel;
		}
		set
		{
			_minimumLevel = (int)value;
		}
	}

	public virtual bool WantsProperties => false;

	public void Emit(ReadOnlySpan<LogRecord> batch)
	{
		LogLevel minimumLevel = MinimumLevel;
		bool condition = false;
		ReadOnlySpan<LogRecord> batchSpan = batch;
		for (int i = 0; i < batchSpan.Length; i++)
		{
			ref readonly LogRecord reference = ref batchSpan[i];
			if (reference.Level >= minimumLevel)
			{
				Write(in reference);
				condition = true;
			}
		}
		if (condition)
		{
			OnBatchWritten();
		}
	}

	protected abstract void Write(in LogRecord record);

	protected virtual void OnBatchWritten()
	{
	}

	public virtual void Flush()
	{
	}

	public virtual void Dispose()
	{
	}
}
