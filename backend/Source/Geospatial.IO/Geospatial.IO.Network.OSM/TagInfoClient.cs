using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Web;

namespace Geospatial.IO.Network.OSM;

public static class TagInfoClient
{
	private class TagInfoResponse
	{
		[JsonPropertyName("total")]
		public int Total { get; set; }

		[JsonPropertyName("data")]
		public List<TagInfoItem> Data { get; set; }
	}

	private class TagInfoItem
	{
		[JsonPropertyName("key")]
		public string Key { get; set; }

		[JsonPropertyName("value")]
		public string Value { get; set; }
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("TagInfoClient");

	private const string KeysPathFormat = "/api/4/keys/all?query={0}&sortname=count_all&sortorder=desc&rp=25&page=1";

	private const string ValuesPathFormat = "/api/4/key/values?key={0}&query={1}&sortname=count&sortorder=desc&rp=25&page=1";

	private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5L);

	private static readonly ConcurrentDictionary<string, IReadOnlyList<string>> _keyCache = new ConcurrentDictionary<string, IReadOnlyList<string>>();

	private static readonly ConcurrentDictionary<(string Key, string Prefix), IReadOnlyList<string>> _valueCache = new ConcurrentDictionary<(string, string), IReadOnlyList<string>>();

	private static HttpClient Client => SharedWebResourceManager.Client;

	public static async Task<IReadOnlyList<string>> SuggestKeysAsync(string prefix, CancellationToken ct = default(CancellationToken))
	{
		string trimmedPrefix = ((prefix == null) ? string.Empty : prefix.Trim());
		if (_keyCache.TryGetValue(trimmedPrefix, out var localValue))
		{
			return localValue;
		}
		var (localValue2, localValue3) = await OsmEndpoints.TryGetTagInfoBaseUrlAsync();
		if (!localValue2)
		{
			return Array.Empty<string>();
		}
		IReadOnlyList<string> readOnlyList = await FetchNamesAsync(localValue3 + $"/api/4/keys/all?query={Uri.EscapeDataString(trimmedPrefix)}&sortname=count_all&sortorder=desc&rp=25&page=1", (TagInfoItem tagInfoItem) => tagInfoItem.Key ?? tagInfoItem.Value, ct);
		_keyCache[trimmedPrefix] = readOnlyList;
		return readOnlyList;
	}

	public static async Task<IReadOnlyList<string>> SuggestValuesAsync(string key, string prefix, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(key))
		{
			return Array.Empty<string>();
		}
		string trimmedKey = key.Trim();
		string trimmedPrefix = ((prefix == null) ? string.Empty : prefix.Trim());
		(string, string) cacheKey = (trimmedKey, trimmedPrefix);
		if (_valueCache.TryGetValue(cacheKey, out var localValue))
		{
			return localValue;
		}
		var (localValue2, localValue3) = await OsmEndpoints.TryGetTagInfoBaseUrlAsync();
		if (!localValue2)
		{
			return Array.Empty<string>();
		}
		IReadOnlyList<string> readOnlyList = await FetchNamesAsync(localValue3 + $"/api/4/key/values?key={Uri.EscapeDataString(trimmedKey)}&query={Uri.EscapeDataString(trimmedPrefix)}&sortname=count&sortorder=desc&rp=25&page=1", (TagInfoItem tagInfoItem) => tagInfoItem.Value ?? tagInfoItem.Key, ct);
		_valueCache[cacheKey] = readOnlyList;
		return readOnlyList;
	}

	private static async Task<IReadOnlyList<string>> FetchNamesAsync(string url, Func<TagInfoItem, string> selectName, CancellationToken ct)
	{
		using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		timeoutCts.CancelAfter(RequestTimeout);
		try
		{
			using HttpResponseMessage response = await Client.GetAsync(url, timeoutCts.Token);
			if (!response.IsSuccessStatusCode)
			{
				return Array.Empty<string>();
			}
			TagInfoResponse tagInfoResponse = JsonSerializer.Deserialize<TagInfoResponse>(await response.Content.ReadAsStringAsync(timeoutCts.Token));
			if (tagInfoResponse?.Data == null)
			{
				return Array.Empty<string>();
			}
			List<string> stringList = new List<string>(tagInfoResponse.Data.Count);
			foreach (TagInfoItem datum in tagInfoResponse.Data)
			{
				string selectName2 = selectName(datum);
				if (!string.IsNullOrEmpty(selectName2))
				{
					stringList.Add(selectName2);
				}
			}
			return stringList;
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Taginfo suggest request failed for {Url}", url);
			return Array.Empty<string>();
		}
	}
}
