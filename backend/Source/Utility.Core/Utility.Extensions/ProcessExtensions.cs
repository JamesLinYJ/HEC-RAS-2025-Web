using System;
using System.Diagnostics;

namespace Utility.Extensions;

public static class ProcessExtensions
{
	private static string FindIndexedProcessName(int pid)
	{
		Process.GetProcessesByName(Process.GetProcessById(pid).ProcessName);
		return null;
	}

	private static Process FindPidFromIndexedProcessName(string indexedProcessName)
	{
		throw new NotImplementedException();
	}

	public static Process Parent(this Process process)
	{
		return FindPidFromIndexedProcessName(FindIndexedProcessName(process.Id));
	}
}
