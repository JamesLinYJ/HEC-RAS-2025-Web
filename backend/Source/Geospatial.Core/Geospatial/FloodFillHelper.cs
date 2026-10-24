using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Geospatial;

public class FloodFillHelper
{
	private interface ICanIndex
	{
		bool GetValue(int idx);

		void SetValue(int idx);
	}

	private class ArrayCanIndex : ICanIndex
	{
		private readonly bool[] _data;

		public ArrayCanIndex(bool[] data)
		{
			_data = data;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool GetValue(int idx)
		{
			return _data[idx];
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SetValue(int idx)
		{
			_data[idx] = true;
		}
	}

	private class HashSetCanIndex : ICanIndex
	{
		private readonly HashSet<int> _data;

		public HashSetCanIndex(HashSet<int> data)
		{
			_data = data;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool GetValue(int idx)
		{
			return _data.Contains(idx);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SetValue(int idx)
		{
			_data.Add(idx);
		}
	}

	private class BACanIndex : ICanIndex
	{
		private readonly BitArray _data;

		public BACanIndex(BitArray data)
		{
			_data = data;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool GetValue(int idx)
		{
			return _data[idx];
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SetValue(int idx)
		{
			_data[idx] = true;
		}
	}

	private readonly Func<int, Memory<int>> _getNeighbors;

	private readonly Func<int, bool> _includeTest;

	private readonly Func<int, int, bool> _fromToTest;

	public FloodFillHelper(Func<int, Memory<int>> getNeighbors, Func<int, bool> includeTest)
	{
		_getNeighbors = getNeighbors;
		_includeTest = includeTest;
	}

	public FloodFillHelper(Func<int, Memory<int>> getNeighbors, Func<int, int, bool> fromToIncludeTest)
	{
		_getNeighbors = getNeighbors;
		_fromToTest = fromToIncludeTest;
	}

	public HashSet<int> Start(IEnumerable<int> seeds, bool emitSeeds = false)
	{
		Start(seeds, out var hs, emitSeeds);
		return hs;
	}

	public void Start(IEnumerable<int> seeds, out HashSet<int> hs, bool emitSeeds = false)
	{
		hs = new HashSet<int>();
		if (emitSeeds)
		{
			foreach (int seed in seeds)
			{
				hs.Add(seed);
			}
		}
		if (_includeTest != null)
		{
			StartInternal_Independent(seeds, new HashSetCanIndex(hs));
		}
		else
		{
			StartInternal_FromTo(seeds, new HashSetCanIndex(hs));
		}
	}

	public bool TryFind(int seed, Func<int, bool> finder, out int foundValue)
	{
		return TryFind(new int[1] { seed }, finder, out foundValue);
	}

	public bool TryFind(IEnumerable<int> seeds, Func<int, bool> finder, out int foundValue)
	{
		if (_includeTest != null)
		{
			throw new NotImplementedException();
		}
		return FindInternal_FromTo(seeds, finder, out foundValue);
	}

	public void Start(IEnumerable<int> seeds, BitArray ba, bool emitSeeds = false)
	{
		if (emitSeeds)
		{
			foreach (int seed in seeds)
			{
				ba[seed] = true;
			}
		}
		BACanIndex tracker = new BACanIndex(ba);
		if (_includeTest != null)
		{
			StartInternal_Independent(seeds, tracker);
		}
		else
		{
			StartInternal_FromTo(seeds, tracker);
		}
	}

	private void StartInternal_Independent<T>(IEnumerable<int> seeds, T tracker) where T : ICanIndex
	{
		HashSet<int> intSet = new HashSet<int>();
		Stack<int> intStack = new Stack<int>();
		foreach (int seed in seeds)
		{
			intSet.Add(seed);
		}
		Memory<int> getNeighbors;
		foreach (int seed2 in seeds)
		{
			getNeighbors = _getNeighbors(seed2);
			Span<int> getNeighborsSpan = getNeighbors.Span;
			for (int i = 0; i < getNeighborsSpan.Length; i++)
			{
				int getNeighbor = getNeighborsSpan[i];
				if (intSet.Add(getNeighbor))
				{
					intStack.Push(getNeighbor);
				}
			}
		}
		int result;
		while (intStack.TryPop(out result))
		{
			intSet.Add(result);
			if (!_includeTest(result))
			{
				continue;
			}
			tracker.SetValue(result);
			getNeighbors = _getNeighbors(result);
			Span<int> getNeighbors2 = getNeighbors.Span;
			for (int i = 0; i < getNeighbors2.Length; i++)
			{
				int getNeighbor2 = getNeighbors2[i];
				if (!intSet.Contains(getNeighbor2))
				{
					intStack.Push(getNeighbor2);
				}
			}
		}
	}

	private void StartInternal_FromTo<T>(IEnumerable<int> seeds, T tracker) where T : ICanIndex
	{
		HashSet<int> intSet = new HashSet<int>();
		Stack<(int, int)> valueTuples = new Stack<(int, int)>();
		foreach (int seed in seeds)
		{
			intSet.Add(seed);
		}
		Memory<int> getNeighbors;
		foreach (int seed2 in seeds)
		{
			getNeighbors = _getNeighbors(seed2);
			Span<int> getNeighborsSpan = getNeighbors.Span;
			for (int i = 0; i < getNeighborsSpan.Length; i++)
			{
				int getNeighbor = getNeighborsSpan[i];
				if (!intSet.Contains(getNeighbor))
				{
					valueTuples.Push((seed2, getNeighbor));
				}
			}
		}
		(int, int) result;
		while (valueTuples.TryPop(out result))
		{
			var (localValue, localValue2) = result;
			if (!_fromToTest(localValue, localValue2))
			{
				continue;
			}
			intSet.Add(localValue2);
			tracker.SetValue(localValue2);
			getNeighbors = _getNeighbors(localValue2);
			Span<int> getNeighbors2 = getNeighbors.Span;
			for (int i = 0; i < getNeighbors2.Length; i++)
			{
				int getNeighbor2 = getNeighbors2[i];
				if (!intSet.Contains(getNeighbor2))
				{
					valueTuples.Push((localValue2, getNeighbor2));
				}
			}
		}
	}

	private bool FindInternal_FromTo(IEnumerable<int> seeds, Func<int, bool> checkValue, out int foundValue)
	{
		HashSet<int> intSet = new HashSet<int>();
		Stack<(int, int)> valueTuples = new Stack<(int, int)>();
		foreach (int seed in seeds)
		{
			intSet.Add(seed);
		}
		Memory<int> getNeighbors;
		foreach (int seed2 in seeds)
		{
			if (checkValue(seed2))
			{
				foundValue = seed2;
				return true;
			}
			getNeighbors = _getNeighbors(seed2);
			Span<int> getNeighborsSpan = getNeighbors.Span;
			for (int i = 0; i < getNeighborsSpan.Length; i++)
			{
				int getNeighbor = getNeighborsSpan[i];
				if (!intSet.Contains(getNeighbor))
				{
					valueTuples.Push((seed2, getNeighbor));
				}
			}
		}
		(int, int) result;
		while (valueTuples.TryPop(out result))
		{
			var (localValue, foundValue2) = result;
			if (!_fromToTest(localValue, foundValue2))
			{
				continue;
			}
			if (checkValue(foundValue2))
			{
				foundValue = foundValue2;
				return true;
			}
			intSet.Add(foundValue2);
			getNeighbors = _getNeighbors(foundValue2);
			Span<int> getNeighbors2 = getNeighbors.Span;
			for (int i = 0; i < getNeighbors2.Length; i++)
			{
				int getNeighbor2 = getNeighbors2[i];
				if (!intSet.Contains(getNeighbor2))
				{
					valueTuples.Push((foundValue2, getNeighbor2));
				}
			}
		}
		foundValue = -1;
		return false;
	}
}
