using System;
using Microsoft.Extensions.Logging;

namespace Utility.Logging;

public static class FastLoggerExtensions
{
	public static FastLoggerProvider AddFastLogger(this ILoggerFactory factory, Action<FastLoggerOptions> configure)
	{
		if (factory == null)
		{
			throw new ArgumentNullException("factory");
		}
		FastLoggerOptions fastLoggerOptions = new FastLoggerOptions();
		configure?.Invoke(fastLoggerOptions);
		FastLoggerProvider fastLoggerProvider = new FastLoggerProvider(fastLoggerOptions);
		factory.AddProvider(fastLoggerProvider);
		return fastLoggerProvider;
	}

	public static FastLoggerOptions WriteToFile(this FastLoggerOptions options, string path, Action<TextFileSinkOptions> configure = null)
	{
		if (options == null)
		{
			throw new ArgumentNullException("options");
		}
		TextFileSinkOptions textFileSinkOptions = new TextFileSinkOptions
		{
			Path = path
		};
		configure?.Invoke(textFileSinkOptions);
		options.Sinks.Add(new TextFileSink(textFileSinkOptions));
		return options;
	}

	public static FastLoggerOptions WriteToJsonLines(this FastLoggerOptions options, string path, Action<JsonLinesFileSinkOptions> configure = null)
	{
		if (options == null)
		{
			throw new ArgumentNullException("options");
		}
		JsonLinesFileSinkOptions jsonLinesFileSinkOptions = new JsonLinesFileSinkOptions
		{
			Path = path
		};
		configure?.Invoke(jsonLinesFileSinkOptions);
		options.Sinks.Add(new JsonLinesFileSink(jsonLinesFileSinkOptions));
		return options;
	}

	public static FastLoggerOptions WriteToMemory(this FastLoggerOptions options, out MemoryLogSink sink, int capacity = 512, LogLevel minimumLevel = LogLevel.Warning)
	{
		if (options == null)
		{
			throw new ArgumentNullException("options");
		}
		sink = new MemoryLogSink(capacity, minimumLevel);
		options.Sinks.Add(sink);
		return options;
	}

	public static FastLoggerOptions WriteTo(this FastLoggerOptions options, ILogSink sink)
	{
		if (options == null)
		{
			throw new ArgumentNullException("options");
		}
		if (sink == null)
		{
			throw new ArgumentNullException("sink");
		}
		options.Sinks.Add(sink);
		return options;
	}
}
