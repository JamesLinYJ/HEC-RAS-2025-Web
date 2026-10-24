using System.Threading;
using System.Threading.Tasks;
using Geospatial.GDALAssist;
using Geospatial.IO.Network.ArcGisRest;
using Geospatial.Vectors;
using Utility.Logging;
using Utility.Progress;
using Utility.Web;

namespace Geospatial.IO.Network.NLD;

public static class NLDRequests
{
	private static readonly FwLinkEndpoint _endpoint = new FwLinkEndpoint("ras-2025-nld-rest-api", "NLD REST API");

	private static readonly MessageClass UnresolvedEndpoint = new MessageClass("NLD_UnresolvedEndpoint", MessageSeverity.Error, "Could not resolve the National Levee Database address. Check your network connection and try again.");

	public static async Task<(OperationResult, ArcGisQueryResponse)> QueryByExtentAsync(NLDLayer layer, Extent extent, Projection projection, string outFields = "*", ProgressReporter reporter = null, CancellationToken ct = default(CancellationToken))
	{
		var (localValue, baseUrl) = await _endpoint.TryGetUrlAsync();
		if (!localValue)
		{
			return (OperationResult.Fail(UnresolvedEndpoint.Create()), null);
		}
		return await ArcGisRestClient.QueryByExtentCompleteAsync(baseUrl, (int)layer, extent, projection, 4326, "1=1", outFields, returnZ: true, 500, reporter, ct);
	}

	public static Task<(OperationResult, ArcGisQueryResponse)> QueryEmbankmentsAsync(Extent extent, Projection projection, string outFields = "*", ProgressReporter reporter = null, CancellationToken ct = default(CancellationToken))
	{
		return QueryByExtentAsync(NLDLayer.Embankments, extent, projection, outFields, reporter, ct);
	}

	public static Task<(OperationResult, ArcGisQueryResponse)> QueryFloodwallsAsync(Extent extent, Projection projection, string outFields = "*", ProgressReporter reporter = null, CancellationToken ct = default(CancellationToken))
	{
		return QueryByExtentAsync(NLDLayer.Floodwalls, extent, projection, outFields, reporter, ct);
	}

	public static Task<(OperationResult, ArcGisQueryResponse)> QueryAlignmentLinesAsync(Extent extent, Projection projection, string outFields = "*", ProgressReporter reporter = null, CancellationToken ct = default(CancellationToken))
	{
		return QueryByExtentAsync(NLDLayer.AlignmentLines, extent, projection, outFields, reporter, ct);
	}
}
