using System;
using System.Collections.Generic;

namespace Geospatial.Rendering;

public class CommonData
{
	private Dictionary<object, Func<object>> _data;

	public bool HasData
	{
		get
		{
			if (_data != null)
			{
				return _data.Count > 0;
			}
			return false;
		}
	}

	public void RegisterCommonData(object key, object data)
	{
		if (_data == null)
		{
			_data = new Dictionary<object, Func<object>>();
		}
		_data[key] = () => data;
	}

	public void RegisterCommonData(object key, Func<object> data)
	{
		if (_data == null)
		{
			_data = new Dictionary<object, Func<object>>();
		}
		_data[key] = data;
	}

	public bool TryRetrieveCommonData(object key, out object data)
	{
		if (_data == null || key == null)
		{
			data = null;
			return false;
		}
		if (_data.TryGetValue(key, out var localValue))
		{
			data = localValue();
			return data != null;
		}
		data = null;
		return false;
	}

	public void Clear()
	{
		_data = null;
	}
}
