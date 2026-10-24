using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Utility.Logging;

public sealed class LogDispatcher : IDisposable
{
	private const string OriginalFormatKey = "{OriginalFormat}";

	private const string ScopePropertyName = "Scope";

	private const int RenderStackChars = 512;

	private const int IdleWaitMs = 250;

	private const int SignalSpinCount = 200;

	private readonly FastLoggerOptions _options;

	private readonly LogRingBuffer<LogRecord> _queue;

	private readonly ILogSink[] _sinks;

	private readonly int[] _sinkFailures;

	private readonly long[] _sinkRetryAt;

	private readonly LogRecord[] _batch;

	private readonly Thread _thread;

	private readonly ManualResetEventSlim _signal = new ManualResetEventSlim(initialState: false, 200);

	private readonly object _flushLock = new object();

	private readonly bool _captureProperties;

	private readonly bool _captureScopes;

	private readonly LogLevel _flushOnLevel;

	private readonly int _flushIntervalMs;

	private readonly long _blockTimeoutMs;

	private readonly long _sinkRetryMs;

	private long _sequence;

	private long _written;

	private long _dropped;

	private long _reportedDrops;

	private long _flushRequested;

	private long _flushCompleted;

	private int _parked;

	private volatile bool _stopping;

	private int _disposed;

	public static Action<string> SelfLog { get; set; }

	public long WrittenCount => Interlocked.Read(in _written);

	public long DroppedCount => Interlocked.Read(in _dropped);

	public int QueueDepth => _queue.Count;

	public int QueueCapacity => _queue.Capacity;

	public LogDispatcher(FastLoggerOptions options)
	{
		_options = options ?? new FastLoggerOptions();
		_sinks = _options.Sinks.ToArray();
		_sinkFailures = new int[_sinks.Length];
		_sinkRetryAt = new long[_sinks.Length];
		_queue = new LogRingBuffer<LogRecord>(_options.QueueCapacity);
		_batch = new LogRecord[Math.Max(1, _options.BatchSize)];
		_flushOnLevel = _options.FlushOnLevel;
		_flushIntervalMs = (int)Math.Max(1.0, _options.FlushInterval.TotalMilliseconds);
		_blockTimeoutMs = (long)Math.Max(0.0, _options.BlockTimeout.TotalMilliseconds);
		_sinkRetryMs = (long)Math.Max(0.0, _options.SinkRetryInterval.TotalMilliseconds);
		_captureScopes = _options.CaptureScopes;
		ILogSink[] sinks = _sinks;
		for (int i = 0; i < sinks.Length; i++)
		{
			if (sinks[i].WantsProperties)
			{
				_captureProperties = true;
			}
		}
		_thread = new Thread(WriterLoop)
		{
			Name = "LogDispatcher",
			IsBackground = true,
			Priority = _options.WriterPriority
		};
		_thread.Start();
	}

	public bool IsEnabled(LogLevel level)
	{
		if (level == LogLevel.None || _stopping)
		{
			return false;
		}
		for (int i = 0; i < _sinks.Length; i++)
		{
			if (level >= _sinks[i].MinimumLevel)
			{
				return true;
			}
		}
		return false;
	}

	public IDisposable BeginScope(object state)
	{
		if (!_captureScopes || state == null)
		{
			return NullScope.Instance;
		}
		return LogScope.Push(state);
	}

