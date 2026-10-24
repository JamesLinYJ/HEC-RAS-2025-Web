using System.Collections.Generic;
using System.Data;
using Utility.Hashing;

namespace Geospatial.GDALAssist.Vectors.SSURGO;

public class ComponentCollection
{
	private readonly List<Component> _components = new List<Component>();

	private readonly Index<Component, string> _muKeyIndex = new Index<Component, string>((Component tx) => tx.MapUnitKey);

	public int Count => _components.Count;

	public void Add(Component tx)
	{
		_components.Add(tx);
		_muKeyIndex.Add(tx);
	}

	public void Add(DataRow dr)
	{
		string cokey = (string)dr["cokey"];
		string mukey = (string)dr["mukey"];
		string coname = (string)dr["compname"];
		int relPct = (int)dr["comppct_r"];
		Add(new Component(cokey, coname, relPct, mukey));
	}

	public void AddRange(DataTable dt)
	{
		for (int i = 0; i < dt.Rows.Count; i++)
		{
			Add(dt.Rows[i]);
		}
	}

	public List<Component> FindByMapUnitKey(string key)
	{
		return _muKeyIndex.GetItems(key);
	}

	public void Link(HorizonCollection horizons)
	{
		for (int i = 0; i < Count; i++)
		{
			Component component = _components[i];
			component.Horizons.AddRange(horizons.FindByComponentKey(component.Key));
		}
	}
}
