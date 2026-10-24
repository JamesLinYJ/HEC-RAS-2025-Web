using System.Collections.Generic;
using Utility.Hashing;

namespace Geospatial.GDALAssist.Vectors.SSURGO;

public class TextureCollection
{
	private List<Texture> _textures;

	private Index<Texture, string> _keyIndex = new Index<Texture, string>((Texture tx) => tx.Key);

	private Index<Texture, string> _nameIndex = new Index<Texture, string>((Texture tx) => tx.TextureName);

	public TextureCollection()
	{
		_textures = new List<Texture>();
	}

	public void Add(Texture tx)
	{
		_textures.Add(tx);
		_keyIndex.Add(tx);
		_nameIndex.Add(tx);
	}

	public List<Texture> FindByKey(string key)
	{
		return _keyIndex.GetItems(key);
	}

	public List<Texture> FindByName(string name)
	{
		return _nameIndex.GetItems(name);
	}
}
