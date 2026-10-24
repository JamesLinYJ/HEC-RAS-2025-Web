using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Utility.Hashing;

namespace Geospatial.GDALAssist.Vectors.SSURGO;

[DebuggerDisplay("{DebugDisplay()}")]
public class Horizon
{
	public readonly string Key;

	public readonly string Name;

	public readonly int DepthTop;

	public readonly int DepthBottom;

	public readonly string ComponentKey;

	public readonly List<TextureGroup> TextureGroups;

	public Horizon(string hzKey, string hzName, int depthTop, int depthBot)
	{
		Key = hzKey;
		Name = hzName;
		DepthTop = depthTop;
		DepthBottom = depthBot;
		TextureGroups = new List<TextureGroup>();
	}

	public Horizon(string hzKey, string hzName, int depthTop, int depthBot, string coKey)
	{
		Key = hzKey;
		Name = hzName;
		DepthTop = depthTop;
		DepthBottom = depthBot;
		ComponentKey = coKey;
		TextureGroups = new List<TextureGroup>();
	}

	public TextureGroup DominantTextureGroup()
	{
		for (int i = 0; i < TextureGroups.Count; i++)
		{
			TextureGroup textureGroup = TextureGroups[i];
			if (textureGroup.IsRepresentative)
			{
				return textureGroup;
			}
		}
		return TextureGroups.FirstOrDefault();
	}

	public bool FunctionallyEquals(Horizon other)
	{
		if (other == null)
		{
			return false;
		}
		if (Name == other.Name && DepthTop == other.DepthTop)
		{
			return DepthBottom == other.DepthBottom;
		}
		return false;
	}

	public int GetKeylessHashCode()
	{
		return Hash.Combine(Name, DepthTop, DepthBottom, Hash.Combine(TextureGroups.Select((TextureGroup tg) => tg.GetKeylessHashCode())));
	}

	private string DebugDisplay()
	{
		return Name + " with " + TextureGroups.Count + " Texture Groups";
	}
}
