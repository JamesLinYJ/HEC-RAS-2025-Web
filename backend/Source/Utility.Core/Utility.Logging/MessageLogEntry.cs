using System;

namespace Utility.Logging;

public readonly struct MessageLogEntry
{
	public DateTime Timestamp { get; }

	public MessageSeverity Severity { get; }

	public string Code { get; }

	public string[] Data { get; }

	public MessageLogEntry(DateTime timestamp, MessageSeverity severity, string code, string[] data)
	{
		Timestamp = timestamp;
		Severity = severity;
		Code = code ?? "";
		Data = data;
	}
}
