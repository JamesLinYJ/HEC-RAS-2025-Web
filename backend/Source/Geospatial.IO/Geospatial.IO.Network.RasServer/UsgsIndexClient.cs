using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Geospatial.IO.Network.RasServer;

public class UsgsIndexClient : IDisposable
{
	private readonly HttpClient _client;

	private readonly bool _ownsClient;

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true
	};

	public UsgsIndexClient(string baseUrl)
	{
		_client = new HttpClient
		{
			BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/")
		};
		_ownsClient = true;
	}

	public UsgsIndexClient(HttpClient client)
	{
		_client = client ?? throw new ArgumentNullException("client");
		_ownsClient = false;
	}

	public async Task<QueryResponse> QueryByExtentAsync(double left, double bottom, double right, double top, int limit = 100, CancellationToken ct = default(CancellationToken))
	{
		string requestUri = $"api/usgs/query?left={left}&bottom={bottom}&right={right}&top={top}&limit={limit}";
		HttpResponseMessage httpResponseMessage = await _client.GetAsync(requestUri, ct);
		httpResponseMessage.EnsureSuccessStatusCode();
		return JsonSerializer.Deserialize<QueryResponse>(await httpResponseMessage.Content.ReadAsStringAsync(ct), JsonOptions) ?? throw new InvalidOperationException("Failed to deserialize query response");
	}

	public async Task<TiffFileInfo> GetByIdAsync(int fileId, CancellationToken ct = default(CancellationToken))
	{
		HttpResponseMessage httpResponseMessage = await _client.GetAsync($"api/usgs/{fileId}", ct);
		if (httpResponseMessage.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}
		httpResponseMessage.EnsureSuccessStatusCode();
		return JsonSerializer.Deserialize<TiffFileInfo>(await httpResponseMessage.Content.ReadAsStringAsync(ct), JsonOptions);
	}

	public async Task<string> GetDownloadUrlAsync(int fileId, CancellationToken ct = default(CancellationToken))
	{
		HttpResponseMessage httpResponseMessage = await _client.GetAsync($"api/usgs/{fileId}/download", ct);
		if (httpResponseMessage.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}
		httpResponseMessage.EnsureSuccessStatusCode();
		return await httpResponseMessage.Content.ReadAsStringAsync(ct);
	}

	public string GetRedirectUrl(int fileId)
	{
		return $"{_client.BaseAddress}api/usgs/{fileId}/redirect";
	}

	public async Task<IndexStats> GetStatsAsync(CancellationToken ct = default(CancellationToken))
	{
		HttpResponseMessage httpResponseMessage = await _client.GetAsync("api/usgs/stats", ct);
		httpResponseMessage.EnsureSuccessStatusCode();
		return JsonSerializer.Deserialize<IndexStats>(await httpResponseMessage.Content.ReadAsStringAsync(ct), JsonOptions) ?? throw new InvalidOperationException("Failed to deserialize stats response");
	}

	public void Dispose()
	{
		if (_ownsClient)
		{
			_client.Dispose();
		}
	}
}
