using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace H5Assist.Caching.VB;

public class H5IndexedCache2D<T> : IH5Cache where T : struct
{
	protected string _filename;

	protected DateTime _filedate;

	private H5RowCache<int> _info;

	private H5RowCache<T> _values;

	private Dictionary<string, string> _infoValueMap;

	public string Filename => _filename;

	public List<string> ValuesDatasets => _infoValueMap.Values.ToList();

	public List<string> InfoDatasets => _infoValueMap.Keys.ToList();

	public H5IndexedCache2D(string filename)
	{
		_filename = filename;
		_info = new H5RowCache<int>(filename);
		_values = new H5RowCache<T>(filename);
		_infoValueMap = new Dictionary<string, string>();
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
		_infoValueMap.Add(infoDataset, valueDataset);
	}

	public int GetInfoIndex(string dataset)
	{
		return _info.GetIndex(dataset);
	}

	public int GetValuesIndex(string dataset)
	{
		return _values.GetIndex(dataset);
	}

	public T[,] Item(int infoDatasetIndex, int valuesDatasetIndex, int itemIndex, ref int[] infoRow)
	{
		throw new NotImplementedException();
	}
}
