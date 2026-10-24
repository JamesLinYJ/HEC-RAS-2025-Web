using System;
using System.Threading;

namespace Utility.Logging;

internal sealed class LogScope : IDisposable
{
	private static readonly AsyncLocal<LogScope> _current = new AsyncLocal<LogScope>();

	private bool _disposed;

	public static LogScope Current => _current.Value;

	public object State { get; }

	public LogScope Parent { get; }

	private LogScope(object state, LogScope parent)
	{
		State = state;
		Parent = parent;
	}

	public static IDisposable Push(object state)
	{
		LogScope logScope = new LogScope(state, _current.Value);
		_current.Value = logScope;
		return logScope;
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			_current.Value = Parent;
		}
	}
}
