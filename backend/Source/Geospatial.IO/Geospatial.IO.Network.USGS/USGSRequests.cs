using System;
using System.Threading.Tasks;
using Geospatial.GDALAssist;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Progress;

namespace Geospatial.IO.Network.USGS;

public static class USGSRequests
{
	private static readonly ILogger _logger = LogHelper.GetLogger("USGSRequests");

	private static bool _providersInitialized;

	private static readonly object _initLock = new object();

	public static TiffDataProviderRegistry ProviderRegistry => TiffDataProviderRegistry.Instance;

	public static void EnsureProvidersInitialized(bool force = false)
	{
		if (_providersInitialized && !force)
		{
			return;
		}
		lock (_initLock)
		{
			if (!_providersInitialized || force)
			{
				ProviderRegistry.InitializeDefaultProviders();
				_providersInitialized = true;
			}
		}
	}

	public static async Task<USGSProductQuery> GetProductsFromExtent(Extent extent, Projection projection, USGSDataType dataType, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (projection == null)
		{
			throw new InvalidOperationException("No projection is set for this project. Set a projection before querying USGS terrain.");
		}
		EnsureProvidersInitialized();
		Extent boundingBox = Projector.GetBoundingBox(extent, projection, WellKnownProjections.WGS84);
		return await ProviderRegistry.QueryAllProvidersAsync(boundingBox, dataType, reporter);
	}
}
