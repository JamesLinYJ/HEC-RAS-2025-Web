using System.Threading.Tasks;
using Utility.Web;

namespace Geospatial.IO.Network.OSM;

public static class OsmEndpoints
{
	public const string RateLimitAdvice = "The Overpass instance limits concurrent queries per IP. Wait a moment and try again, or query a smaller area.";

	private static readonly FwLinkEndpoint _overpass = new FwLinkEndpoint("ras-2025-overpass-api", "Overpass API");

	private static readonly FwLinkEndpoint _overpassStatus = new FwLinkEndpoint("ras-2025-overpass-status", "Overpass status endpoint");

	private static readonly FwLinkEndpoint _tagInfo = new FwLinkEndpoint("ras-2025-taginfo-api", "taginfo API");

	public static Task<(bool Success, string Url)> TryGetOverpassUrlAsync()
	{
		return _overpass.TryGetUrlAsync();
	}

	public static Task<(bool Success, string Url)> TryGetOverpassStatusUrlAsync()
	{
		return _overpassStatus.TryGetUrlAsync();
	}

	public static Task<(bool Success, string Url)> TryGetTagInfoBaseUrlAsync()
	{
		return _tagInfo.TryGetUrlAsync();
	}
}
