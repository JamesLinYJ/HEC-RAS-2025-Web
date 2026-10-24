using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Utility.Logging;

[ProviderAlias("Fast")]
public sealed class FastLoggerProvider : ILoggerProvider, IDisposable, ISupportExternalScope
{
	private readonly ConcurrentDictionary<string, FastLogger> _loggers = new ConcurrentDictionary<string, FastLogger>(StringComparer.Ordinal);

	private readonly FastLoggerOptions _options;

	private readonly LogDispatcher _dispatcher;

	private int _levelVersion;

	public LogDispatcher Dispatcher => _dispatcher;

	public FastLoggerOptions Options => _options;

	internal int LevelVersion => Volatile.Read(in _levelVersion);

	public FastLoggerProvider(FastLoggerOptions options)
	{
		_options = options ?? throw new ArgumentNullException("options");
		_dispatcher = new LogDispatcher(_options);
	}

	public ILogger CreateLogger(string categoryName)
	{
		return _loggers.GetOrAdd(categoryName ?? string.Empty, CreateLoggerCore);
	}

	private FastLogger CreateLoggerCore(string categoryName)
	{
		return new FastLogger(categoryName, this);
	}

	public bool Flush(TimeSpan timeout)
	{
		return _dispatcher.Flush(timeout);
	}

	public void SetMinimumLevel(LogLevel level)
	{
		_options.MinimumLevel = level;
		InvalidateLevels();
	}

	public void SetCategoryLevel(string category, LogLevel level)
	{
		_options.CategoryLevels[category] = level;
		InvalidateLevels();
	}

	public void InvalidateLevels()
	{
		Interlocked.Increment(ref _levelVersion);
	}

	internal LogLevel ResolveLevel(string category)
	{
		return _options.ResolveLevel(category);
	}

	public void SetScopeProvider(IExternalScopeProvider scopeProvider)
	{
	}

	public void Dispose()
	{
		_dispatcher.Dispose();
		_loggers.Clear();
	}
}
