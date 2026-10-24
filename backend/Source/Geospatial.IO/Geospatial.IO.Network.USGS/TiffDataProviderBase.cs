using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Geospatial.Vectors;
using Utility.Progress;

namespace Geospatial.IO.Network.USGS;

public abstract class TiffDataProviderBase : ITiffDataProvider, IDisposable
{
	private DateTime _lastAvailabilityCheck = DateTime.MinValue;

	private bool _cachedAvailability = true;

	private static readonly TimeSpan AvailabilityCacheDuration = TimeSpan.FromMinutes(5L);

	protected bool _disposed;

	public abstract string ProviderId { get; }

	public abstract string DisplayName { get; }

	public abstract string Description { get; }

	public bool IsEnabled { get; set; } = true;

	public abstract USGSDataType SupportedDataTypes { get; }

	public abstract IReadOnlyList<DataResolution> AvailableResolutions { get; }

	public async Task<bool> CheckAvailabilityAsync()
	{
		if (DateTime.Now - _lastAvailabilityCheck < AvailabilityCacheDuration)
		{
			return _cachedAvailability;
		}
		_cachedAvailability = await PerformAvailabilityCheckAsync();
		_lastAvailabilityCheck = DateTime.Now;
		return _cachedAvailability;
	}

	protected abstract Task<bool> PerformAvailabilityCheckAsync();

	public abstract Task<List<USGSItem>> QueryTilesAsync(Extent queryExtentWGS84, ProgressReporter reporter = null);

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		_disposed = true;
	}
}
