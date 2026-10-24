using System;
using System.Threading;

namespace Utility.Threading;

public class LockWrapper : IDisposable
{
	private bool _isDisposed;

	private readonly Action<LockWrapper> _onDisposing;

	private readonly Action<LockWrapper> _onDisposed;

	public ReaderWriterLockSlim Lock { get; }

	public string TaskName { get; }

	public bool IsWriter { get; }

	private LockWrapper(ReaderWriterLockSlim lck, string intent, bool isWriter, Action<LockWrapper> onDisposing = null, Action<LockWrapper> onDisposed = null)
	{
		Lock = lck;
		TaskName = intent;
		IsWriter = isWriter;
		_onDisposing = onDisposing;
		_onDisposed = onDisposed;
	}

	public static LockWrapper TryGetReaderLock(ReaderWriterLockSlim lck, int msTimeout, string intent, Action<LockWrapper> onDisposing, Action<LockWrapper> onDisposed)
	{
		if (lck.TryEnterReadLock(msTimeout))
		{
			return new LockWrapper(lck, intent, isWriter: false, onDisposing, onDisposed);
		}
		return null;
	}

	public static LockWrapper TryGetWriterLock(ReaderWriterLockSlim lck, int msTimeout, string intent, Action<LockWrapper> onDisposing, Action<LockWrapper> onDisposed)
	{
		if (lck.TryEnterWriteLock(msTimeout))
		{
			return new LockWrapper(lck, intent, isWriter: true, onDisposing, onDisposed);
		}
		return null;
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			_onDisposing?.Invoke(this);
			if (IsWriter)
			{
				Lock.ExitWriteLock();
			}
			else
			{
				Lock.ExitReadLock();
			}
			_isDisposed = true;
			_onDisposed?.Invoke(this);
		}
	}

	~LockWrapper()
	{
		Dispose(disposing: false);
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
