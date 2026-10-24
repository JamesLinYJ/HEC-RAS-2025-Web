using System.Collections;
using System.Collections.Generic;

namespace Utility.DataStructures;

public class ArrayWrapper2D<T> : IArray<T>, IEnumerable<T>, IEnumerable
{
	public T this[int index]
	{
		get
		{
			return BackingBuffer[index / Width, index % Width];
		}
		set
		{
			BackingBuffer[index / Width, index % Width] = value;
		}
	}

	public int Count => BackingBuffer.Length;

	public T[,] BackingBuffer { get; private set; }

	public int Height => BackingBuffer.GetLength(0);

	public int Width => BackingBuffer.GetLength(1);

	public ArrayWrapper2D(T[,] data)
	{
		BackingBuffer = data;
	}

	public T First()
	{
		return BackingBuffer[0, 0];
	}

	public T Last()
	{
		return BackingBuffer[Height - 1, Width - 1];
	}

	public List<T> ToList()
	{
		int count = Count;
		int width = Width;
		int height = Height;
		List<T> items = new List<T>(count);
		for (int i = 0; i < height; i++)
		{
			for (int j = 0; j < width; j++)
			{
				items.Add(BackingBuffer[i, j]);
			}
		}
		return items;
	}

	public IEnumerator<T> GetEnumerator()
	{
		int w = Width;
		int h = Height;
		for (int r = 0; r < h; r++)
		{
			for (int c = 0; c < w; c++)
			{
				yield return BackingBuffer[r, c];
			}
		}
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return BackingBuffer.GetEnumerator();
	}
}
