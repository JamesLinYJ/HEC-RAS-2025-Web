using System.Collections.Generic;
using System.Data;
using Utility.Extensions;
using Utility.Hashing;

namespace Geospatial.GDALAssist.Vectors.SSURGO;

public class TextureGroupCollection
{
	public readonly List<TextureGroup> Groups;

	private readonly Index<TextureGroup, string> _hzKeyIndex;

	public int Count => Groups.Count;

	public TextureGroupCollection()
	{
		Groups = new List<TextureGroup>();
		_hzKeyIndex = new Index<TextureGroup, string>((TextureGroup tx) => tx.HorizonKey);
	}

	public void Add(TextureGroup tx)
	{
		_hzKeyIndex.Add(tx);
	}

	public void Add(DataRow dr)
	{
		string tgKey = (string)dr["chtgkey"];
		string tgDesc = (string)dr["texdesc"];
		string texture = (string)dr["texture"];
		string hzKey = (string)dr["chkey"];
		bool rv = (((string)dr["rvindicator"]).RoughlyEquals("yes") ? true : false);
		Add(new TextureGroup(tgKey, tgDesc, texture, hzKey, rv));
	}

	public void AddRange(DataTable dt)
	{
		for (int i = 0; i < dt.Rows.Count; i++)
		{
			Add(dt.Rows[i]);
		}
	}

	public List<TextureGroup> FindByHorizonKey(string key)
	{
		return _hzKeyIndex.GetItems(key);
	}
}
