using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace H5Assist.Chunking.VB;

public class Chunk2D<T> : Chunk<T> where T : struct
{
	private List<T[]> _data;

	private int _startIdx;

	private int _endIdx;

	private int _lineLength;

	private int _sizeOfT = Marshal.SizeOf(new T());

	public Chunk2D(List<T[]> chunk, int startIdx)
	{
		if (chunk == null)
		{
			throw new ArgumentNullException("chunk");
		}
		if (chunk.Count == 0)
		{
			throw new ArgumentException("chunk");
		}
		UpdateLastAccessedDate();
		_data = chunk;
		_startIdx = startIdx;
		_endIdx = startIdx + chunk.Count - 1;
		_lineLength = chunk[0].Length;
	}

	public bool ContainsLine(int idx)
	{
		if (idx >= _startIdx && idx <= _endIdx)
		{
			return true;
		}
		return false;
	}

	public T[] GetLine(int idx)
	{
		if (idx > _endIdx || idx < _startIdx)
		{
			throw new IndexOutOfRangeException();
		}
		UpdateLastAccessedDate();
		T[] values = _data[idx - _startIdx];
		T[] line = new T[values.Length];
		Buffer.BlockCopy(values, 0, line, 0, values.Length * _sizeOfT);
		return line;
	}

	public bool TryGetLine(int idx, ref T[] values)
	{
		if (!ContainsLine(idx))
		{
			return false;
		}
		try
		{
			values = GetLine(idx);
		}
		catch
		{
			return false;
		}
		return true;
	}

	public override int GetChunkSize()
	{
		return _data.Count * _lineLength * _sizeOfT;
	}

	public static Chunk2D<T> ParseColumns(T[,] values, int startIdx)
	{
		int lineLength = values.GetLength(0);
		List<T[]> valList = new List<T[]>();
		for (int i = 0; i < values.GetLength(1); i++)
		{
			valList.Add(null);
		}
		Parallel.For(0, values.GetLength(1), (int col) =>
		{
			T[] valListArray = new T[lineLength];
			for (int j = 0; j < lineLength; j++)
			{
				valListArray[j] = values[j, col];
			}
			valList[col] = valListArray;
		});
		return new Chunk2D<T>(valList, startIdx);
	}

	public static Chunk2D<T> ParseRows(T[,] values, int startIdx)
	{
		int length = values.GetLength(1);
		List<T[]> valuesList = new List<T[]>();
		for (int i = 0; i < values.GetLength(0); i++)
		{
			T[] valuesArray = new T[length];
			for (int j = 0; j < length; j++)
			{
				valuesArray[j] = values[i, j];
			}
			valuesList.Add(valuesArray);
		}
		return new Chunk2D<T>(valuesList, startIdx);
	}
}
