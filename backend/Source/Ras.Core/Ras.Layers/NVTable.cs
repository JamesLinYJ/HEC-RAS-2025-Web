using System.Collections.Generic;
using Geospatial;
using Utility.Hashing;

namespace Ras.Layers;

public class NVTable : IDataHash, IMutableSpatialVersion, ISpatialVersion
{
	private Dictionary<string, float> _data;

	private long _versionXy;

	private Freezer _freezer;

	public int Count => _data.Count;

	public Dictionary<string, float> RawData => _data;

	public long VersionXY
	{
		get
		{
			if (!_freezer.IsFrozen)
			{
				return _versionXy;
			}
			return _freezer.FrozenVersionXY;
		}
	}

	public void FreezeOne()
	{
		_freezer.FreezeOne(this);
	}

	public void ThawOne()
	{
		_freezer.ThawOne();
	}

	public void ForceSetVersion(long versionXy)
	{
		_versionXy = versionXy;
	}

	public NVTable()
	{
		_data = new Dictionary<string, float>();
	}

	public bool Remove(string key)
	{
		_versionXy++;
		return _data.Remove(key);
	}

	public void Set(string key, float floatValue)
	{
		_versionXy++;
		_data[key] = floatValue;
	}

	public void UpdateName(string oldKey, string newKey)
	{
		float oldKeyTerm = _data[oldKey];
		_data.Remove(oldKey);
		_data[newKey] = oldKeyTerm;
		_versionXy++;
	}

	public float Get(string key)
	{
		return _data[key];
	}

	public int GetDataHash()
	{
		int combine = 0;
		foreach (KeyValuePair<string, float> datum in _data)
		{
			combine = StableHash.Combine(combine, datum.Key);
			combine = StableHash.Combine(combine, datum.Value);
		}
		return combine;
	}

	public void Clear()
	{
		_data.Clear();
		_versionXy++;
	}
}
