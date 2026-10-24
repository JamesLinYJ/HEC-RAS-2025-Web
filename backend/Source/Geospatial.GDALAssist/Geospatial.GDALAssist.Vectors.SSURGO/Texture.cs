using System.Diagnostics;
using Utility.Hashing;

namespace Geospatial.GDALAssist.Vectors.SSURGO;

[DebuggerDisplay("{TextureName} ({InLieu})")]
public class Texture
{
	public readonly string Key;

	public readonly string TextureName;

	public readonly string InLieu;

	public Texture(string txKey, string txName, string inLieu)
	{
		Key = txKey;
		TextureName = txName;
		InLieu = inLieu;
	}

	public bool FunctionallyEquals(Texture other)
	{
		if (other == null)
		{
			return false;
		}
		if (TextureName == other.TextureName)
		{
			return InLieu == other.InLieu;
		}
		return false;
	}

	public int GetKeylessHashCode()
	{
		return Hash.Combine(TextureName, InLieu);
	}
}
