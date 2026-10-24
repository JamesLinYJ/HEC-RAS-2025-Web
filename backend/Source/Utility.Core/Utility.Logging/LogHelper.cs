using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Utility.Logging;

public static class LogHelper
{
	private static readonly HashSet<string> _logOnce = new HashSet<string>();

	private static readonly Dictionary<string, int> _logDebounce = new Dictionary<string, int>();

	public static ILoggerFactory LoggerFactory { get; set; } = new LoggerFactory();

	public static string DefaultLogLocation(string fn = "")
	{
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HEC", "RAS", "Logs");
		if (string.IsNullOrEmpty(fn))
		{
			return path;
		}
		return Path.Combine(path, fn);
	}

	public static void Dispose()
	{
		LoggerFactory?.Dispose();
		LoggerFactory = null;
	}

	public static ILogger GetLogger(string name)
	{
		ILoggerFactory loggerFactory = LoggerFactory;
		if (loggerFactory == null)
		{
			return null;
		}
		try
		{
			return loggerFactory.CreateLogger(name);
		}
		catch
		{
			return null;
		}
	}

	public static void LogOnce(this ILogger logger, LogLevel level, string msg, params object[] args)
	{
		lock (_logOnce)
		{
			if (_logOnce.Add(msg))
			{
				logger.Log(level, msg, args);
			}
		}
	}

	public static void LogDebounce(this ILogger logger, LogLevel level, string msg)
	{
		lock (_logDebounce)
		{
			if (_logDebounce.TryGetValue(msg, out var logDebounce))
			{
				logDebounce++;
				if (IsPowerOf2(logDebounce))
				{
					logger.Log(level, msg + " ({Count}x)", logDebounce);
				}
				_logDebounce[msg] = logDebounce;
			}
			else
			{
				_logDebounce.Add(msg, 1);
				logger.Log(level, msg);
			}
		}
	}

	private static bool IsPowerOf2(int x)
	{
		if (x > 0)
		{
			return (x & (x - 1)) == 0;
		}
		return false;
	}
}
