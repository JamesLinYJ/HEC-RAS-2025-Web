using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Utility.Extensions;
using Utility.Progress;

namespace Utility.Processes;

public static class ProcessHelper
{
	public static Process Run(string exeName, string args, bool showWindow = false, ConsoleProgressParser reporter = null)
	{
		return Run(new ProcessStartInfo(exeName, args)
		{
			RedirectStandardError = true,
			RedirectStandardOutput = true,
			UseShellExecute = false,
			CreateNoWindow = !showWindow
		}, args, reporter);
	}

	public static Process RunAndLeaveOpen(string file)
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo("cmd.exe", "/k " + file.InDoubleQuotes());
		processStartInfo.RedirectStandardError = false;
		processStartInfo.RedirectStandardOutput = false;
		processStartInfo.UseShellExecute = false;
		processStartInfo.CreateNoWindow = false;
		Process process = new Process();
		process.StartInfo = processStartInfo;
		process.Start();
		return process;
	}

	public static Task<Process> RunAsync(string exeName, string args, ConsoleProgressParser reporter = null)
	{
		return RunAsync(new ProcessStartInfo(exeName, args)
		{
			RedirectStandardError = true,
			RedirectStandardOutput = true,
			UseShellExecute = false,
			CreateNoWindow = true
		}, args, reporter);
	}

	public static Process Run(ProcessStartInfo exe, string args, ConsoleProgressParser reporter = null)
	{
		return Run(exe.CloneWith(args), reporter);
	}

	public static Task<Process> RunAsync(ProcessStartInfo exe, string args, ConsoleProgressParser reporter = null)
	{
		return RunAsync(exe.CloneWith(args), reporter);
	}

	public static Process Run(ProcessStartInfo psi, ConsoleProgressParser reporter = null)
	{
		Process process = new Process();
		process.StartInfo = psi;
		process.Start();
		if (!TryStartReporting(process, psi, reporter, out var stdOut, out var stdErr))
		{
			process.WaitForExit();
			return process;
		}
		process.WaitForExit();
		stdOut.ProcessCompletedEvent.Wait();
		stdErr.ProcessCompletedEvent.Wait();
		return process;
	}

	public static Task<Process> RunAsync(ProcessStartInfo psi, ConsoleProgressParser reporter = null)
	{
		Process p = new Process();
		p.EnableRaisingEvents = true;
		p.StartInfo = psi;
		p.Start();
		TaskCompletionSource<Process> tcs = new TaskCompletionSource<Process>();
		if (!TryStartReporting(p, psi, reporter, out var stdOut, out var stdErr))
		{
			p.Exited += (object? s, EventArgs a) =>
			{
				tcs.TrySetResult(p);
			};
			return tcs.Task;
		}
		p.Exited += (object? s, EventArgs a) =>
		{
			stdOut.ProcessCompletedEvent.Wait();
			stdErr.ProcessCompletedEvent.Wait();
			tcs.TrySetResult(p);
		};
		return tcs.Task;
	}

	public static ProcessOutput RunStreaming(ProcessStartInfo psi, Action<char> stdOutReport, Action<char> stdErrReport, Action<Process> processReport = null)
	{
		using Process process = new Process();
		process.StartInfo = psi;
		process.Start();
		processReport?.Invoke(process);
		EXERunContext eXERunContext = StartStreamListener(process.StandardOutput, stdOutReport);
		EXERunContext eXERunContext2 = StartStreamListener(process.StandardError, stdErrReport);
		process.WaitForExit();
		eXERunContext.ProcessCompletedEvent.Wait();
		eXERunContext2.ProcessCompletedEvent.Wait();
		return new ProcessOutput(process.ExitCode, eXERunContext.CapturedText, eXERunContext2.CapturedText, timedOut: false, DescribeCommand(psi), EffectiveWorkingDirectory(psi));
	}

	public static ProcessOutput RunAndCapture(string exeName, string args, string workingDirectory = null, TimeSpan? timeout = null)
	{
		return RunAndCapture(CaptureStartInfo(exeName, args, workingDirectory), timeout);
	}

	public static Task<ProcessOutput> RunAndCaptureAsync(string exeName, string args, string workingDirectory = null, TimeSpan? timeout = null)
	{
		return RunAndCaptureAsync(CaptureStartInfo(exeName, args, workingDirectory), timeout);
	}

	public static ProcessOutput RunAndCapture(string exeName, IEnumerable<string> args, string workingDirectory = null, TimeSpan? timeout = null)
	{
		return RunAndCapture(CaptureStartInfo(exeName, args, workingDirectory), timeout);
	}

	public static Task<ProcessOutput> RunAndCaptureAsync(string exeName, IEnumerable<string> args, string workingDirectory = null, TimeSpan? timeout = null)
	{
		return RunAndCaptureAsync(CaptureStartInfo(exeName, args, workingDirectory), timeout);
	}

	public static ProcessOutput RunAndCapture(ProcessStartInfo psi, TimeSpan? timeout = null)
	{
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = new StringBuilder();
		using Process process = StartCapturing(psi, stringBuilder, stringBuilder2);
		bool hasValueAndNotWaitForExit = timeout.HasValue && !process.WaitForExit((int)timeout.Value.TotalMilliseconds);
		if (hasValueAndNotWaitForExit)
		{
			KillTree(process);
		}
		process.WaitForExit();
		return new ProcessOutput(process.ExitCode, stringBuilder.ToString(), stringBuilder2.ToString(), hasValueAndNotWaitForExit, DescribeCommand(psi), EffectiveWorkingDirectory(psi));
	}

	public static async Task<ProcessOutput> RunAndCaptureAsync(ProcessStartInfo psi, TimeSpan? timeout = null)
	{
		StringBuilder stdOut = new StringBuilder();
		StringBuilder stdErr = new StringBuilder();
		using Process p = StartCapturing(psi, stdOut, stdErr);
		bool timedOut = false;
		if (!timeout.HasValue)
		{
			await p.WaitForExitAsync();
		}
		else
		{
			using CancellationTokenSource expiry = new CancellationTokenSource(timeout.Value);
			try
			{
				await p.WaitForExitAsync(expiry.Token);
			}
			catch (OperationCanceledException)
			{
				timedOut = true;
				KillTree(p);
				await p.WaitForExitAsync();
			}
		}
		return new ProcessOutput(p.ExitCode, stdOut.ToString(), stdErr.ToString(), timedOut, DescribeCommand(psi), EffectiveWorkingDirectory(psi));
	}

	public static string DescribeCommand(ProcessStartInfo psi)
	{
		string arguments = psi.Arguments;
		if (string.IsNullOrEmpty(arguments) && psi.ArgumentList.Count > 0)
		{
			arguments = string.Join(" ", psi.ArgumentList.Select(Quoted));
		}
		if (string.IsNullOrEmpty(arguments))
		{
			return Quoted(psi.FileName);
		}
		return Quoted(psi.FileName) + " " + arguments;
	}

	private static string Quoted(string textValue)
	{
		if (string.IsNullOrEmpty(textValue) || !textValue.Contains(' ') || textValue.StartsWith('"'))
		{
			return textValue;
		}
		return textValue.InDoubleQuotes();
	}

	private static string EffectiveWorkingDirectory(ProcessStartInfo psi)
	{
		if (!string.IsNullOrEmpty(psi.WorkingDirectory))
		{
			return psi.WorkingDirectory;
		}
		return Environment.CurrentDirectory;
	}

	private static ProcessStartInfo CaptureStartInfo(string exeName, string args, string workingDirectory)
	{
		return CaptureStartInfo(new ProcessStartInfo(exeName, args), workingDirectory);
	}

	private static ProcessStartInfo CaptureStartInfo(string exeName, IEnumerable<string> args, string workingDirectory)
	{
		return CaptureStartInfo(new ProcessStartInfo(exeName, args), workingDirectory);
	}

	private static ProcessStartInfo CaptureStartInfo(ProcessStartInfo psi, string workingDirectory)
	{
		psi.CreateNoWindow = true;
		if (!string.IsNullOrEmpty(workingDirectory))
		{
			psi.WorkingDirectory = workingDirectory;
		}
		return psi;
	}

	private static Process StartCapturing(ProcessStartInfo psi, StringBuilder stdOut, StringBuilder stdErr)
	{
		psi.RedirectStandardOutput = true;
		psi.RedirectStandardError = true;
		psi.UseShellExecute = false;
		Process process = new Process();
		process.StartInfo = psi;
		process.OutputDataReceived += (object s, DataReceivedEventArgs e) =>
		{
			AppendLine(stdOut, e.Data);
		};
		process.ErrorDataReceived += (object s, DataReceivedEventArgs e) =>
		{
			AppendLine(stdErr, e.Data);
		};
		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		return process;
	}

	private static void AppendLine(StringBuilder builder, string line)
	{
		if (line != null)
		{
			builder.AppendLine(line);
		}
	}

	private static void KillTree(Process p)
	{
		try
		{
			p.Kill(entireProcessTree: true);
		}
		catch
		{
		}
	}

	private static bool TryStartReporting(Process p, ProcessStartInfo psi, ConsoleProgressParser reporter, out EXERunContext stdOut, out EXERunContext stdErr)
	{
		stdOut = null;
		stdErr = null;
		if (reporter == null || !psi.RedirectStandardOutput || !psi.RedirectStandardError || psi.UseShellExecute)
		{
			return false;
		}
		reporter.Reset();
		stdOut = StartStreamListener(p.StandardOutput, reporter.ReportStdOut);
		stdErr = StartStreamListener(p.StandardError, reporter.ReportStdErr);
		return true;
	}

	private static EXERunContext StartStreamListener(StreamReader stream, Action<char> report)
	{
		EXERunContext eXERunContext = new EXERunContext(stream, report);
		BackgroundWorker backgroundWorker = new BackgroundWorker();
		backgroundWorker.DoWork += StreamListener;
		backgroundWorker.RunWorkerAsync(eXERunContext);
		return eXERunContext;
	}

	private static void StreamListener(object sender, DoWorkEventArgs e)
	{
		EXERunContext eXERunContext = e.Argument as EXERunContext;
		char[] charValues = new char[1];
		while (!eXERunContext.Stream.EndOfStream)
		{
			eXERunContext.Stream.Read(charValues, 0, 1);
			eXERunContext.Accept(charValues[0]);
		}
		eXERunContext.MarkCompleted();
	}

	public static void OpenWebsite(string website)
	{
		Process.Start(new ProcessStartInfo
		{
			FileName = website,
			UseShellExecute = true
		});
	}

	public static Process GetParentProcess()
	{
		return GetParentProcess(Process.GetCurrentProcess().Handle);
	}

	public static Process GetParentProcess(int id)
	{
		return GetParentProcess(Process.GetProcessById(id).Handle);
	}

	public static Process GetParentProcess(nint handle)
	{
		ParentProcessUtilities processInformation = default;
		int ntQueryInformationProcess = ParentProcessUtilities.NtQueryInformationProcess(handle, 0, ref processInformation, Marshal.SizeOf(processInformation), out var _);
		if (ntQueryInformationProcess != 0)
		{
			throw new Win32Exception(ntQueryInformationProcess);
		}
		try
		{
			return Process.GetProcessById(((IntPtr)processInformation.InheritedFromUniqueProcessId).ToInt32());
		}
		catch (ArgumentException)
		{
			return null;
		}
	}
}
