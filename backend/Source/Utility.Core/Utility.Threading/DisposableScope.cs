using System;
using System.Threading;

namespace Utility.Threading;

public sealed class DisposableScope : IDisposable
{
	private Action _onDispose;

	private readonly IDisposable[] _chained;

	public DisposableScope(Action onDispose, params IDisposable[] chained)
	{
		_onDispose = onDispose;
		_chained = chained;
	}

	public void Dispose()
	{
		Action action = Interlocked.Exchange(ref _onDispose, null);
		if (action != null)
		{
			action();
			IDisposable[] chained = _chained;
			for (int i = 0; i < chained.Length; i++)
			{
				chained[i]?.Dispose();
			}
		}
	}
}
