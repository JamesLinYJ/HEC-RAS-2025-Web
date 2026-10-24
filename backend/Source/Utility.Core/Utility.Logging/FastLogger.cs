using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Utility.Logging;

internal sealed class FastLogger(string category, FastLoggerProvider provider) : ILogger
{
	private readonly string _category = category;

	private readonly FastLoggerProvider _provider = provider;

	private LogLevel _cachedLevel = LogLevel.None;

	private int _cachedVersion = -1;

	private LogLevel EffectiveLevel
	{
		get
		{
			int levelVersion = _provider.LevelVersion;
			if (levelVersion == Volatile.Read(in _cachedVersion))
			{
				return _cachedLevel;
			}
			LogLevel result = (_cachedLevel = _provider.ResolveLevel(_category));
			Volatile.Write(ref _cachedVersion, levelVersion);
			return result;
		}
	}

	public IDisposable BeginScope<TState>(TState state)
	{
		return _provider.Dispatcher.BeginScope(state);
	}

	public bool IsEnabled(LogLevel logLevel)
	{
		if (logLevel == LogLevel.None)
		{
			return false;
		}
		if (logLevel < EffectiveLevel)
		{
			return false;
		}
		return _provider.Dispatcher.IsEnabled(logLevel);
	}

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
	{
		if (IsEnabled(logLevel))
		{
			_provider.Dispatcher.Write(_category, logLevel, eventId, state, exception, formatter);
		}
	}
}
