using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using Utility.Helpers;

namespace Geospatial.Rendering;

public class KeyedProfileState : IProfileState, IDisposable
{
	private readonly IProfileState _keyState;

	private readonly List<IProfileState> _children;

	private readonly Dictionary<IProfileState, int> _nearestIndexHints = new Dictionary<IProfileState, int>();

	private readonly ReadOnlyDictionary<IProfileState, int> _nearestIndexHintsView;

	private readonly TimeSpan _timestampTolerance;

	private int _currentProfileIndex;

	private long _version = 1L;

	private bool _suppressKeyNotifications;

	private bool _disposed;

	private readonly List<WeakHandler> _childProfileStructureHandlers = new List<WeakHandler>();

	private readonly WeakHandler _keyProfileChangedHandler;

	private readonly WeakHandler _keyProfileStructureChangedHandler;

	private static readonly TimeSpan DefaultTimestampTolerance = TimeSpan.FromMilliseconds(10L);

	public IProfileState KeyState => _keyState;

	public IReadOnlyList<IProfileState> Children => _children;

	public IReadOnlyDictionary<IProfileState, int> NearestIndexHints => _nearestIndexHintsView;

	public int CurrentProfileIndex
	{
		get
		{
			return _currentProfileIndex;
		}
		set
		{
			if (_currentProfileIndex == value)
			{
				return;
			}
			_currentProfileIndex = value;
			try
			{
				_suppressKeyNotifications = true;
				if (_keyState.CurrentProfileIndex != value)
				{
					_keyState.CurrentProfileIndex = value;
				}
			}
			finally
			{
				_suppressKeyNotifications = false;
			}
			SyncChildrenToKeyState();
			RaiseProfileChanged();
		}
	}

	public int ProfileCount => _keyState.ProfileCount;

	public IReadOnlyList<string> ProfileNames => _keyState.ProfileNames;

	public long Version => _version;

	public bool IsRegularProfile => _keyState.IsRegularProfile;

	public bool IsNull => _keyState.IsNull;

	public event ProfileState.ProfileChangedEvent ProfileChanged;

	public event ProfileState.ProfileStructureChangedEvent ProfileStructureChanged;

	public KeyedProfileState(IProfileState keyState, IEnumerable<IProfileState> additionalStates, TimeSpan? timestampTolerance = null)
	{
		_keyState = keyState ?? throw new ArgumentNullException("keyState");
		_children = new List<IProfileState>();
		if (additionalStates != null)
		{
			foreach (IProfileState additionalState in additionalStates)
			{
				if (additionalState != null && additionalState != keyState)
				{
					_children.Add(additionalState);
				}
			}
		}
		_timestampTolerance = timestampTolerance ?? DefaultTimestampTolerance;
		_currentProfileIndex = keyState.CurrentProfileIndex;
		_nearestIndexHintsView = new ReadOnlyDictionary<IProfileState, int>(_nearestIndexHints);
		_keyProfileChangedHandler = _keyState.SubscribeProfileChanged(this, (KeyedProfileState ths, int idx) =>
		{
			ths.OnKeyProfileChanged(idx);
		});
		_keyProfileStructureChangedHandler = _keyState.SubscribeProfileStructureChanged(this, (KeyedProfileState ths) =>
		{
			ths.OnKeyProfileStructureChanged();
		});
		foreach (IProfileState child in _children)
		{
			WeakHandler weakHandler = child.SubscribeProfileStructureChanged(this, (KeyedProfileState ths) =>
			{
				ths.OnChildProfileStructureChanged();
			});
			if (weakHandler != null)
			{
				_childProfileStructureHandlers.Add(weakHandler);
			}
		}
	}

	private void OnKeyProfileChanged(int newIndex)
	{
		if (!_suppressKeyNotifications)
		{
			CurrentProfileIndex = newIndex;
		}
	}

	private void OnKeyProfileStructureChanged()
	{
		SyncChildrenToKeyState();
		ProfileStructureChanged?.Invoke();
	}

	private void OnChildProfileStructureChanged()
	{
		SyncChildrenToKeyState();
		ProfileStructureChanged?.Invoke();
	}

	public void ForcePushStateToChildren()
	{
		SyncChildrenToKeyState();
	}

	private void SyncChildrenToKeyState()
	{
		if (_children.Count == 0)
		{
			return;
		}
		if (!IsRegularProfileIndex(_currentProfileIndex))
		{
			foreach (IProfileState child in _children)
			{
				child.CurrentProfileIndex = _currentProfileIndex;
			}
			_nearestIndexHints.Clear();
			return;
		}
		if (!DateTime.TryParse(_keyState.ProfileNames[_currentProfileIndex], out var result))
		{
			SetChildrenToNull();
			return;
		}
		foreach (IProfileState child2 in _children)
		{
			if (child2.ProfileCount == 0)
			{
				child2.CurrentProfileIndex = -1;
				_nearestIndexHints[child2] = -1;
				continue;
			}
			(int, int) tuple = FindMatchingIndex(child2, result, _timestampTolerance);
			if (tuple.Item1 >= 0)
			{
				(child2.CurrentProfileIndex, _) = tuple;
				_nearestIndexHints[child2] = tuple.Item1;
			}
			else
			{
				child2.CurrentProfileIndex = -1;
				_nearestIndexHints[child2] = tuple.Item2;
			}
		}
	}

	private void SetChildrenToNull()
	{
		foreach (IProfileState child in _children)
		{
			child.CurrentProfileIndex = -1;
		}
		_nearestIndexHints.Clear();
	}

	private static (int MatchIndex, int NearestIndex) FindMatchingIndex(IProfileState child, DateTime target, TimeSpan tolerance)
	{
		int ordinal = -1;
		long maxValue = long.MaxValue;
		for (int i = 0; i < child.ProfileCount; i++)
		{
			if (DateTime.TryParse(child.ProfileNames[i], out var result))
			{
				long maxValue2 = Math.Abs(result.Ticks - target.Ticks);
				if (maxValue2 < maxValue)
				{
					maxValue = maxValue2;
					ordinal = i;
				}
				if (maxValue2 <= tolerance.Ticks)
				{
					return (MatchIndex: i, NearestIndex: ordinal);
				}
			}
		}
		return (MatchIndex: -1, NearestIndex: ordinal);
	}

	private void RaiseProfileChanged()
	{
		Interlocked.Increment(ref _version);
		ProfileChanged?.Invoke(_currentProfileIndex);
	}

	private bool IsRegularProfileIndex(int idx)
	{
		if (idx >= 0)
		{
			return idx < _keyState.ProfileCount;
		}
		return false;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_keyProfileChangedHandler?.Unsubscribe();
		_keyProfileStructureChangedHandler?.Unsubscribe();
		foreach (WeakHandler childProfileStructureHandler in _childProfileStructureHandlers)
		{
			childProfileStructureHandler?.Unsubscribe();
		}
		_nearestIndexHints.Clear();
		_childProfileStructureHandlers.Clear();
		_disposed = true;
		GC.SuppressFinalize(this);
	}
}
