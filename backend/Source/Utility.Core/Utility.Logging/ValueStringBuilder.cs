using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Utility.Logging;

public ref struct ValueStringBuilder
{
	private char[] _arrayToReturnToPool;

	private Span<char> _chars;

	private int _pos;

	public int Length
	{
		get
		{
			return _pos;
		}
		set
		{
			_pos = value;
		}
	}

	public int Capacity => _chars.Length;

	public ref char this[int index] => ref _chars[index];

	public Span<char> RawChars => _chars;

	public ValueStringBuilder(Span<char> initialBuffer)
	{
		_arrayToReturnToPool = null;
		_chars = initialBuffer;
		_pos = 0;
	}

	public ValueStringBuilder(int initialCapacity)
	{
		_arrayToReturnToPool = ArrayPool<char>.Shared.Rent(initialCapacity);
		_chars = _arrayToReturnToPool;
		_pos = 0;
	}

	public void EnsureCapacity(int capacity)
	{
		if ((uint)capacity > (uint)_chars.Length)
		{
			Grow(capacity - _pos);
		}
	}

	public ref char GetPinnableReference()
	{
		return ref MemoryMarshal.GetReference(_chars);
	}

	public ref char GetPinnableReference(bool terminate)
	{
		if (terminate)
		{
			EnsureCapacity(Length + 1);
			_chars[Length] = '\0';
		}
		return ref MemoryMarshal.GetReference(_chars);
	}

	public override string ToString()
	{
		string result = _chars.Slice(0, _pos).ToString();
		Dispose();
		return result;
	}

	public ReadOnlySpan<char> AsSpan(bool terminate)
	{
		if (terminate)
		{
			EnsureCapacity(Length + 1);
			_chars[Length] = '\0';
		}
		return _chars.Slice(0, _pos);
	}

	public ReadOnlySpan<char> AsSpan()
	{
		return _chars.Slice(0, _pos);
	}

	public ReadOnlySpan<char> AsSpan(int start)
	{
		return _chars.Slice(start, _pos - start);
	}

	public ReadOnlySpan<char> AsSpan(int start, int length)
	{
		return _chars.Slice(start, length);
	}

	public bool TryCopyTo(Span<char> destination, out int charsWritten)
	{
		if (_chars.Slice(0, _pos).TryCopyTo(destination))
		{
			charsWritten = _pos;
			Dispose();
			return true;
		}
		charsWritten = 0;
		Dispose();
		return false;
	}

	public void Insert(int index, char charValue, int count)
	{
		if (_pos > _chars.Length - count)
		{
			Grow(count);
		}
		int length = _pos - index;
		_chars.Slice(index, length).CopyTo(_chars.Slice(index + count));
		_chars.Slice(index, count).Fill(charValue);
		_pos += count;
	}

	public void Insert(int index, string s)
	{
		if (s != null)
		{
			int length = s.Length;
			if (_pos > _chars.Length - length)
			{
				Grow(length);
			}
			int length2 = _pos - index;
			_chars.Slice(index, length2).CopyTo(_chars.Slice(index + length));
			s.CopyTo(_chars.Slice(index));
			_pos += length;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Append(char c)
	{
		int pos = _pos;
		if ((uint)pos < (uint)_chars.Length)
		{
			_chars[pos] = c;
			_pos = pos + 1;
		}
		else
		{
			GrowAndAppend(c);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Append(string s)
	{
		if (s != null)
		{
			int pos = _pos;
			if (s.Length == 1 && (uint)pos < (uint)_chars.Length)
			{
				_chars[pos] = s[0];
				_pos = pos + 1;
			}
			else
			{
				AppendSlow(s);
			}
		}
	}

	private void AppendSlow(string s)
	{
		int pos = _pos;
		if (pos > _chars.Length - s.Length)
		{
			Grow(s.Length);
		}
		s.CopyTo(_chars.Slice(pos));
		_pos += s.Length;
	}

	public void Append(char c, int count)
	{
		if (_pos > _chars.Length - count)
		{
			Grow(count);
		}
		Span<char> slice = _chars.Slice(_pos, count);
		for (int i = 0; i < slice.Length; i++)
		{
			slice[i] = c;
		}
		_pos += count;
	}

	public unsafe void Append(char* localValue, int length)
	{
		if (_pos > _chars.Length - length)
		{
			Grow(length);
		}
		Span<char> slice = _chars.Slice(_pos, length);
		for (int i = 0; i < slice.Length; i++)
		{
			slice[i] = *(localValue++);
		}
		_pos += length;
	}

	public void Append(ReadOnlySpan<char> charSpan)
	{
		if (_pos > _chars.Length - charSpan.Length)
		{
			Grow(charSpan.Length);
		}
		charSpan.CopyTo(_chars.Slice(_pos));
		_pos += charSpan.Length;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Span<char> AppendSpan(int length)
	{
		int pos = _pos;
		if (pos > _chars.Length - length)
		{
			Grow(length);
		}
		_pos = pos + length;
		return _chars.Slice(pos, length);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void GrowAndAppend(char c)
	{
		Grow(1);
		Append(c);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void Grow(int additionalCapacityBeyondPos)
	{
		int minimumLength = (int)Math.Max((uint)(_pos + additionalCapacityBeyondPos), Math.Min((uint)(_chars.Length * 2), 2147483591u));
		char[] arrayToReturnToPoolArray = ArrayPool<char>.Shared.Rent(minimumLength);
		_chars.Slice(0, _pos).CopyTo(arrayToReturnToPoolArray);
		char[] arrayToReturnToPool = _arrayToReturnToPool;
		_chars = (_arrayToReturnToPool = arrayToReturnToPoolArray);
		if (arrayToReturnToPool != null)
		{
			ArrayPool<char>.Shared.Return(arrayToReturnToPool);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Dispose()
	{
		char[] arrayToReturnToPool = _arrayToReturnToPool;
		this = default;
		if (arrayToReturnToPool != null)
		{
			ArrayPool<char>.Shared.Return(arrayToReturnToPool);
		}
	}
}
