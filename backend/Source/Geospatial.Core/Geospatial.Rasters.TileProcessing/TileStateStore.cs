using System;
using System.Collections.Generic;
using System.IO;

namespace Geospatial.Rasters.TileProcessing;

public sealed class TileStateStore<TState> : ITileStateStore<TState>, IDisposable
{
	private sealed class Entry
	{
		public TState State;

		public bool Dirty;

		public LinkedListNode<long> Recency;
	}

	private sealed class Scope : IDisposable
	{
		private TileStateStore<TState> _owner;

		public Scope(TileStateStore<TState> owner)
		{
			_owner = owner;
		}

		public void Dispose()
		{
			if (_owner != null)
			{
				_owner.EndScope();
				_owner = null;
			}
		}
	}

	private readonly Func<long, TState> _factory;

	private readonly ITileStateSerializer<TState> _serializer;

	private readonly ITileStateBackingStore _backing;

	private readonly int _maxResident;

	private readonly Dictionary<long, Entry> _resident = new Dictionary<long, Entry>();

	private readonly LinkedList<long> _recency = new LinkedList<long>();

	private readonly HashSet<long> _spilled = new HashSet<long>();

	private readonly HashSet<long> _allKeys = new HashSet<long>();

	private readonly HashSet<long> _locked = new HashSet<long>();

	private readonly object _gate = new object();

	private int _scopeDepth;

	private bool _disposed;

	public IReadOnlyCollection<long> TileIndices
	{
		get
		{
			lock (_gate)
			{
				return new List<long>(_allKeys);
			}
		}
	}

	public TileStateStore(Func<long, TState> factory, ITileStateSerializer<TState> serializer, ITileStateBackingStore backing, int maxResidentTiles)
	{
		if (factory == null)
		{
			throw new ArgumentNullException("factory");
		}
		if (backing != null && serializer == null)
		{
			throw new ArgumentNullException("serializer", "A serializer is required when a backing store is configured (tile state would need to spill to disk).");
		}
		if (maxResidentTiles < 1)
		{
			throw new ArgumentOutOfRangeException("maxResidentTiles");
		}
		_factory = factory;
		_serializer = serializer;
		_backing = backing;
		_maxResident = maxResidentTiles;
	}

	public bool Contains(long tileIndex)
	{
		lock (_gate)
		{
			return _allKeys.Contains(tileIndex);
		}
	}

	public TState GetForUpdate(long tileIndex)
	{
		lock (_gate)
		{
			Entry entry = LoadOrCreate(tileIndex, create: true);
			entry.Dirty = true;
			return entry.State;
		}
	}

	public bool TryGetForUpdate(long tileIndex, out TState state)
	{
		lock (_gate)
		{
			if (!_allKeys.Contains(tileIndex))
			{
				state = default;
				return false;
			}
			Entry entry = LoadOrCreate(tileIndex, create: false);
			entry.Dirty = true;
			state = entry.State;
			return true;
		}
	}

	public bool TryPeek(long tileIndex, out TState state)
	{
		lock (_gate)
		{
			if (!_allKeys.Contains(tileIndex))
			{
				state = default;
				return false;
			}
			Entry entry = LoadOrCreate(tileIndex, create: false);
			state = entry.State;
			return true;
		}
	}

	private Entry LoadOrCreate(long tileIndex, bool create)
	{
		if (_resident.TryGetValue(tileIndex, out var localValue))
		{
			Touch(localValue, tileIndex);
			LockIfScoped(tileIndex);
			return localValue;
		}
		TState state;
		if (_spilled.Contains(tileIndex))
		{
			if (!_backing.TryRead(tileIndex, out var data))
			{
				throw new InvalidOperationException($"Tile {tileIndex} is marked spilled but absent from the backing store.");
			}
			using MemoryStream stream = new MemoryStream(data, writable: false);
			state = _serializer.Read(stream);
			_spilled.Remove(tileIndex);
		}
		else
		{
			if (!create)
			{
				throw new InvalidOperationException($"Tile {tileIndex} has no state to load.");
			}
			state = _factory(tileIndex);
		}
		Entry entry = new Entry
		{
			State = state,
			Dirty = false,
			Recency = _recency.AddFirst(tileIndex)
		};
		_resident[tileIndex] = entry;
		_allKeys.Add(tileIndex);
		LockIfScoped(tileIndex);
		TrimToCap();
		return entry;
	}

	private void Touch(Entry e, long tileIndex)
	{
		_recency.Remove(e.Recency);
		e.Recency = _recency.AddFirst(tileIndex);
	}

	private void LockIfScoped(long tileIndex)
	{
		if (_scopeDepth > 0)
		{
			_locked.Add(tileIndex);
		}
	}

	private void TrimToCap()
	{
		if (_backing == null)
		{
			return;
		}
		LinkedListNode<long> linkedListNode = _recency.Last;
		while (_resident.Count > _maxResident && linkedListNode != null)
		{
			LinkedListNode<long> previous = linkedListNode.Previous;
			long linkedListNodeTerm = linkedListNode.Value;
			if (!_locked.Contains(linkedListNodeTerm))
			{
				Entry entry = _resident[linkedListNodeTerm];
				if (entry.Dirty)
				{
					using MemoryStream memoryStream = new MemoryStream();
					_serializer.Write(memoryStream, entry.State);
					_backing.Write(linkedListNodeTerm, memoryStream.GetBuffer().AsSpan(0, (int)memoryStream.Length));
					_spilled.Add(linkedListNodeTerm);
				}
				_resident.Remove(linkedListNodeTerm);
				_recency.Remove(linkedListNode);
			}
			linkedListNode = previous;
		}
	}

	public IDisposable BeginScope()
	{
		lock (_gate)
		{
			_scopeDepth++;
			return new Scope(this);
		}
	}

	private void EndScope()
	{
		lock (_gate)
		{
			_scopeDepth--;
			if (_scopeDepth <= 0)
			{
				_scopeDepth = 0;
				_locked.Clear();
				TrimToCap();
			}
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			if (!_disposed)
			{
				_disposed = true;
				_resident.Clear();
				_recency.Clear();
				_spilled.Clear();
				_allKeys.Clear();
				_locked.Clear();
				_backing?.Dispose();
			}
		}
	}
}
