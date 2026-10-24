using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Utility.Threading;

public class LockHelper
{
	private static readonly ILogger _logger = LogHelper.GetLogger("LockHelper");

	public const int WriteLockTimeoutMS = 250;

	public const int ExtendedWriteLockTimeoutMS = 1000;

	private readonly Func<ReaderWriterLockSlim> _getUnderlyingLock;

	private LockWrapper _writeLock;

	public List<LockWrapper> OutstandingLocks = new List<LockWrapper>();

	private readonly object _writeLockLock = new object();

	public string Name { get; set; }

	public LockWrapper WriteLock => _writeLock;

	public event EventHandler OnLockChanged;

	public LockHelper(Func<ReaderWriterLockSlim> getUnderlyingLock, string logName)
	{
		_getUnderlyingLock = getUnderlyingLock;
		Name = logName;
	}

	public LockWrapper TryGetReaderLock(string intent, int msTimeout = 250)
	{
		LockWrapper lockWrapper = LockWrapper.TryGetReaderLock(_getUnderlyingLock(), msTimeout, intent, null, (LockWrapper l) =>
		{
			RemoveLock(l);
		});
		if (lockWrapper != null)
		{
			_logger.LogInformation("TryGetReaderLock for {Name} succeeded. Intent: {Intent}", Name, intent);
			AddLock(lockWrapper);
		}
		else
		{
			_logger.LogInformation("TryGetReaderLock for {Name} FAILED. Intent: {Intent}", Name, intent);
		}
		return lockWrapper;
	}

	public bool IsWriteLockHeld()
	{
		return WriteLock != null;
	}

	public LockWrapper TryGetWriterLock(string intent, int msTimeout = 250)
	{
		LockWrapper writeLock = WriteLock;
		if (writeLock != null)
		{
			_logger.LogInformation("Tried to acquire write lock for {Intent} with one already in use for {OtherIntent}", intent, writeLock.TaskName);
			return null;
		}
		LockWrapper lockWrapper = LockWrapper.TryGetWriterLock(_getUnderlyingLock(), msTimeout, intent, (LockWrapper l) =>
		{
			ClearWriteLock(l);
		}, null);
		if (lockWrapper != null)
		{
			_logger.LogInformation("TryGetWriterLock for {Name} succeeded. Intent: {Intent}", Name, intent);
			SetWriteLock(lockWrapper);
		}
		else
		{
			_logger.LogInformation("TryGetWriterLock for {Name} FAILED. Intent: {Intent}", Name, intent);
		}
		return lockWrapper;
	}

	public async Task<LockWrapper> TryGetWriterLockAsync(int msTimeout, int nAttempts, int retryTimeoutMs, string intent)
	{
		for (int i = 0; i <= nAttempts; i++)
		{
			LockWrapper lockWrapper = TryGetWriterLock(intent, msTimeout);
			if (lockWrapper != null)
			{
				return lockWrapper;
			}
			if (i != nAttempts)
			{
				await Task.Delay(retryTimeoutMs);
			}
		}
		return null;
	}

	private void AddLock(LockWrapper lw)
	{
		lock (OutstandingLocks)
		{
			OutstandingLocks.Add(lw);
		}
		OnLockChanged?.Invoke(null, null);
	}

	private void RemoveLock(LockWrapper lw)
	{
		lock (OutstandingLocks)
		{
			OutstandingLocks.Remove(lw);
		}
		OnLockChanged?.Invoke(null, null);
	}

	private void SetWriteLock(LockWrapper lw)
	{
		lock (_writeLockLock)
		{
			if (WriteLock != null)
			{
				throw new Exception("Cannot assign a 2nd write-lock when there is one already present.");
			}
			_writeLock = lw;
		}
		OnLockChanged?.Invoke(null, null);
	}

	private void ClearWriteLock(LockWrapper lw)
	{
		lock (_writeLockLock)
		{
			if (WriteLock != lw)
			{
				throw new Exception("Clearing WriteLock with incorrect wrapper");
			}
			_writeLock = null;
		}
		OnLockChanged?.Invoke(null, null);
	}
}
