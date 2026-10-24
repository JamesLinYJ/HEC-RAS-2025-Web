using System.Threading;
using System.Threading.Tasks;
using Geospatial.Features;
using Geospatial.GDALAssist;
using Geospatial.IO.Network.ArcGisRest;
using Geospatial.Vectors;
using Utility.Logging;
using Utility.Memory;
using Utility.Progress;
using Utility.Web;

namespace Geospatial.IO.Network.NFHL;

public static class NFHLRequests
{
	public const int FloodHazardZonesLayer = 28;

	public const int NativeSrWkid = 4269;

	private static readonly MessageClass UnexpectedSrWarning = new MessageClass("NFHL_UnexpectedSR", MessageSeverity.Warning, "FEMA NFHL returned spatial reference {Returned}, expected native {Expected}; the server reprojected the geometry.");

	private static readonly FwLinkEndpoint _endpoint = new FwLinkEndpoint("ras-2025-nfhl-rest-api", "FEMA NFHL REST API");

	private static readonly MessageClass UnresolvedEndpoint = new MessageClass("NFHL_UnresolvedEndpoint", MessageSeverity.Error, "Could not resolve the FEMA NFHL address. Check your network connection and try again.");

	public static async Task<(OperationResult, ComplexPolygonFeatureCollection)> QueryFloodHazardZonesCompleteAsync(Extent extent, Projection projection, ProgressReporter reporter = null, CancellationToken ct = default(CancellationToken))
	{
		var (localValue, baseUrl) = await _endpoint.TryGetUrlAsync();
		if (!localValue)
		{
			return (OperationResult.Fail(UnresolvedEndpoint.Create()), null);
		}
		var (result, response) = await ArcGisRestClient.QueryByExtentCompleteAsync(baseUrl, 28, extent, projection, 4269, "FLD_ZONE NOT IN ('D', 'OPEN WATER', 'AREA NOT INCLUDED') AND (FLD_ZONE <> 'X' OR ZONE_SUBTY LIKE '0.2%' OR ZONE_SUBTY LIKE '%LEVEE%')", "*", returnZ: false, 500, reporter, ct);
		return BuildZoneResult(result, response, projection, reporter);
	}

	private static (OperationResult, ComplexPolygonFeatureCollection) BuildZoneResult(OperationResult result, ArcGisQueryResponse response, Projection projection, ProgressReporter reporter)
	{
		if (!result)
		{
			return (result, EmptyZones());
		}
		OperationResult other = ArcGisRestConversions.TryGetResponseProjectionOrDefault(response, out var projection2);
		result.MergeIn(other);
		OperationResult other2 = ArcGisRestConversions.TryConvertToComplexPolygons(response, projection2, projection, out var complexPolygons, out var attributeTable);
		result.MergeIn(other2);
		return (result, new ComplexPolygonFeatureCollection(complexPolygons, attributeTable));
	}

	private static ComplexPolygonFeatureCollection EmptyZones()
	{
		return new ComplexPolygonFeatureCollection(new ComplexPolygonCollection(), new Table());
	}
}
