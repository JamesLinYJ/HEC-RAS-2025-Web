using System;
using System.IO;
using Utility.Logging;

namespace Utility.Progress;

public sealed class FileProgressTarget : IProgressTarget, IDisposable
{
	private readonly object _lock = new object();

	private readonly StreamWriter _writer;

	private readonly bool _includeProgress;

	private bool _disposed;

	public FileProgressTarget(string filePath, bool includeProgress = false)
	{
		if (string.IsNullOrEmpty(filePath))
		{
			throw new ArgumentException("File path is required.", "filePath");
		}
		string directoryName = Path.GetDirectoryName(filePath);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		_writer = new StreamWriter(filePath, append: true)
		{
			AutoFlush = true
		};
		_includeProgress = includeProgress;
	}

	public void WriteMessage(ProgressReporter sender, Message message)
	{
		string textValue;
		if (message.MessageType.DefaultSeverity == MessageSeverity.Error)
		{
			textValue = "ERROR ";
		}
		else
		{
			textValue = ((message.MessageType.DefaultSeverity == MessageSeverity.Warning) ? "WARN " : "");
		}
		lock (_lock)
		{
			if (!_disposed)
			{
				_writer.WriteLine($"{DateTime.Now:HH:mm:ss} {textValue}{message.EvaluatedMessage}");
			}
		}
	}

	public void WriteProgress(ProgressReporter sender, int localProgress, int globalProgress)
	{
		if (!_includeProgress)
		{
			return;
		}
		lock (_lock)
		{
			if (!_disposed)
			{
				_writer.WriteLine($"{DateTime.Now:HH:mm:ss} progress {globalProgress}%");
			}
		}
	}

	public void Dispose()
	{
		lock (_lock)
		{
			if (!_disposed)
			{
				_disposed = true;
				_writer.Dispose();
			}
		}
	}
}
