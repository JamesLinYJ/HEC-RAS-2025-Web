using System;
using System.Globalization;
using System.IO;

namespace Ras.Engine.Structures;

public static class StructureTSLogger
{
	private static readonly object _lock;

	private static StreamWriter _writer;

	static StructureTSLogger()
	{
		_lock = new object();
		OpenNewLogFile();
	}

	private static void OpenNewLogFile()
	{
		string directory = Path.Combine(Path.GetTempPath(), "HEC", "RAS", "StructureTimeSeries");
		Directory.CreateDirectory(directory);
		string now = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff", CultureInfo.InvariantCulture);
		string path = Path.Combine(directory, "struct_ts_" + now + "_" + Environment.ProcessId + "_" + Guid.NewGuid().ToString("N") + ".log");
		var next = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
		{
			AutoFlush = true
		};
		StreamWriter previous = _writer;
		_writer = next;
		previous?.Dispose();
	}

	public static void Log(string message)
	{
		lock (_lock)
		{
			_writer?.WriteLine(message);
		}
	}

	public static void RotateLog()
	{
		lock (_lock)
		{
			OpenNewLogFile();
		}
	}

	public static void Close()
	{
		lock (_lock)
		{
			StreamWriter previous = _writer;
			_writer = null;
			previous?.Dispose();
		}
	}
}
