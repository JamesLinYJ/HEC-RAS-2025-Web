using System;
using System.Diagnostics;
using System.Threading;
using SystemProcess = System.Diagnostics.Process;

namespace Ras.Process.Platform;

internal static class ExternalProcessLauncher
{
	private static readonly AsyncLocal<Func<ProcessStartInfo, bool>?> TestLauncher = new();

	public static bool Start(ProcessStartInfo startInfo)
	{
		ArgumentNullException.ThrowIfNull(startInfo);
		Func<ProcessStartInfo, bool>? testLauncher = TestLauncher.Value;
		if (testLauncher != null) return testLauncher(startInfo);
		using SystemProcess? process = SystemProcess.Start(startInfo);
		return process != null;
	}

	internal static IDisposable CaptureForTesting(Func<ProcessStartInfo, bool> launcher)
	{
		ArgumentNullException.ThrowIfNull(launcher);
		Func<ProcessStartInfo, bool>? previous = TestLauncher.Value;
		TestLauncher.Value = launcher;
		return new RestoreLauncher(previous);
	}

	private sealed class RestoreLauncher(Func<ProcessStartInfo, bool>? previous) : IDisposable
	{
		private int _disposed;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 0) TestLauncher.Value = previous;
		}
	}
}
