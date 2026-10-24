using System;
using System.Runtime.InteropServices;

namespace H5Assist.Chunking.VB;

public class Chunk1D<T> : Chunk<T> where T : struct
{
	private T[] _data;

	public int StartIndex;

	public int EndIndex;

	private int _sizeOfT = Marshal.SizeOf(new T());

	public Chunk1D(T[] chunk, int startIdx)
	{
		if (chunk == null)
		{
			throw new ArgumentNullException("chunk");
		}
		if (chunk.Length == 0)
		{
			throw new ArgumentException("chunk");
		}
		UpdateLastAccessedDate();
		_data = chunk;
		StartIndex = startIdx;
		EndIndex = startIdx + chunk.Length - 1;
	}

	public override int GetChunkSize()
	{
		return _data.Length * _sizeOfT;
	}

	public bool ContainsItem(int idx)
	{
		if (idx >= StartIndex && idx <= EndIndex)
		{
			return true;
		}
		return false;
	}

	public T[] GetLine()
	{
		UpdateLastAccessedDate();
		T[] dst = new T[_data.Length];
		Buffer.BlockCopy(_data, 0, dst, 0, _data.Length * _sizeOfT);
		return _data;
	}

	public T GetItem(int idx)
	{
		if (idx > EndIndex || idx < StartIndex)
		{
			throw new IndexOutOfRangeException();
		}
		return _data[idx - StartIndex];
	}
}
