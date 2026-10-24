using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Threading.Tasks;

namespace Utility.Systems;

public static class DebugRasStreamer
{
	private static string _rasExe;

	private static NamedPipeServerStream _stream;

	private static Process _proc;

	private static string TryFindRasExe()
	{
		string directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
		string path = Path.Combine(directoryName, "Ras.Interface.WinUI.exe");
		if (File.Exists(path))
		{
			return path;
		}
		string directoryName2 = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
		if (directoryName != directoryName2)
		{
			path = Path.Combine(directoryName2, "Ras.Interface.WinUI.exe");
			if (File.Exists(path))
			{
				return path;
			}
		}
		string textValue = "Debug";
		string fullPath = Path.GetFullPath(Path.Combine(directoryName, "..\\..\\..\\..\\..\\Ras.Interface.WinUI\\bin\\x64\\" + textValue + "\\"));
		if (Directory.Exists(fullPath))
		{
			DateTime dateTime = DateTime.MinValue;
			string directoryTerm = "";
			string[] directories = Directory.GetDirectories(fullPath);
			foreach (string directory in directories)
			{
				DateTime creationTime = Directory.GetCreationTime(directory);
				if (creationTime > dateTime)
				{
					dateTime = creationTime;
					directoryTerm = directory;
				}
			}
			if (!string.IsNullOrEmpty(directoryTerm))
			{
				path = Path.Combine(directoryTerm, "Ras.Interface.WinUI.exe");
				if (File.Exists(path))
				{
					return path;
				}
			}
		}
		return "";
	}

	public static void AssignStream(NamedPipeServerStream stream)
	{
		_stream = stream;
	}

	public static bool Stream(IStreamableObject iStreamableObject)
	{
		if (_stream == null && !Bootstrap())
		{
			return false;
		}
		StreamableObjectHelper.Write(_stream, iStreamableObject);
		return true;
	}

	public static async Task<bool> StreamAsync(IStreamableObject iStreamableObject)
	{
		if (_stream == null && !Bootstrap())
		{
			return false;
		}
		await StreamableObjectHelper.WriteAsync(_stream, iStreamableObject);
		return true;
	}

	public static bool Bootstrap()
	{
		if (_stream != null)
		{
			return true;
		}
		if (string.IsNullOrEmpty(_rasExe))
		{
			_rasExe = TryFindRasExe();
			if (string.IsNullOrEmpty(_rasExe))
			{
				return false;
			}
		}
		string inOutIds = Guid.NewGuid().ToString();
		_stream = new NamedPipeServerStream(inOutIds, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
		_proc = Process.Start(_rasExe, "-stream=" + inOutIds);
		_stream.WaitForConnection();
		return true;
	}

	public static bool BootstrapImmediateWindow()
	{
		if (_stream != null)
		{
			return true;
		}
		if (string.IsNullOrEmpty(_rasExe))
		{
			_rasExe = TryFindRasExe();
			if (string.IsNullOrEmpty(_rasExe))
			{
				return false;
			}
		}
		string inOutIds = Guid.NewGuid().ToString();
		_stream = new NamedPipeServerStream(inOutIds, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None);
		_proc = Process.Start(_rasExe, "-stream=" + inOutIds);
		_stream.WaitForConnection();
		return true;
	}

	public static async Task<bool> BootstrapAsync()
	{
		if (_stream != null)
		{
			return true;
		}
		if (string.IsNullOrEmpty(_rasExe))
		{
			_rasExe = TryFindRasExe();
			if (string.IsNullOrEmpty(_rasExe))
			{
				return false;
			}
		}
		string inOutIds = Guid.NewGuid().ToString();
		_stream = new NamedPipeServerStream(inOutIds, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
		_proc = Process.Start(_rasExe, "-stream=" + inOutIds);
		await _stream.WaitForConnectionAsync();
		return true;
	}
}
