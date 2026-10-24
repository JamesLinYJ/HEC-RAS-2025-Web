using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Utility.DataStructures;
using Utility.Interfaces;
using Utility.Logging;
using Utility.Threading;

namespace Ras.Observables;

public abstract class ObservableBaseLayer : ObservableValidator, IReadOnlyDisplayName, IFilename
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ObservableBaseLayer");

	public const int WriteLockTimeoutMS = 250;

	public const int ExtendedWriteLockTimeoutMS = 1000;
	private AdvancedCollection<LockWrapper> _outstandingLocks;

	private LockWrapper _writeLock;

	private IReadOnlyDisplayName _lyr;

	private readonly object _writeLockLock = new object();

	public LockWrapper WriteLock => _writeLock;

	public LockHelper LockHelper { get; }

	public bool IsLocked
	{
		get
		{
			lock (OutstandingLocks)
			{
				return OutstandingLocks.Count > 0 || LockHelper.IsWriteLockHeld();
			}
		}
	}

	public bool IsReadLockHeld => Lock.IsReadLockHeld;

	public bool IsWriteLockHeld => Lock.IsWriteLockHeld;

	private ReaderWriterLockSlim Lock { get; } = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

	string IReadOnlyDisplayName.DisplayName => _lyr.DisplayName;

	public ObservableProject Project { get; }

	public abstract string Filename { get; }

	public abstract bool IsInMemory { get; }

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public AdvancedCollection<LockWrapper> OutstandingLocks
	{
		get
		{
			return _outstandingLocks;
		}
		[MemberNotNull("_outstandingLocks")]
		set
		{
			if (!EqualityComparer<AdvancedCollection<LockWrapper>>.Default.Equals(_outstandingLocks, value))
			{
				OnPropertyChanging("OutstandingLocks");
				_outstandingLocks = value;
				OnPropertyChanged("OutstandingLocks");
			}
		}
	}

	public ObservableBaseLayer(ObservableProject project, IReadOnlyDisplayName lyr)
	{
		Project = project;
		_lyr = lyr;
		LockHelper = new LockHelper(() => Lock, lyr.DisplayName);
		OutstandingLocks = new AdvancedCollection<LockWrapper>();
		OutstandingLocks.CollectionChanged += (object? _, NotifyCollectionChangedEventArgs _) =>
		{
			OnPropertyChanged("IsLocked");
		};
	}

	protected abstract void DeleteSelfInternal();

	public void DeleteSelfAndRemoveFromProject()
	{
		if (LockHelper.IsWriteLockHeld())
		{
			DeleteSelfInternal();
			return;
		}
		using LockWrapper lockWrapper = LockHelper.TryGetWriterLock("Delete this layer and all input files.", 1000);
		if (lockWrapper == null)
		{
			throw new Exception("Could not get an exclusive write-lock to the layer.");
		}
		DeleteSelfInternal();
	}

	public LockWrapper TryGetReaderLock(string intent, int msTimeout = 250)
	{
		LockWrapper lockWrapper = LockWrapper.TryGetReaderLock(Lock, msTimeout, intent, null, (LockWrapper l) =>
		{
			RemoveLock(l);
		});
		if (lockWrapper != null)
		{
			_logger.LogInformation("TryGetReaderLock for {Name} succeeded. Intent: {Intent}", _lyr.DisplayName, intent);
			AddLock(lockWrapper);
		}
		else
		{
			_logger.LogInformation("TryGetReaderLock for {Name} FAILED. Intent: {Intent}", _lyr.DisplayName, intent);
		}
		return lockWrapper;
	}

	public LockWrapper TryGetWriterLock(string intent, int msTimeout = 250)
	{
		LockWrapper writeLock = WriteLock;
		if (writeLock != null)
		{
			_logger.LogInformation("Tried to acquire write lock for {Intent} with one already in use for {OtherIntent}", intent, writeLock.TaskName);
			return null;
		}
		LockWrapper lockWrapper = LockWrapper.TryGetWriterLock(Lock, msTimeout, intent, (LockWrapper l) =>
		{
			ClearWriteLock(l);
		}, null);
		if (lockWrapper != null)
		{
			_logger.LogInformation("TryGetWriterLock for {Name} succeeded. Intent: {Intent}", _lyr.DisplayName, intent);
			SetWriteLock(lockWrapper);
		}
		else
		{
			_logger.LogInformation("TryGetWriterLock for {Name} FAILED. Intent: {Intent}", _lyr.DisplayName, intent);
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
	}

	private void RemoveLock(LockWrapper lw)
	{
		lock (OutstandingLocks)
		{
			OutstandingLocks.Remove(lw);
		}
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
		OnPropertyChanged("WriteLock");
		OnPropertyChanged("IsLocked");
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
		OnPropertyChanged("WriteLock");
		OnPropertyChanged("IsLocked");
	}
}
