using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib.Utilities;

public abstract class General
{
	public static string BooleanToString(bool boolValue)
	{
		if (boolValue)
		{
			return "True";
		}
		return "False";
	}

	public static bool StringToBoolean(string stringValue)
	{
		if (stringValue.Equals("true", StringComparison.CurrentCultureIgnoreCase))
		{
			return true;
		}
		return false;
	}

	public static void ConsoleWriteLineToStdErr(string errMessage)
	{
		Stream stream = Console.OpenStandardError();
		byte[] bytes = new ASCIIEncoding().GetBytes(errMessage + "\r\n");
		stream.Write(bytes, 0, bytes.Count());
		stream.Close();
	}

	public static bool EqualStringArrays(string[] a, string[] b)
	{
		if ((a == null) & (b == null))
		{
			return true;
		}
		if ((a == null) | (b == null))
		{
			return false;
		}
		if (a.Length != b.Length)
		{
			return false;
		}
		checked
		{
			int count = a.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				if (Operators.CompareString(a[i], b[i], TextCompare: false) != 0)
				{
					return false;
				}
			}
			return true;
		}
	}

	public static void CopyStringArray(string[] a, ref string[] b)
	{
		if (a == null)
		{
			b = null;
			return;
		}
		b = new string[checked(a.Count() - 1 + 1)];
		Array.Copy(a, b, a.Length);
	}

	public static void LaunchProcess(string exeFilename, string args)
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo(exeFilename);
		processStartInfo.Arguments = args;
		processStartInfo.RedirectStandardInput = true;
		processStartInfo.RedirectStandardOutput = true;
		processStartInfo.RedirectStandardError = true;
		processStartInfo.CreateNoWindow = true;
		processStartInfo.UseShellExecute = false;
		Process process = new Process();
		process.StartInfo = processStartInfo;
		process.Start();
		BackgroundWorker backgroundWorker = new BackgroundWorker();
		backgroundWorker.DoWork += StdOutDoWork;
		backgroundWorker.RunWorkerCompleted += ConsoleProcessRWC;
		backgroundWorker.RunWorkerAsync(process.StandardOutput);
		BackgroundWorker backgroundWorker2 = new BackgroundWorker();
		backgroundWorker2.DoWork += StdErrDoWork;
		backgroundWorker2.RunWorkerAsync(process.StandardError);
		process.WaitForExit();
	}

	public static void StdOutDoWork(object ob, DoWorkEventArgs ev)
	{
		StreamReader streamReader = (StreamReader)ev.Argument;
		while (!streamReader.EndOfStream)
		{
		}
	}

	public static void StdErrDoWork(object ob, DoWorkEventArgs ev)
	{
		StreamReader streamReader = (StreamReader)ev.Argument;
		while (!streamReader.EndOfStream)
		{
		}
	}

	public static void ConsoleProcessRWC(object ob, RunWorkerCompletedEventArgs ev)
	{
	}
}
