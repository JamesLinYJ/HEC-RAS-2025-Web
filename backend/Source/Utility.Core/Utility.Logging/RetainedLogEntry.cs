using System;
using Microsoft.Extensions.Logging;

namespace Utility.Logging;

public readonly struct RetainedLogEntry(DateTime timestampUtc, LogLevel level, string category, string message, Exception exception)
{
	public readonly DateTime TimestampUtc = timestampUtc;

	public readonly LogLevel Level = level;

	public readonly string Category = category;

	public readonly string Message = message;

	public readonly Exception Exception = exception;

	public override string ToString()
	{
		return $"{TimestampUtc.ToLocalTime():HH:mm:ss.fff} {TextLogFormatter.LevelCode(Level)} {Message}";
	}
}
