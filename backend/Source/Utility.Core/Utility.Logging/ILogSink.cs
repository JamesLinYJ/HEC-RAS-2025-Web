using System;
using Microsoft.Extensions.Logging;

namespace Utility.Logging;

public interface ILogSink : IDisposable
{
	LogLevel MinimumLevel { get; }

	bool WantsProperties { get; }

	void Emit(ReadOnlySpan<LogRecord> batch);

	void Flush();
}