	public void Write<TState>(string category, LogLevel level, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
	{
		if (_stopping)
		{
			return;
		}
		char[] utcNowIds = null;
		int utcNowIds2 = 0;
		LogProperty[] utcNowIdsArray = null;
		int captured = 0;
		string utcNowIds3 = null;
		Span<char> initialBuffer = stackalloc char[512];
		ValueStringBuilder builder = new ValueStringBuilder(initialBuffer);
		try
		{
			if (state is IReadOnlyList<KeyValuePair<string, object>> { Count: var count } readOnlyList)
			{
				int count2 = count;
				if (count > 0 && readOnlyList[count - 1].Key == "{OriginalFormat}")
				{
					utcNowIds3 = readOnlyList[count - 1].Value as string;
					count2 = count - 1;
				}
				if (utcNowIds3 != null)
				{
					LogTemplate.Get(utcNowIds3).Render(ref builder, readOnlyList, count2);
				}
				else if (formatter != null)
				{
					builder.Append(formatter(state, exception));
				}
				else
				{
					builder.Append(state.ToString());
				}
				if (_captureProperties)
				{
					utcNowIdsArray = CaptureProperties(readOnlyList, count2, out captured);
				}
			}
			else if (formatter != null)
			{
				builder.Append(formatter(state, exception));
			}
			else if (state != null)
			{
				builder.Append(state.ToString());
			}
			if (_captureProperties && _captureScopes)
			{
				utcNowIdsArray = CaptureScopeProperties(utcNowIdsArray, ref captured);
			}
			utcNowIds2 = builder.Length;
			if (utcNowIds2 > 0)
			{
				utcNowIds = LogBufferPool.Rent(utcNowIds2);
				builder.AsSpan().CopyTo(utcNowIds);
			}
		}
		catch (Exception ex)
		{
			if (utcNowIds != null)
			{
				LogBufferPool.Return(utcNowIds);
			}
			if (utcNowIdsArray != null)
			{
				ArrayPool<LogProperty>.Shared.Return(utcNowIdsArray, clearArray: true);
			}
			Report("Failed to render a log message: " + ex.Message);
			return;
		}
		finally
		{
			builder.Dispose();
		}
		LogRecord record = new LogRecord(Interlocked.Increment(ref _sequence), DateTime.UtcNow, level, eventId, category, utcNowIds3, exception, Environment.CurrentManagedThreadId, utcNowIds, utcNowIds2, utcNowIdsArray, captured);
		if (!Enqueue(in record))
		{
			record.Release();
			Interlocked.Increment(ref _dropped);
		}
	}

	public bool Flush(TimeSpan timeout)
	{
		if (Volatile.Read(in _disposed) != 0)
		{
			return false;
		}
		long increment = Interlocked.Increment(ref _flushRequested);
		Signal();
		long tickCount64EnvironmentTerm = Environment.TickCount64 + (long)Math.Max(0.0, timeout.TotalMilliseconds);
		lock (_flushLock)
		{
			while (Interlocked.Read(in _flushCompleted) < increment)
			{
				int tickCount64EnvironmentTerm2 = (int)Math.Min(2147483647L, tickCount64EnvironmentTerm - Environment.TickCount64);
				if (tickCount64EnvironmentTerm2 <= 0)
				{
					return false;
				}
				Monitor.Wait(_flushLock, tickCount64EnvironmentTerm2);
			}
		}
		return true;
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}
		_stopping = true;
		_signal.Set();
		if (!_thread.Join(_options.ShutdownTimeout))
		{
			Report("Log dispatcher did not drain within the shutdown timeout; sinks left open.");
			return;
		}
		ILogSink[] sinks = _sinks;
		foreach (ILogSink logSink in sinks)
		{
			try
			{
				logSink.Flush();
				logSink.Dispose();
			}
			catch (Exception ex)
			{
				Report("Sink " + logSink.GetType().Name + " failed to close: " + ex.Message);
			}
		}
	}

	private bool Enqueue(in LogRecord record)
	{
		if (_queue.TryEnqueue(in record))
		{
			Signal();
			return true;
		}
		if (_options.OverflowPolicy != LogOverflowPolicy.Block)
		{
			return false;
		}
		Signal();
		SpinWait spinWait = default;
		long tickCount64PlusBlockTimeoutMs = Environment.TickCount64 + _blockTimeoutMs;
		while (Environment.TickCount64 <= tickCount64PlusBlockTimeoutMs)
		{
			spinWait.SpinOnce();
			if (_queue.TryEnqueue(in record))
			{
				Signal();
				return true;
			}
		}
		return false;
	}

	private void Signal()
	{
		if (Volatile.Read(in _parked) != 0)
		{
			_signal.Set();
		}
	}

	private static LogProperty[] CaptureProperties(IReadOnlyList<KeyValuePair<string, object>> pairs, int count, out int captured)
	{
		captured = 0;
		if (count <= 0)
		{
			return null;
		}
		LogProperty[] rent = ArrayPool<LogProperty>.Shared.Rent(count);
		for (int i = 0; i < count; i++)
		{
			KeyValuePair<string, object> pair = pairs[i];
			rent[i] = new LogProperty(pair.Key, Snapshot(pair.Value));
		}
		captured = count;
		return rent;
	}

	private static LogProperty[] CaptureScopeProperties(LogProperty[] properties, ref int count)
	{
		for (LogScope logScope = LogScope.Current; logScope != null; logScope = logScope.Parent)
		{
			if (logScope.State is IReadOnlyList<KeyValuePair<string, object>> readOnlyList)
			{
				for (int i = 0; i < readOnlyList.Count; i++)
				{
					KeyValuePair<string, object> readOnlyList2 = readOnlyList[i];
					if (!(readOnlyList2.Key == "{OriginalFormat}"))
					{
						Add(ref properties, ref count, new LogProperty(readOnlyList2.Key, Snapshot(readOnlyList2.Value)));
					}
				}
			}
			else
			{
				Add(ref properties, ref count, new LogProperty("Scope", Snapshot(logScope.State)));
			}
		}
		return properties;
	}

	private static void Add(ref LogProperty[] properties, ref int count, LogProperty property)
	{
		if (properties == null)
		{
			properties = ArrayPool<LogProperty>.Shared.Rent(8);
		}
		else if (count == properties.Length)
		{
			LogProperty[] propertiesArray = ArrayPool<LogProperty>.Shared.Rent(count * 2);
			Array.Copy(properties, propertiesArray, count);
			ArrayPool<LogProperty>.Shared.Return(properties, clearArray: true);
			properties = propertiesArray;
		}
		properties[count++] = property;
	}

