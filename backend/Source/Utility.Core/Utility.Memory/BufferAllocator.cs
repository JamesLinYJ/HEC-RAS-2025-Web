using System;

namespace Utility.Memory;

public class BufferAllocator<T>
{
	private T[] _buffer;

	private int _position;

	private readonly int _minChunkSize;

	private readonly int _defaultChunkSize;

	private readonly int[] _history;

	private int _historyIndex;

	private int _historyCount;

	private readonly int _usersPerBufferTarget;

	public BufferAllocator(int minChunkSize = 10, int defaultChunkSize = 100, int historySize = 100, int usersPerBufferTarget = 100)
	{
		_minChunkSize = minChunkSize;
		_defaultChunkSize = defaultChunkSize;
		_buffer = new T[_defaultChunkSize];
		_position = 0;
		_history = new int[historySize];
		_historyIndex = 0;
		_historyCount = 0;
		_usersPerBufferTarget = usersPerBufferTarget;
	}

	public Memory<T> Get(int size)
	{
		if (size > _buffer.Length - _position)
		{
			AllocateNewBuffer(size);
		}
		Memory<T> result = new Memory<T>(_buffer, _position, size);
		_position += size;
		RecordRequest(size);
		return result;
	}

	private void AllocateNewBuffer(int requiredSize)
	{
		int recommendedBufferSize = GetRecommendedBufferSize(requiredSize);
		_buffer = new T[recommendedBufferSize];
		_position = 0;
	}

	private void RecordRequest(int size)
	{
		_history[_historyIndex] = size;
		_historyIndex = (_historyIndex + 1) % _history.Length;
		if (_historyCount < _history.Length)
		{
			_historyCount++;
		}
	}

	private int GetRecommendedBufferSize(int requiredSize)
	{
		if (_historyCount == 0)
		{
			return Math.Max(_defaultChunkSize, requiredSize);
		}
		long historySum = 0L;
		for (int i = 0; i < _historyCount; i++)
		{
			historySum += _history[i];
		}
		int historySumHistoryCountTerm = (int)Math.Max((double)historySum / (double)_historyCount * (double)_usersPerBufferTarget, requiredSize);
		return Math.Max(_minChunkSize, historySumHistoryCountTerm);
	}
}
