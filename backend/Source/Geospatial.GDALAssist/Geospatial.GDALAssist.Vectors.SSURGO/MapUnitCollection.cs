using System.Collections.Generic;
using System.Data;
using Utility.Hashing;

namespace Geospatial.GDALAssist.Vectors.SSURGO;

public class MapUnitCollection
{
	private readonly List<MapUnit> _mapUnits = new List<MapUnit>();

	private readonly Index<MapUnit, string> _keyIndex = new Index<MapUnit, string>((MapUnit tx) => tx.Key);

	public List<MapUnit> MapUnits => _mapUnits;

	public int Count => _mapUnits.Count;

	public void Add(MapUnit tx)
	{
		_mapUnits.Add(tx);
		_keyIndex.Add(tx);
	}

	public void Add(DataRow dr)
	{
		string mukey = (string)dr["mapunit.mukey"];
		string musym = (string)dr["mapunit.musym"];
		string hydrSoilGrp = (string)dr["muaggatt.hydgrpdcd"];
		Add(new MapUnit(mukey, musym, hydrSoilGrp));
	}

	public void AddRange(DataTable dt)
	{
		for (int i = 0; i < dt.Rows.Count; i++)
		{
			Add(dt.Rows[i]);
		}
	}

	public List<MapUnit> FindByKey(string key)
	{
		return _keyIndex.GetItems(key);
	}

	public void Link(ComponentCollection components)
	{
		for (int i = 0; i < Count; i++)
		{
			MapUnit mapUnit = _mapUnits[i];
			mapUnit.Components.AddRange(components.FindByMapUnitKey(mapUnit.Key));
		}
	}
}
