using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public sealed class CodeTimer
{
	public enum ContextMode
	{
		AsyncLocal,
		ThreadStatic
	}

	private sealed class TimerStats
	{
		public readonly string Name;

		private long _wallSwTicks;

		private long _cpu100nsTicks;

		private long _count;

		private long _minWallSwTicks = long.MaxValue;

		private long _maxWallSwTicks;

		public long Count => Interlocked.Read(in _count);

		public long TotalWallSwTicks => Interlocked.Read(in _wallSwTicks);

		public long TotalCpu100nsTicks => Interlocked.Read(in _cpu100nsTicks);

		public long MinWallSwTicks => Volatile.Read(in _minWallSwTicks);

		public long MaxWallSwTicks => Volatile.Read(in _maxWallSwTicks);

		public TimerStats(string name)
		{
			Name = name;
		}

		public void AddSample(long wallSwTicks, long cpu100nsTicks, bool cpuEnabled)
		{
			Interlocked.Add(ref _wallSwTicks, wallSwTicks);
			if (cpuEnabled)
			{
				Interlocked.Add(ref _cpu100nsTicks, cpu100nsTicks);
			}
			Interlocked.Increment(ref _count);
			UpdateMin(ref _minWallSwTicks, wallSwTicks);
			UpdateMax(ref _maxWallSwTicks, wallSwTicks);
		}

		private static void UpdateMin(ref long target, long candidate)
		{
			long read = Volatile.Read(in target);
			while (candidate < read)
			{
				long compareExchange = Interlocked.CompareExchange(ref target, candidate, read);
				if (compareExchange != read)
				{
					read = compareExchange;
					continue;
				}
				break;
			}
		}

		private static void UpdateMax(ref long target, long candidate)
		{
			long read = Volatile.Read(in target);
			while (candidate > read)
			{
				long compareExchange = Interlocked.CompareExchange(ref target, candidate, read);
				if (compareExchange != read)
				{
					read = compareExchange;
					continue;
				}
				break;
			}
		}
	}

	public readonly struct ActiveInterval(string name, long wallStartSwTicks, long cpuStart100ns)
	{
		public readonly string Name = name;

		public readonly long WallStartSwTicks = wallStartSwTicks;

		public readonly long CpuStart100ns = cpuStart100ns;
	}

	public struct Scope(CodeTimer owner, string name, Stack<ActiveInterval> activeIntervals) : IDisposable
	{
		private readonly CodeTimer _owner = owner;

		private readonly string _name = name;

		private readonly Stack<ActiveInterval> _stack = activeIntervals;

		private readonly bool _valid = true;

		private int _stopped = 0;

		public void Stop()
		{
			if (_valid && Interlocked.Exchange(ref _stopped, 1) != 1)
			{
				_owner.StopCore(_name, _stack);
			}
		}

		public void Dispose()
		{
			Stop();
		}
	}

	private struct FILETIME
	{
		public uint dwLowDateTime;

		public uint dwHighDateTime;
	}

	private readonly bool _cpuEnabled;

	private readonly bool _useAsyncLocal;

	private readonly AsyncLocal<Dictionary<string, Stack<ActiveInterval>>> _asyncStacks;

	private readonly ThreadLocal<Dictionary<string, Stack<ActiveInterval>>> _threadStacks;

	private readonly ConcurrentDictionary<string, TimerStats> _stats = new ConcurrentDictionary<string, TimerStats>(StringComparer.Ordinal);

	public bool CpuEnabled => _cpuEnabled;

	public ContextMode Mode
	{
		get
		{
			if (!_useAsyncLocal)
			{
				return ContextMode.ThreadStatic;
			}
			return ContextMode.AsyncLocal;
		}
	}

	public CodeTimer(bool cpuEnabled = false, ContextMode contextMode = ContextMode.AsyncLocal)
	{
		_cpuEnabled = cpuEnabled;
		_useAsyncLocal = contextMode == ContextMode.AsyncLocal;
		if (_useAsyncLocal)
		{
			_asyncStacks = new AsyncLocal<Dictionary<string, Stack<ActiveInterval>>>();
			return;
		}
		_threadStacks = new ThreadLocal<Dictionary<string, Stack<ActiveInterval>>>(() => new Dictionary<string, Stack<ActiveInterval>>(StringComparer.Ordinal), trackAllValues: false);
	}

	public Scope Measure(string name)
	{
		return Start(name);
	}

	public Scope Start(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException("Timer name must not be empty.", "name");
		}
		Dictionary<string, Stack<ActiveInterval>> stacks = GetStacks();
		if (!stacks.TryGetValue(name, out var stacksActiveIntervalTerm))
		{
			stacksActiveIntervalTerm = (stacks[name] = new Stack<ActiveInterval>(4));
		}
		long timestamp = Stopwatch.GetTimestamp();
		long cpuStart100ns = (_cpuEnabled ? GetCurrentThreadCpuTime100ns() : 0);
		stacksActiveIntervalTerm.Push(new ActiveInterval(name, timestamp, cpuStart100ns));
		return new Scope(this, name, stacksActiveIntervalTerm);
	}

	public bool Stop(string name)
	{
		if (!GetStacks().TryGetValue(name, out var localValue) || localValue.Count == 0)
		{
			return false;
		}
		StopCore(name, localValue);
		return true;
	}

	public Scope Resume(string name)
	{
		return Start(name);
	}

	public bool Pause(string name)
	{
		return Stop(name);
	}

	public void Reset()
	{
		_stats.Clear();
	}

	public string GetSummary(bool sortByWallDescending = true)
	{
		List<TimerStats> timerStats = new List<TimerStats>(_stats.Values);
		if (sortByWallDescending)
		{
			timerStats.Sort((TimerStats a, TimerStats b) => b.TotalWallSwTicks.CompareTo(a.TotalWallSwTicks));
		}
		else
		{
			timerStats.Sort((TimerStats a, TimerStats b) => StringComparer.Ordinal.Compare(a.Name, b.Name));
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("CodeTimer summary");
		if (_cpuEnabled)
		{
			stringBuilder.AppendLine("Name | Count | Wall Total | Wall Avg | CPU Total | CPU Avg | CPU/Wall | Min | Max");
		}
		else
		{
			stringBuilder.AppendLine("Name | Count | Wall Total | Wall Avg | Min | Max");
		}
		stringBuilder.AppendLine(new string('-', _cpuEnabled ? 90 : 65));
		foreach (TimerStats timerStat in timerStats)
		{
			long count = timerStat.Count;
			long totalWallSwTicks = timerStat.TotalWallSwTicks;
			long swTicks = ((count > 0) ? (totalWallSwTicks / Math.Max(1L, count)) : 0);
			long swTicks2 = ((timerStat.MinWallSwTicks == long.MaxValue) ? 0 : timerStat.MinWallSwTicks);
			long maxWallSwTicks = timerStat.MaxWallSwTicks;
			string fromStopwatchTicks = FormatFromStopwatchTicks(totalWallSwTicks);
			string fromStopwatchTicks2 = FormatFromStopwatchTicks(swTicks);
			string fromStopwatchTicks3 = FormatFromStopwatchTicks(swTicks2);
			string fromStopwatchTicks4 = FormatFromStopwatchTicks(maxWallSwTicks);
			if (_cpuEnabled)
			{
				long totalCpu100nsTicks = timerStat.TotalCpu100nsTicks;
				long ticks100ns = ((count > 0) ? (totalCpu100nsTicks / Math.Max(1L, count)) : 0);
				string from100ns = FormatFrom100ns(totalCpu100nsTicks);
				string from100ns2 = FormatFrom100ns(ticks100ns);
				string message = ((totalWallSwTicks > 0) ? $"{100.0 * ((double)totalCpu100nsTicks / (double)StopwatchTicksTo100ns(totalWallSwTicks)):0.0}%" : "n/a");
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder3 = stringBuilder2;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(24, 9, stringBuilder2);
				handler.AppendFormatted(timerStat.Name);
				handler.AppendLiteral(" | ");
				handler.AppendFormatted(count);
				handler.AppendLiteral(" | ");
				handler.AppendFormatted<string>(fromStopwatchTicks, 10);
				handler.AppendLiteral(" | ");
				handler.AppendFormatted<string>(fromStopwatchTicks2, 8);
				handler.AppendLiteral(" | ");
				handler.AppendFormatted<string>(from100ns, 9);
				handler.AppendLiteral(" | ");
				handler.AppendFormatted<string>(from100ns2, 7);
				handler.AppendLiteral(" | ");
				handler.AppendFormatted<string>(message, 7);
				handler.AppendLiteral(" | ");
				handler.AppendFormatted<string>(fromStopwatchTicks3, 5);
				handler.AppendLiteral(" | ");
				handler.AppendFormatted<string>(fromStopwatchTicks4, 5);
				stringBuilder3.AppendLine(ref handler);
			}
			else
			{
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder4 = stringBuilder2;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(15, 6, stringBuilder2);
				handler.AppendFormatted(timerStat.Name);
				handler.AppendLiteral(" | ");
				handler.AppendFormatted(count);
				handler.AppendLiteral(" | ");
				handler.AppendFormatted<string>(fromStopwatchTicks, 10);
				handler.AppendLiteral(" | ");
				handler.AppendFormatted<string>(fromStopwatchTicks2, 8);
				handler.AppendLiteral(" | ");
				handler.AppendFormatted<string>(fromStopwatchTicks3, 5);
				handler.AppendLiteral(" | ");
				handler.AppendFormatted<string>(fromStopwatchTicks4, 5);
				stringBuilder4.AppendLine(ref handler);
			}
		}
		return stringBuilder.ToString();
	}

	private void StopCore(string name, Stack<ActiveInterval> activeIntervals)
	{
		if (activeIntervals.Count != 0)
		{
			ActiveInterval activeInterval = activeIntervals.Pop();
			long wallSwTicks = Stopwatch.GetTimestamp() - activeInterval.WallStartSwTicks;
			long cpu100nsTicks = 0L;
			if (_cpuEnabled)
			{
				long currentThreadCpuTime100ns = GetCurrentThreadCpuTime100ns();
				cpu100nsTicks = Math.Max(0L, currentThreadCpuTime100ns - activeInterval.CpuStart100ns);
			}
			_stats.GetOrAdd(name, (string n) => new TimerStats(n)).AddSample(wallSwTicks, cpu100nsTicks, _cpuEnabled);
		}
	}

	private Dictionary<string, Stack<ActiveInterval>> GetStacks()
	{
		if (_useAsyncLocal)
		{
			Dictionary<string, Stack<ActiveInterval>> activeIntervalListByString = _asyncStacks.Value;
			if (activeIntervalListByString == null)
			{
				activeIntervalListByString = new Dictionary<string, Stack<ActiveInterval>>(StringComparer.Ordinal);
				_asyncStacks.Value = activeIntervalListByString;
			}
			return activeIntervalListByString;
		}
		Dictionary<string, Stack<ActiveInterval>> activeIntervalListByString2 = _threadStacks.Value;
		if (activeIntervalListByString2 == null)
		{
			activeIntervalListByString2 = new Dictionary<string, Stack<ActiveInterval>>(StringComparer.Ordinal);
			_threadStacks.Value = activeIntervalListByString2;
		}
		return activeIntervalListByString2;
	}

	private static string FormatFromStopwatchTicks(long swTicks)
	{
		if (swTicks <= 0)
		{
			return "0 ms";
		}
		return FormatFrom100ns(StopwatchTicksTo100ns(swTicks));
	}

	private static string FormatFrom100ns(long ticks100ns)
	{
		if (ticks100ns <= 0)
		{
			return "0 ms";
		}
		TimeSpan timeSpan = new TimeSpan(ticks100ns);
		if (!(timeSpan.TotalSeconds >= 1.0))
		{
			if (!(timeSpan.TotalMilliseconds >= 1.0))
			{
				return $"{timeSpan.TotalMilliseconds * 1000.0:0.000} µs";
			}
			return $"{timeSpan.TotalMilliseconds:0.000} ms";
		}
		return $"{timeSpan.TotalSeconds:0.000} s";
	}

	private static long StopwatchTicksTo100ns(long swTicks)
	{
		long frequency = Stopwatch.Frequency;
		long swTicksOverFrequency = swTicks / frequency;
		long swTicksWithFrequency = swTicks % frequency;
		return swTicksOverFrequency * 10000000 + swTicksWithFrequency * 10000000 / frequency;
	}

	private static long GetCurrentThreadCpuTime100ns()
	{
		nint currentThread = GetCurrentThread();
		if (currentThread == IntPtr.Zero)
		{
			return 0L;
		}
		if (GetThreadTimes(currentThread, out var _, out var _, out var lpKernelTime, out var lpUserTime))
		{
			long fileTimeToTicks = FileTimeToTicks(lpKernelTime);
			long fileTimeToTicks2 = FileTimeToTicks(lpUserTime);
			long fileTimeToTicksPlusFileTimeToTicks2 = fileTimeToTicks + fileTimeToTicks2;
			if (fileTimeToTicksPlusFileTimeToTicks2 >= 0)
			{
				return fileTimeToTicksPlusFileTimeToTicks2;
			}
			return 0L;
		}
		return 0L;
	}

	private static long FileTimeToTicks(FILETIME ft)
	{
		ulong dwHighDateTime = ft.dwHighDateTime;
		ulong dwLowDateTime = ft.dwLowDateTime;
		return (long)((dwHighDateTime << 32) | dwLowDateTime);
	}

	[DllImport("kernel32.dll")]
	private static extern nint GetCurrentThread();

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetThreadTimes(nint hThread, out FILETIME lpCreationTime, out FILETIME lpExitTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);
}
