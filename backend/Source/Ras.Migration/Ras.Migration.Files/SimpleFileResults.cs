using System;
using System.Collections.Generic;

namespace Ras.Migration.Files;

public class SimpleFileResults
{
	protected static string VALUE_DELIMETER = "=";

	private Dictionary<string, object> _cache;

	private ISimpleFileReader _reader;

	private ISimpleFileWriter _writer;

	public SimpleFileResults(ISimpleFileReader reader, ISimpleFileWriter writer)
	{
		_cache = new Dictionary<string, object>();
		_writer = writer;
		_reader = reader;
	}

	public string ReadStringValue(string path, string defaultIfDNE = null, bool allowCache = true)
	{
		if (allowCache && _cache.ContainsKey(path))
		{
			if (_cache[path] != null)
			{
				return _cache[path].ToString();
			}
			return null;
		}
		string cache = _reader.ReadValue(path + VALUE_DELIMETER);
		if (allowCache)
		{
			_cache[path] = cache;
		}
		if (cache != null)
		{
			return cache;
		}
		return defaultIfDNE;
	}

	public T ReadEnumValue<T>(string path, T defaultIfDNE = default(T), bool allowCache = true) where T : Enum
	{
		if (ReadStringValue(path) == null)
		{
			return defaultIfDNE;
		}
		object result = null;
		if (Enum.TryParse(typeof(T), path, out result) && result is T)
		{
			return (T)result;
		}
		return defaultIfDNE;
	}

	public List<string> ReadList(string path, bool allowCache = true)
	{
		if (allowCache && _cache.ContainsKey(path))
		{
			if (_cache[path] is List<string>)
			{
				return _cache[path] as List<string>;
			}
			return null;
		}
		List<string> cache = _reader.ReadList(path + VALUE_DELIMETER);
		if (allowCache)
		{
			_cache[path] = cache;
		}
		return cache;
	}

	public List<double> ReadDoubleList(string path, bool allowCache = true)
	{
		List<double> doubleList = new List<double>();
		List<string> readList = ReadList(path, allowCache);
		if (readList == null)
		{
			return doubleList;
		}
		foreach (string readList2 in readList)
		{
			if (double.TryParse(readList2, out var result))
			{
				doubleList.Add(result);
			}
			else
			{
				doubleList.Add(double.NaN);
			}
		}
		return doubleList;
	}

	public bool ReadBoolValue(string path, bool defaultIfDNE = false, bool allowCache = true)
	{
		string stringValue = ReadStringValue(path, null, allowCache);
		if (stringValue != null && bool.TryParse(stringValue, out var result))
		{
			return result;
		}
		return defaultIfDNE;
	}

	public double ReadDoubleValue(string path, double defaultIfDNE = double.NaN, bool allowCache = true)
	{
		string stringValue = ReadStringValue(path, null, allowCache);
		if (stringValue != null && double.TryParse(stringValue, out var result))
		{
			return result;
		}
		return defaultIfDNE;
	}

	public float ReadFloatValue(string path, float defaultIfDNE = float.NaN, bool allowCache = true)
	{
		string stringValue = ReadStringValue(path, null, allowCache);
		if (stringValue != null && float.TryParse(stringValue, out var result))
		{
			return result;
		}
		return defaultIfDNE;
	}

	public int ReadIntValue(string path, int defaultIfDNE = 0, bool allowCache = true)
	{
		string stringValue = ReadStringValue(path, null, allowCache);
		if (stringValue != null && int.TryParse(stringValue, out var result))
		{
			return result;
		}
		return defaultIfDNE;
	}

	public long ReadLongValue(string path, long defaultIfDNE = 0L, bool allowCache = true)
	{
		string stringValue = ReadStringValue(path, null, allowCache);
		if (stringValue != null && long.TryParse(stringValue, out var result))
		{
			return result;
		}
		return defaultIfDNE;
	}

	protected void WriteValue(string path, bool boolValue, bool allowCache = true)
	{
		WriteValue(path, boolValue.ToString() ?? "", allowCache);
	}

	protected void WriteValue(string path, string cache, bool allowCache = true)
	{
		if (allowCache)
		{
			_cache[path] = cache;
		}
		_writer.WriteValue(path + VALUE_DELIMETER, cache);
	}

	protected void WriteList(string path, List<string> values, bool allowCache = true)
	{
		if (allowCache)
		{
			_cache[path] = values;
		}
		_writer.WriteList(path + VALUE_DELIMETER, values);
	}

	protected void WriteList(string path, List<double> values, bool allowCache = true)
	{
		if (allowCache)
		{
			_cache[path] = values;
		}
		List<string> stringList = new List<string>();
		foreach (double computed in values)
		{
			stringList.Add(computed.ToString());
		}
		_writer.WriteList(path + VALUE_DELIMETER, stringList);
	}

	public bool HasPath(string path, bool requireEntireLineEquality = false)
	{
		return _reader.ContainsPath(path, requireEntireLineEquality);
	}

	protected void CreatePath(string path)
	{
		_writer.CreatePath(path);
	}
}
