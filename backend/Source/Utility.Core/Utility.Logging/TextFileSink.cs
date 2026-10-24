using System;

namespace Utility.Logging;

public sealed class TextFileSink : RollingFileSinkBase
{
	private const int RenderStackChars = 512;

	private readonly ILogTextFormatter _formatter;

	public TextFileSink(string path)
		: this(new TextFileSinkOptions
		{
			Path = path
		})
	{
	}

	public TextFileSink(TextFileSinkOptions options)
		: base(options)
	{
		_formatter = options.Formatter ?? new TextLogFormatter();
	}

	protected override void Write(in LogRecord record)
	{
		Span<char> initialBuffer = stackalloc char[512];
		ValueStringBuilder builder = new ValueStringBuilder(initialBuffer);
		try
		{
			_formatter.Format(in record, ref builder);
			Append(builder.AsSpan());
		}
		finally
		{
			builder.Dispose();
		}
	}
}
