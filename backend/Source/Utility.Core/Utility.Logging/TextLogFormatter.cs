using System;
using Microsoft.Extensions.Logging;

namespace Utility.Logging;

public sealed class TextLogFormatter : ILogTextFormatter
{
	private const string NoCategory = "?";

	public string TimestampFormat { get; set; } = "HH:mm:ss.fff";

	public bool UseUtc { get; set; }

	public bool IncludeLevel { get; set; } = true;

	public bool IncludeCategory { get; set; } = true;

	public bool IncludeThreadId { get; set; }

	public bool IncludeEventId { get; set; }

	public bool IncludeProperties { get; set; }

	public bool IncludeException { get; set; } = true;

	public void Format(in LogRecord record, ref ValueStringBuilder builder)
	{
		if (!string.IsNullOrEmpty(TimestampFormat))
		{
			DateTime timestamp = (UseUtc ? record.TimestampUtc : record.TimestampLocal);
			AppendTimestamp(ref builder, timestamp);
			builder.Append(' ');
		}
		if (IncludeLevel)
		{
			builder.Append(LevelCode(record.Level));
			builder.Append(' ');
		}
		if (IncludeThreadId)
		{
			builder.Append('#');
			AppendInt(ref builder, record.ThreadId);
			builder.Append(' ');
		}
		if (IncludeCategory)
		{
			builder.Append('[');
			builder.Append(string.IsNullOrEmpty(record.Category) ? "?" : record.Category);
			if (IncludeEventId && record.EventId.Id != 0)
			{
				builder.Append('/');
				AppendInt(ref builder, record.EventId.Id);
			}
			builder.Append(']');
			builder.Append(' ');
		}
		builder.Append(record.Message);
		if (IncludeProperties)
		{
			AppendProperties(ref builder, record.Properties);
		}
		builder.Append(Environment.NewLine);
		if (IncludeException && record.Exception != null)
		{
			AppendIndented(ref builder, record.Exception.ToString());
		}
	}

	private static void AppendProperties(ref ValueStringBuilder builder, ReadOnlySpan<LogProperty> properties)
	{
		if (properties.Length == 0)
		{
			return;
		}
		builder.Append(" {");
		for (int i = 0; i < properties.Length; i++)
		{
			if (i > 0)
			{
				builder.Append(", ");
			}
			builder.Append(properties[i].Name);
			builder.Append('=');
			builder.Append(properties[i].Value?.ToString());
		}
		builder.Append('}');
	}

	private static void AppendIndented(ref ValueStringBuilder builder, string textValue)
	{
		ReadOnlySpan<char> textValueSpan = textValue;
		ReadOnlySpan<char> newLines = Environment.NewLine;
		while (!textValueSpan.IsEmpty)
		{
			int indexOf = textValueSpan.IndexOf('\n');
			ReadOnlySpan<char> slice = ((indexOf < 0) ? textValueSpan : textValueSpan.Slice(0, indexOf));
			textValueSpan = ((indexOf < 0) ? default(ReadOnlySpan<char>) : textValueSpan.Slice(indexOf + 1));
			if (slice.Length > 0 && slice[slice.Length - 1] == '\r')
			{
				slice = slice.Slice(0, slice.Length - 1);
			}
			builder.Append("    ");
			builder.Append(slice);
			builder.Append(newLines);
		}
	}

	private void AppendTimestamp(ref ValueStringBuilder builder, DateTime timestamp)
	{
		ReadOnlySpan<char> format = TimestampFormat;
		int charsWritten;
		while (!timestamp.TryFormat(builder.RawChars.Slice(builder.Length), out charsWritten, format))
		{
			builder.EnsureCapacity(Math.Max(builder.Capacity * 2, builder.Length + 64));
		}
		builder.Length += charsWritten;
	}

	private static void AppendInt(ref ValueStringBuilder builder, int ordinal)
	{
		int charsWritten;
		while (!ordinal.TryFormat(builder.RawChars.Slice(builder.Length), out charsWritten))
		{
			builder.EnsureCapacity(Math.Max(builder.Capacity * 2, builder.Length + 16));
		}
		builder.Length += charsWritten;
	}

	public static string LevelCode(LogLevel level)
	{
		return level switch
		{
			LogLevel.Trace => "TRC", 
			LogLevel.Debug => "DBG", 
			LogLevel.Information => "INF", 
			LogLevel.Warning => "WRN", 
			LogLevel.Error => "ERR", 
			LogLevel.Critical => "CRT", 
			_ => "NON", 
		};
	}

	void ILogTextFormatter.Format(in LogRecord record, ref ValueStringBuilder builder)
	{
		Format(in record, ref builder);
	}
}
