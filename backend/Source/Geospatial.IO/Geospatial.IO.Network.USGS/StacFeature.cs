using System.Collections.Generic;
using System.Text.Json.Serialization;
using Geospatial.Vectors;

namespace Geospatial.IO.Network.USGS;

internal class StacFeature
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("bbox")]
	public double[] Bbox { get; set; }

	[JsonPropertyName("properties")]
	public StacProperties Properties { get; set; }

	[JsonPropertyName("assets")]
	public Dictionary<string, StacAsset> Assets { get; set; }

	public Extent? GetBboxAsExtent()
	{
		if (Bbox == null || Bbox.Length < 4)
		{
			return null;
		}
		return new Extent(Bbox[0], Bbox[2], Bbox[1], Bbox[3]);
	}

	public string GetDownloadUrl()
	{
		if (Assets == null)
		{
			return null;
		}
		if (Assets.TryGetValue("data", out var localValue) && !string.IsNullOrEmpty(localValue.Href))
		{
			return localValue.Href;
		}
		foreach (StacAsset stacAsset in Assets.Values)
		{
			if (!string.IsNullOrEmpty(stacAsset.Href))
			{
				return stacAsset.Href;
			}
		}
		return null;
	}
}
