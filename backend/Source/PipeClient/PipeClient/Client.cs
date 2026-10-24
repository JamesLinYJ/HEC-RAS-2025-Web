using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using Utility.Systems;

namespace PipeClient;

public static class Client
{
	public static bool ShowServerConsoleWindow;

	public const string ServerExeName = "PipeServer.exe";

	public static string OverrideServerDirectory;

	public static void RunRemote(string pipeName, string cmd, bool blockUntilExit, Action<NamedPipeClientStream> InitiateClient)
	{
		Process serverProcess = GetServerProcess(pipeName, cmd);
		serverProcess.Start();
		using (NamedPipeClientStream startClientWaitConnection = Pipe.StartClientWaitConnection(pipeName))
		{
			InitiateClient(startClientWaitConnection);
		}
		if (blockUntilExit)
		{
			serverProcess.WaitForExit();
		}
	}

	public static void StartRemoteServer(string pipeName, string cmd, Action<NamedPipeClientStream> InitiateClient, out int procID)
	{
		Process serverProcess = GetServerProcess(pipeName, cmd);
		serverProcess.Start();
		using (NamedPipeClientStream startClientWaitConnection = Pipe.StartClientWaitConnection(pipeName))
		{
			InitiateClient(startClientWaitConnection);
		}
		procID = serverProcess.Id;
	}

	public static T StartRemoteServer<T>(string pipeName, string cmd, Func<NamedPipeClientStream, T> InitiateClient)
	{
		Process serverProcess = GetServerProcess(pipeName, cmd);
		serverProcess.Start();
		T result;
		using (NamedPipeClientStream startClientWaitConnection = Pipe.StartClientWaitConnection(pipeName))
		{
			result = InitiateClient(startClientWaitConnection);
		}
		serverProcess.WaitForExit();
		return result;
	}

	public static NamedPipeClientStream StartRemoteServer(string pipeName, string cmd)
	{
		GetServerProcess(pipeName, cmd).Start();
		return Pipe.StartClientWaitConnection(pipeName);
	}

	public static NamedPipeClientStream StartRemoteServer(string pipeName, string cmd, out int PID)
	{
		Process serverProcess = GetServerProcess(pipeName, cmd);
		serverProcess.Start();
		PID = serverProcess.Id;
		return Pipe.StartClientWaitConnection(pipeName);
	}

	public static Process GetServerProcess(string pipeName, string cmd)
	{
		string getDirectoryNamePathTerm;
		if (string.IsNullOrEmpty(OverrideServerDirectory))
		{
			getDirectoryNamePathTerm = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			Console.WriteLine("Current executing assembly Folder: " + getDirectoryNamePathTerm);
		}
		else
		{
			getDirectoryNamePathTerm = OverrideServerDirectory;
			Console.WriteLine("Server directory manual override: " + getDirectoryNamePathTerm);
		}
		string path = Path.Combine(getDirectoryNamePathTerm, "PipeServer.exe");
		if (!File.Exists(path))
		{
			throw new Exception("Server " + path + " was not found.");
		}
		Console.WriteLine("Server: " + path);
		Process process = new Process();
		ProcessStartInfo processStartInfo = (process.StartInfo = new ProcessStartInfo(path, Pipe.FormatArgs(cmd, pipeName)));
		if (!ShowServerConsoleWindow)
		{
			processStartInfo.UseShellExecute = false;
			processStartInfo.CreateNoWindow = true;
		}
		return process;
	}
}
