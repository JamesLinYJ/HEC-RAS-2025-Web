using System;
using System.Threading;

namespace Utility.Logging;

public sealed class LogRingBuffer<T>
{
	private struct Slot
	{
		public int Sequence;

		public T Value;
	}

	private readonly Slot[] _slots;

	private readonly int _mask;

	private PaddedPosition _enqueue;

	private PaddedPosition _dequeue;

	public int Capacity => _slots.Length;

	public int Count => Math.Max(0, Volatile.Read(in _enqueue.Value) - Volatile.Read(in _dequeue.Value));

	public bool IsEmpty => Volatile.Read(in _enqueue.Value) - Volatile.Read(in _dequeue.Value) <= 0;

	public LogRingBuffer(int capacity)
	{
		int roundUpToPowerOfTwo = RoundUpToPowerOfTwo(Math.Max(2, capacity));
		_slots = new Slot[roundUpToPowerOfTwo];
		_mask = roundUpToPowerOfTwo - 1;
		for (int i = 0; i < roundUpToPowerOfTwo; i++)
		{
			_slots[i].Sequence = i;
		}
		_enqueue.Value = 0;
		_dequeue.Value = 0;
	}

	public bool TryEnqueue(in T localValue)
	{
		Slot[] slots = _slots;
		int read = Volatile.Read(in _enqueue.Value);
		while (true)
		{
			ref Slot reference = ref slots[read & _mask];
			int readSequenceTerm = Volatile.Read(in reference.Sequence) - read;
			if (readSequenceTerm == 0)
			{
				int compareExchange = Interlocked.CompareExchange(ref _enqueue.Value, read + 1, read);
				if (compareExchange == read)
				{
					reference.Value = localValue;
					Volatile.Write(ref reference.Sequence, read + 1);
					return true;
				}
				read = compareExchange;
			}
			else
			{
				if (readSequenceTerm < 0)
				{
					break;
				}
				read = Volatile.Read(in _enqueue.Value);
			}
		}
		return false;
	}

	public bool TryDequeue(out T referenceTerm)
	{
		Slot[] slots = _slots;
		int read = Volatile.Read(in _dequeue.Value);
		while (true)
		{
			ref Slot reference = ref slots[read & _mask];
			int readSequenceTerm = Volatile.Read(in reference.Sequence) - (read + 1);
			if (readSequenceTerm == 0)
			{
				int compareExchange = Interlocked.CompareExchange(ref _dequeue.Value, read + 1, read);
				if (compareExchange == read)
				{
					referenceTerm = reference.Value;
					reference.Value = default;
					Volatile.Write(ref reference.Sequence, read + _mask + 1);
					return true;
				}
				read = compareExchange;
			}
			else
			{
				if (readSequenceTerm < 0)
				{
					break;
				}
				read = Volatile.Read(in _dequeue.Value);
			}
		}
		referenceTerm = default;
		return false;
	}

	private static int RoundUpToPowerOfTwo(int ordinal)
	{
		int ordinal2 = 2;
		while (ordinal2 < ordinal && ordinal2 < 1073741824)
		{
			ordinal2 <<= 1;
		}
		return ordinal2;
	}
}
