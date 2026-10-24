using System.Collections.Generic;
using System.Data;
using Utility.Hashing;

namespace Geospatial.GDALAssist.Vectors.SSURGO;

public class HorizonCollection
{
	public readonly List<Horizon> Horizons = new List<Horizon>();

	public TextureGroupCollection TextureGroupCollection;

	private readonly Index<Horizon, string> _coKeyIndex;

	private readonly UniqueIndex<Horizon, string> _hzKeyIndex;

	public int Count => Horizons.Count;

	public HorizonCollection()
	{
		HashSet<Horizon> itemCollection = new HashSet<Horizon>();
		_coKeyIndex = new Index<Horizon, string>((Horizon hz) => hz.ComponentKey, itemCollection);
		_hzKeyIndex = new UniqueIndex<Horizon, string>((Horizon hz) => hz.Key, itemCollection);
	}

	public void Add(Horizon tx)
	{
		Horizons.Add(tx);
		_hzKeyIndex.Add(tx);
		_coKeyIndex.Add(tx);
	}

	public void Add(DataRow dr)
	{
		string hzKey = (string)dr["chkey"];
		string coKey = (string)dr["cokey"];
		string hzName = (string)dr["hzname"];
		int depthTop = (int)dr["hzdept_r"];
		int depthBot = (int)dr["hzdepb_r"];
		Add(new Horizon(hzKey, hzName, depthTop, depthBot, coKey));
	}

	public void AddRange(DataTable dt)
	{
		for (int i = 0; i < dt.Rows.Count; i++)
		{
			Add(dt.Rows[i]);
		}
	}

	public List<Horizon> FindByComponentKey(string key)
	{
		return _coKeyIndex.GetItems(key);
	}

	public Horizon FindByKey(string key)
	{
		return _hzKeyIndex.GetItemOrDefault(key);
	}

	public void Link(TextureGroupCollection txGroups)
	{
		for (int i = 0; i < Count; i++)
		{
			Horizon horizon = Horizons[i];
			horizon.TextureGroups.AddRange(txGroups.FindByHorizonKey(horizon.Key));
		}
	}
}
