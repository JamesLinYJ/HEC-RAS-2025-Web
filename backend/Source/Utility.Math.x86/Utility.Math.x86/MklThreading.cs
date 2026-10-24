using System;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Utility.Math.x86;

public static class MklThreading
{
	private static readonly ILogger _logger = LogHelper.GetLogger("MklThreading");

	private static volatile bool _available = true;

	public static bool Available => _available;

	public static int SetLocal(int numThreads)
	{
		if (!_available)
		{
			return 0;
		}
		try
		{
			return MklThreadingNative.mkl_set_num_threads_local(ref numThreads);
		}
		catch (Exception ex) when (ex is EntryPointNotFoundException || ex is DllNotFoundException)
		{
			MarkUnavailable(ex);
			return 0;
		}
	}

	public static void SetGlobal(int numThreads)
	{
		if (!_available)
		{
			return;
		}
		try
		{
			MklThreadingNative.mkl_set_num_threads(ref numThreads);
		}
		catch (Exception ex) when (ex is EntryPointNotFoundException || ex is DllNotFoundException)
		{
			MarkUnavailable(ex);
		}
	}

	public static int GetMax()
	{
		if (!_available)
		{
			return 0;
		}
		try
		{
			return MklThreadingNative.mkl_get_max_threads();
		}
		catch (Exception ex) when (ex is EntryPointNotFoundException || ex is DllNotFoundException)
		{
			MarkUnavailable(ex);
			return 0;
		}
	}

	private static void MarkUnavailable(Exception ex)
	{
		_available = false;
		_logger.LogWarning(ex, "MKL thread-control exports not found in {Dll}; thread budgeting disabled (MKL uses its default thread count). Update the HEC.RAS.NativeParallel package.", "RasNativeParallel.dll");
	}
}
