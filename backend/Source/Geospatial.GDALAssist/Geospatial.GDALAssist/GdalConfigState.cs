using System;
using System.Collections.Generic;
using OSGeo.GDAL;

namespace Geospatial.GDALAssist;

public sealed class GdalConfigState
{
	private readonly object _lock = new object();

	private readonly Dictionary<string, string> _originalValues = new Dictionary<string, string>(StringComparer.Ordinal);

	public void SetConfigOption(string key, string configOption)
	{
		if (string.IsNullOrEmpty(key))
		{
			throw new ArgumentException("Config option key must be non-empty.", "key");
		}
		lock (_lock)
		{
			if (!_originalValues.ContainsKey(key))
			{
				_originalValues[key] = Gdal.GetConfigOption(key, null);
			}
			Gdal.SetConfigOption(key, configOption);
		}
	}

	public void Restore()
	{
		lock (_lock)
		{
			foreach (KeyValuePair<string, string> originalValue in _originalValues)
			{
				Gdal.SetConfigOption(originalValue.Key, originalValue.Value);
			}
			_originalValues.Clear();
		}
	}
}
