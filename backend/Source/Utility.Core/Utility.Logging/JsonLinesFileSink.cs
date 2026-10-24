using System;
using System.Buffers;
using System.Globalization;
using System.Text.Json;

namespace Utility.Logging;

public sealed class JsonLinesFileSink : RollingFileSinkBase
{
	private const int JsonBufferBytes = 1024;

	private const int MaxRetainedJsonBytes = 65536;

	private const int TimestampChars = 32;

	private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fff'Z'";

	private const byte Newline = 10;

	private static readonly JsonWriterOptions WriterOptions = new JsonWriterOptions
	{
		SkipValidation = true
	};

	private static readonly JsonEncodedText TimestampKey = JsonEncodedText.Encode("t");

	private static readonly JsonEncodedText SequenceKey = JsonEncodedText.Encode("seq");

	private static readonly JsonEncodedText LevelKey = JsonEncodedText.Encode("l");

	private static readonly JsonEncodedText CategoryKey = JsonEncodedText.Encode("c");

	private static readonly JsonEncodedText TemplateKey = JsonEncodedText.Encode("tpl");

	private static readonly JsonEncodedText MessageKey = JsonEncodedText.Encode("msg");

	private static readonly JsonEncodedText ThreadKey = JsonEncodedText.Encode("tid");

	private static readonly JsonEncodedText ExceptionKey = JsonEncodedText.Encode("ex");

	private static readonly JsonEncodedText PropertiesKey = JsonEncodedText.Encode("p");

	private ArrayBufferWriter<byte> _json = new ArrayBufferWriter<byte>(1024);

	private readonly Utf8JsonWriter _writer;

	public override bool WantsProperties => true;

	public JsonLinesFileSink(string path)
		: this(new JsonLinesFileSinkOptions
		{
			Path = path
		})
	{
	}

	public JsonLinesFileSink(JsonLinesFileSinkOptions options)
		: base(options)
	{
		_writer = new Utf8JsonWriter(_json, WriterOptions);
	}

	public override void Dispose()
	{
		_writer.Dispose();
		base.Dispose();
	}

	protected override void Write(in LogRecord record)
	{
		_json.ResetWrittenCount();
		_writer.Reset();
		_writer.WriteStartObject();
		Span<char> destination = stackalloc char[32];
		record.TimestampUtc.TryFormat(destination, out var charsWritten, "yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
		_writer.WriteString(TimestampKey, destination.Slice(0, charsWritten));
		_writer.WriteNumber(SequenceKey, record.Sequence);
		_writer.WriteNumber(LevelKey, (int)record.Level);
		_writer.WriteString(CategoryKey, record.Category);
		_writer.WriteString(TemplateKey, record.Template);
		_writer.WriteString(MessageKey, record.Message);
		_writer.WriteNumber(ThreadKey, record.ThreadId);
		if (record.Exception != null)
		{
			_writer.WriteString(ExceptionKey, record.Exception.ToString());
		}
		WriteProperties(record.Properties);
		_writer.WriteEndObject();
		_writer.Flush();
		_json.GetSpan(1)[0] = 10;
		_json.Advance(1);
		AppendBytes(_json.WrittenSpan);
		ShrinkJsonBufferIfOversized();
	}

	private void WriteProperties(ReadOnlySpan<LogProperty> properties)
	{
		if (properties.Length != 0)
		{
			_writer.WriteStartObject(PropertiesKey);
			ReadOnlySpan<LogProperty> propertiesSpan = properties;
			for (int i = 0; i < propertiesSpan.Length; i++)
			{
				WriteProperty(in propertiesSpan[i]);
			}
			_writer.WriteEndObject();
		}
	}

	private void WriteProperty(in LogProperty property)
	{
		string propertyName = property.Name ?? string.Empty;
		object localValue = property.Value;
		if (localValue != null)
		{
			if (!(localValue is string textValue))
			{
				if (!(localValue is bool condition))
				{
					if (!(localValue is int ordinal))
					{
						if (!(localValue is long ordinal2))
						{
							if (!(localValue is double computed))
							{
								if (!(localValue is float computed2))
								{
									if (!(localValue is decimal computed3))
									{
										if (!(localValue is DateTime dateTime))
										{
											if (localValue is TimeSpan timeSpan)
											{
												_writer.WriteNumber(propertyName, timeSpan.TotalMilliseconds);
											}
											else
											{
												_writer.WriteString(propertyName, Convert.ToString(property.Value, CultureInfo.InvariantCulture));
											}
										}
										else
										{
											_writer.WriteString(propertyName, dateTime);
										}
									}
									else
									{
										_writer.WriteNumber(propertyName, computed3);
									}
								}
								else
								{
									_writer.WriteNumber(propertyName, computed2);
								}
							}
							else
							{
								_writer.WriteNumber(propertyName, computed);
							}
						}
						else
						{
							_writer.WriteNumber(propertyName, ordinal2);
						}
					}
					else
					{
						_writer.WriteNumber(propertyName, ordinal);
					}
				}
				else
				{
					_writer.WriteBoolean(propertyName, condition);
				}
			}
			else
			{
				_writer.WriteString(propertyName, textValue);
			}
		}
		else
		{
			_writer.WriteNull(propertyName);
		}
	}

	private void ShrinkJsonBufferIfOversized()
	{
		if (_json.Capacity > 65536)
		{
			_json = new ArrayBufferWriter<byte>(1024);
			_writer.Reset(_json);
		}
	}
}
