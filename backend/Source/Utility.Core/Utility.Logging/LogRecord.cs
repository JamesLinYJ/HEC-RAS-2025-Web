using System;
using System.Buffers;
using Microsoft.Extensions.Logging;

namespace Utility.Logging;

public readonly struct LogRecord
{
	public readonly long Sequence;

	public readonly DateTime TimestampUtc;

	public readonly LogLevel Level;

	public readonly EventId EventId;

	public readonly string Category;

	public readonly string Template;

	public readonly Exception Exception;

	public readonly int ThreadId;

	private readonly char[] _message;

	private readonly int _messageLength;

	private readonly LogProperty[] _properties;

	private readonly int _propertyCount;

	public ReadOnlySpan<char> Message => (_message == null) ? default(Span<char>) : _message.AsSpan(0, _messageLength);

	public ReadOnlySpan<LogProperty> Properties => (_properties == null) ? default(Span<LogProperty>) : _properties.AsSpan(0, _propertyCount);

	public DateTime TimestampLocal => TimestampUtc.ToLocalTime();

	internal LogRecord(long sequence, DateTime timestampUtc, LogLevel level, EventId eventId, string category, string template, Exception exception, int threadId, char[] message, int messageLength, LogProperty[] properties, int propertyCount)
	{
		Sequence = sequence;
		TimestampUtc = timestampUtc;
		Level = level;
		EventId = eventId;
		Category = category;
		Template = template;
		Exception = exception;
		ThreadId = threadId;
		_message = message;
		_messageLength = messageLength;
		_properties = properties;
		_propertyCount = propertyCount;
	}

	public string MessageToString()
	{
		if (_message != null)
		{
			return new string(_message, 0, _messageLength);
		}
		return string.Empty;
	}

	internal void Release()
	{
		if (_message != null)
		{
			LogBufferPool.Return(_message);
		}
		if (_properties != null)
		{
			ArrayPool<LogProperty>.Shared.Return(_properties, clearArray: true);
		}
	}
}
