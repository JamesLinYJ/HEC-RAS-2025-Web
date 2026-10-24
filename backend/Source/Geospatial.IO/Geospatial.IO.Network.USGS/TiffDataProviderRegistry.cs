using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Progress;

namespace Geospatial.IO.Network.USGS;

public sealed class TiffDataProviderRegistry : IDisposable
{
	private static readonly ILogger _logger = LogHelper.GetLogger("TiffDataProviderRegistry");

	private readonly ConcurrentDictionary<string, ITiffDataProvider> _providers = new ConcurrentDictionary<string, ITiffDataProvider>();

	private bool _disposed;

	public static TiffDataProviderRegistry Instance { get; } = new TiffDataProviderRegistry();

	public IReadOnlyCollection<ITiffDataProvider> Providers => _providers.Values.ToList();

	public IEnumerable<ITiffDataProvider> EnabledProviders => _providers.Values.Where((ITiffDataProvider p) => p.IsEnabled);

	public void Register(ITiffDataProvider provider)
	{
		if (provider == null)
		{
			throw new ArgumentNullException("provider");
		}
		if (_providers.TryAdd(provider.ProviderId, provider))
		{
			_logger.LogInformation("Registered TIFF data provider: {ProviderId} ({DisplayName})", provider.ProviderId, provider.DisplayName);
		}
		else
		{
			_logger.LogWarning("Provider {ProviderId} is already registered", provider.ProviderId);
		}
	}

	public bool Unregister(string providerId)
	{
		if (_providers.TryRemove(providerId, out var localValue))
		{
			localValue.Dispose();
			_logger.LogInformation("Unregistered TIFF data provider: {ProviderId}", providerId);
			return true;
		}
		return false;
	}

	public ITiffDataProvider GetProvider(string providerId)
	{
		_providers.TryGetValue(providerId, out var provider);
		return provider;
	}

	public T GetProvider<T>(string providerId) where T : class, ITiffDataProvider
	{
		return GetProvider(providerId) as T;
	}

	public bool SetProviderEnabled(string providerId, bool enabled)
	{
		if (_providers.TryGetValue(providerId, out var localValue))
		{
			localValue.IsEnabled = enabled;
			_logger.LogInformation("Provider {ProviderId} is now {State}", providerId, enabled ? "enabled" : "disabled");
			return true;
		}
		return false;
	}

	public async Task<USGSProductQuery> QueryAllProvidersAsync(Extent queryExtentWGS84, USGSDataType dataType, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		List<ITiffDataProvider> enabledProviders = EnabledProviders.Where((ITiffDataProvider p) => p.SupportedDataTypes.HasFlag(dataType)).ToList();
		if (enabledProviders.Count == 0)
		{
			_logger.LogWarning("No enabled providers available for data type: {DataType}", dataType);
			OperationResult operationResult = new OperationResult();
			operationResult.AddFailure($"No enabled download provider supports {dataType}.");
			return new USGSProductQuery
			{
				Total = 0,
				Items = new List<USGSItem>(),
				OperationResult = operationResult
			};
		}
		ConcurrentBag<USGSItem> allItems = new ConcurrentBag<USGSItem>();
		OperationResult or = new OperationResult();
		int completedProviders = 0;
		await Parallel.ForEachAsync(enabledProviders, new ParallelOptions
		{
			MaxDegreeOfParallelism = 4
		}, async (ITiffDataProvider provider, CancellationToken ct) =>
		{
			try
			{
				_logger.LogInformation("Querying provider: {ProviderId}", provider.ProviderId);
				List<USGSItem> queryTilesAsync = await provider.QueryTilesAsync(queryExtentWGS84, reporter);
				foreach (USGSItem queryTilesAsync2 in queryTilesAsync)
				{
					allItems.Add(queryTilesAsync2);
				}
				_logger.LogInformation("Provider {ProviderId} returned {Count} items", provider.ProviderId, queryTilesAsync.Count);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Provider {ProviderId} query failed", provider.ProviderId);
				lock (or)
				{
					or.AddFailure(provider.ProviderId + ": " + ex.Message);
				}
			}
			finally
			{
				int increment = Interlocked.Increment(ref completedProviders);
				reporter.ReportProgressFraction((float)increment / (float)enabledProviders.Count);
			}
		});
		List<USGSItem> allItemsList = allItems.ToList();
		_logger.LogInformation("Total items from all providers: {Count}", allItemsList.Count);
		return new USGSProductQuery
		{
			Total = allItemsList.Count,
			Items = allItemsList,
			OperationResult = or
		};
	}

	public void InitializeDefaultProviders()
	{
		foreach (ITiffDataProvider iTiffDataProvider in _providers.Values)
		{
			iTiffDataProvider.Dispose();
		}
		_providers.Clear();
		Register(new TNMApiProvider
		{
			IsEnabled = true
		});
		Register(new SeamlessDEMProvider
		{
			IsEnabled = false
		});
		Register(new WESMRestProvider
		{
			IsEnabled = false
		});
		Register(new NOAATopobathyProvider
		{
			IsEnabled = false
		});
		_logger.LogInformation("Initialized default TIFF data providers");
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		foreach (ITiffDataProvider iTiffDataProvider in _providers.Values)
		{
			iTiffDataProvider.Dispose();
		}
		_providers.Clear();
		_disposed = true;
	}
}
