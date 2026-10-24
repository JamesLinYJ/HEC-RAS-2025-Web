using System;
using System.Threading;

namespace Utility.Threading;

public class EnsureSingleton : IDisposable
{
	private int _count;

	public bool IsRunning => _count > 0;

	public EnsureSingleton(bool autoStart = false)
	{
		_count = 0;
		if (autoStart)
		{
			Start();
		}
	}

	public IDisposable Lock()
	{
		return Start();
	}

	public IDisposable Start(string onMultiExecutionErrorMessage = "")
	{
		int increment = Interlocked.Increment(ref _count);
		if (increment > 1)
		{
			Interlocked.Decrement(ref _count);
			if (string.IsNullOrWhiteSpace(onMultiExecutionErrorMessage))
			{
				onMultiExecutionErrorMessage = "Multi-execution detected.";
			}
			throw new Exception(onMultiExecutionErrorMessage);
		}
		if (increment <= 0)
		{
			Interlocked.Decrement(ref _count);
			throw new Exception("Usage error detected - execution count was negative.");
		}
		return this;
	}

	public void Dispose()
	{
		Interlocked.Decrement(ref _count);
	}
}
