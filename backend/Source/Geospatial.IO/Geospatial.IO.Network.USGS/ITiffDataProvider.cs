using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Geospatial.Vectors;
using Utility.Progress;

namespace Geospatial.IO.Network.USGS;

public interface ITiffDataProvider : IDisposable
{
	string ProviderId { get; }

	string DisplayName { get; }

	string Description { get; }

	bool IsEnabled { get; set; }

	USGSDataType SupportedDataTypes { get; }

	IReadOnlyList<DataResolution> AvailableResolutions { get; }

	Task<bool> CheckAvailabilityAsync();

	Task<List<USGSItem>> QueryTilesAsync(Extent queryExtentWGS84, ProgressReporter reporter = null);
}