	private static object Snapshot(object localValue)
	{
		if (localValue == null || localValue is string || localValue is ValueType)
		{
			return localValue;
		}
		try
		{
			return localValue.ToString();
		}
		catch
		{
			return null;
		}
	}

	private void WriterLoop()
	{
		long tickCount = Environment.TickCount64;
		while (true)
		{
			long read = Volatile.Read(in _flushRequested);
			if (DrainAndDispatch() <= 0)
			{
				ReportDrops();
				if (read > Interlocked.Read(in _flushCompleted))
				{
					FlushSinks();
					CompleteFlush(read);
					tickCount = Environment.TickCount64;
				}
				else if (Environment.TickCount64 - tickCount >= _flushIntervalMs)
				{
					FlushSinks();
					tickCount = Environment.TickCount64;
				}
				if (_stopping)
				{
					break;
				}
				_signal.Reset();
				Interlocked.Exchange(ref _parked, 1);
				if (_queue.IsEmpty && Volatile.Read(in _flushRequested) == read)
				{
					_signal.Wait(250);
				}
				Volatile.Write(ref _parked, 0);
			}
		}
		while (DrainAndDispatch() > 0)
		{
		}
		ReportDrops();
		FlushSinks();
		CompleteFlush(Volatile.Read(in _flushRequested));
	}

	private int DrainAndDispatch()
	{
		LogRecord[] batch = _batch;
		int i = 0;
		bool condition = false;
		for (; i < batch.Length && _queue.TryDequeue(out batch[i]); i++)
		{
			if (batch[i].Level >= _flushOnLevel)
			{
				condition = true;
			}
		}
		if (i == 0)
		{
			return 0;
		}
		Dispatch(batch, i);
		for (int j = 0; j < i; j++)
		{
			batch[j].Release();
			batch[j] = default;
		}
		Interlocked.Add(ref _written, i);
		if (condition)
		{
			FlushSinks();
		}
		return i;
	}

	private void Dispatch(LogRecord[] batch, int count)
	{
		ReadOnlySpan<LogRecord> batch2 = batch.AsSpan(0, count);
		for (int i = 0; i < _sinks.Length; i++)
		{
			if (_sinkFailures[i] < _options.MaxSinkFailures || Environment.TickCount64 >= _sinkRetryAt[i])
			{
				try
				{
					_sinks[i].Emit(batch2);
					_sinkFailures[i] = 0;
				}
				catch (Exception ex)
				{
					OnSinkFailure(i, ex);
				}
			}
		}
	}

	private void FlushSinks()
	{
		for (int i = 0; i < _sinks.Length; i++)
		{
			if (_sinkFailures[i] < _options.MaxSinkFailures || Environment.TickCount64 >= _sinkRetryAt[i])
			{
				try
				{
					_sinks[i].Flush();
				}
				catch (Exception ex)
				{
					OnSinkFailure(i, ex);
				}
			}
		}
	}

	private void OnSinkFailure(int index, Exception ex)
	{
		_sinkFailures[index]++;
		_sinkRetryAt[index] = Environment.TickCount64 + _sinkRetryMs;
		if (_sinkFailures[index] == _options.MaxSinkFailures)
		{
			Report($"Sink {_sinks[index].GetType().Name} parked after {_sinkFailures[index]} failures: {ex.Message}");
		}
		else if (_sinkFailures[index] < _options.MaxSinkFailures)
		{
			Report("Sink " + _sinks[index].GetType().Name + " failed: " + ex.Message);
		}
	}

	private void CompleteFlush(long ticket)
	{
		lock (_flushLock)
		{
			if (Interlocked.Read(in _flushCompleted) < ticket)
			{
				Interlocked.Exchange(ref _flushCompleted, ticket);
			}
			Monitor.PulseAll(_flushLock);
		}
	}

	private void ReportDrops()
	{
		long reportedDrops = Interlocked.Read(in _dropped);
		if (reportedDrops != _reportedDrops)
		{
			long reportedDropsDifference = reportedDrops - _reportedDrops;
			_reportedDrops = reportedDrops;
			EmitInternal(LogLevel.Warning, $"Log queue full: {reportedDropsDifference} events dropped ({reportedDrops} total).");
		}
	}

	private void EmitInternal(LogLevel level, string message)
	{
		char[] utcNowIds = LogBufferPool.Rent(message.Length);
		((ReadOnlySpan<char>)message).CopyTo(utcNowIds);
		LogRecord logRecord = new LogRecord(Interlocked.Increment(ref _sequence), DateTime.UtcNow, level, default, "LogDispatcher", null, null, Environment.CurrentManagedThreadId, utcNowIds, message.Length, null, 0);
		_batch[0] = logRecord;
		try
		{
			Dispatch(_batch, 1);
		}
		finally
		{
			_batch[0] = default;
			logRecord.Release();
		}
	}

	private static void Report(string message)
	{
		try
		{
			SelfLog?.Invoke(message);
		}
		catch
		{
		}
	}
}
