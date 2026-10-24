using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace H5Assist.Caching.VB;

public class H5IndexedCache1D<T> : IH5Cache where T : struct
{
	protected string _filename;

	protected DateTime _filedate;

	private readonly H5RowCache<int> _info;

	private readonly H5Cache1D<T> _values;

	private ConcurrentDictionary<string, string> _infoValueMap;

	public string Filename => _filename;

	public List<string> ValuesDatasets => _infoValueMap.Values.ToList();

	public List<string> InfoDatasets => _infoValueMap.Keys.ToList();

	public H5IndexedCache1D(string filename)
	{
		_filename = filename;
		_info = new H5RowCache<int>(filename);
		_values = new H5Cache1D<T>(filename);
		_infoValueMap = new ConcurrentDictionary<string, string>();
	}

	public void Clear()
	{
		_infoValueMap.Clear();
		_info.Clear();
		_values.Clear();
		_filedate = File.GetLastWriteTime(_filename);
	}

	public void VerifyFileDate()
	{
		DateTime lastWriteTime = File.GetLastWriteTime(_filename);
		if (lastWriteTime != _filedate)
		{
			Clear();
			_filedate = lastWriteTime;
		}
	}

	public void AddDataset(string infoDataset, string valueDataset)
	{
		_info.EnsureDataset(infoDataset);
		_values.EnsureDataset(valueDataset);
		_infoValueMap.TryAdd(infoDataset, valueDataset);
	}

	public int GetInfoIndex(string dataset)
	{
		return _info.GetIndex(dataset);
	}

	public int GetValuesIndex(string dataset)
	{
		return _values.GetIndex(dataset);
	}

	public T[] Item(int infoDatasetIndex, int valuesDatasetIndex, int itemIndex, ref int[] infoRow)
	{
		infoRow = _info.Item(infoDatasetIndex, itemIndex);
		int infoRow2 = infoRow[0];
		int infoRow3 = infoRow[1];
		int infoRow2InfoRow3Term = infoRow2 + infoRow3 - 1;
		int lineIndex = _values.GetLineIndex(valuesDatasetIndex, infoRow2);
		int lineIndex2 = _values.GetLineIndex(valuesDatasetIndex, infoRow2InfoRow3Term);
		List<int> intList = new List<int>();
		List<T[]> lineContainings = new List<T[]>();
		for (int i = lineIndex; i <= lineIndex2; i++)
		{
			int startItemIdx = 0;
			T[] lineContaining = _values.GetLineContaining(valuesDatasetIndex, i, ref startItemIdx);
			intList.Add(startItemIdx);
			lineContainings.Add(lineContaining);
		}
		T[] values = new T[infoRow3];
		int sizeOf = Marshal.SizeOf(new T());
		if (lineIndex == lineIndex2)
		{
			T[] src = lineContainings[0];
			int ordinal = intList[0];
			int infoRow2Term = infoRow2 - ordinal;
			Buffer.BlockCopy(src, infoRow2Term * sizeOf, values, 0, infoRow3 * sizeOf);
		}
		else
		{
			T[] lineContainingArray = lineContainings[0];
			int ordinal2 = intList[0];
			int infoRow2Term2 = infoRow2 - ordinal2;
			int ordinal3 = lineContainingArray.Length - infoRow2Term2;
			int ordinal4 = 0;
			int sizeOfTerm = ordinal3 * sizeOf;
			Buffer.BlockCopy(lineContainingArray, infoRow2Term2 * sizeOf, values, ordinal4, sizeOfTerm);
			ordinal4 += sizeOfTerm;
			for (int j = lineIndex + 1; j <= lineIndex2 - 1; j++)
			{
				T[] lineContaining2 = lineContainings[j - lineIndex];
				sizeOfTerm = lineContaining2.Length * sizeOf;
				Buffer.BlockCopy(lineContaining2, 0, values, ordinal4, sizeOfTerm);
				ordinal4 += sizeOfTerm;
			}
			T[] src2 = lineContainings.Last();
			ordinal2 = intList.Last();
			sizeOfTerm = (infoRow2InfoRow3Term - ordinal2 + 1) * sizeOf;
			Buffer.BlockCopy(src2, 0, values, ordinal4, sizeOfTerm);
		}
		return values;
	}
}
