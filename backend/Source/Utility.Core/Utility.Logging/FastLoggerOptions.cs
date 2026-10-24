using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Utility.Logging;

public sealed class FastLoggerOptions
{
	public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

	public Dictionary<string, LogLevel> CategoryLevels { get; } = new Dictionary<string, LogLevel>(StringComparer.OrdinalIgnoreCase);

	public int QueueCapacity { get; set; } = 8192;

	public int BatchSize { get; set; } = 512;

	public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(2L);

	public LogLevel FlushOnLevel { get; set; } = LogLevel.Error;

	public LogOverflowPolicy OverflowPolicy { get; set; }

	public TimeSpan BlockTimeout { get; set; } = TimeSpan.FromMilliseconds(100L);

	public bool CaptureScopes { get; set; }

	public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5L);

	public ThreadPriority WriterPriority { get; set; } = ThreadPriority.BelowNormal;

	public int MaxSinkFailures { get; set; } = 5;

	public TimeSpan SinkRetryInterval { get; set; } = TimeSpan.FromSeconds(30L);

	public List<ILogSink> Sinks { get; } = new List<ILogSink>();

	public LogLevel ResolveLevel(string category)
	{
		if (string.IsNullOrEmpty(category) || CategoryLevels.Count == 0)
		{
			return MinimumLevel;
		}
		LogLevel result = MinimumLevel;
		int ordinal = -1;
		foreach (KeyValuePair<string, LogLevel> categoryLevel in CategoryLevels)
		{
			string key = categoryLevel.Key;
			if (key.Length > ordinal && category.StartsWith(key, StringComparison.OrdinalIgnoreCase) && (category.Length <= key.Length || category[key.Length] == '.'))
			{
				ordinal = key.Length;
				result = categoryLevel.Value;
			}
		}
		return result;
	}
}
