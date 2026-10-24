using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Utility.Hashing;

namespace Geospatial.GDALAssist.Vectors.SSURGO;

[DebuggerDisplay("{DebugDisplay()}")]
public class TextureGroup
{
	public readonly string Key;

	public readonly string Description;

	public readonly string Texture;

	public readonly string HorizonKey;

	public readonly bool IsRepresentative;

	public readonly List<Texture> Textures;

	public TextureGroup(string tgKey, string tgDesc, string texture)
	{
		Key = tgKey;
		Description = tgDesc;
		Texture = texture;
		Textures = new List<Texture>();
	}

	public TextureGroup(string tgKey, string tgDesc, string texture, string hzKey)
	{
		Key = tgKey;
		Description = tgDesc;
		Texture = texture;
		HorizonKey = hzKey;
		Textures = new List<Texture>();
	}

	public TextureGroup(string tgKey, string tgDesc, string texture, string hzKey, bool rv)
	{
		Key = tgKey;
		Description = tgDesc;
		Texture = texture;
		HorizonKey = hzKey;
		IsRepresentative = rv;
		Textures = new List<Texture>();
	}

	public bool FunctionallyEquals(TextureGroup other)
	{
		if (other == null)
		{
			return false;
		}
		if (Description == other.Description)
		{
			return Texture == other.Texture;
		}
		return false;
	}

	public int GetKeylessHashCode()
	{
		return Hash.Combine(Description, Texture);
	}

	public string BestTextureName()
	{
		if (!string.IsNullOrEmpty(Texture))
		{
			return Texture;
		}
		if (Textures.Count == 0)
		{
			return "";
		}
		Texture texture = Textures.First();
		return texture.TextureName ?? texture.InLieu;
	}

	private string DebugDisplay()
	{
		return Description + " with " + Textures.Count + " Textures";
	}
}
