using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Progress;
using Utility.Web;

namespace Utility.Network.Cumulus;

public sealed class CumulusClient
{
	private static readonly ILogger _logger = LogHelper.GetLogger("CumulusClient");

	private static readonly HttpClient _httpClient = SharedWebResourceManager.Client;

	private readonly string _baseUrl;

	private readonly string _appKey;

	public string AuthToken { get; set; }

	public CumulusClient(string baseUrl, string authToken = null, string appKey = null)
	{
		if (string.IsNullOrWhiteSpace(baseUrl))
		{
			throw new ArgumentException("Cumulus base URL is required", "baseUrl");
		}
		_baseUrl = baseUrl.TrimEnd('/');
		AuthToken = authToken;
		_appKey = appKey;
	}

	private HttpRequestMessage BuildGet(string relativeUrl)
	{
		HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, _baseUrl + relativeUrl);
		if (!string.IsNullOrEmpty(AuthToken))
		{
			httpRequestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);
		}
		return httpRequestMessage;
	}

	public async Task<IReadOnlyList<CumulusProduct>> GetProductsAsync(CancellationToken ct = default(CancellationToken), ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		reporter.ReportMessage("Querying Cumulus products...");
		using HttpRequestMessage req = BuildGet("/api/products");
		using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
		resp.EnsureSuccessStatusCode();
		List<CumulusProduct> fromJsonAsync = await resp.Content.ReadFromJsonAsync<List<CumulusProduct>>(ct);
		reporter.ReportProgress(100);
		if (fromJsonAsync == null)
		{
			fromJsonAsync = new List<CumulusProduct>();
		}
		return fromJsonAsync;
	}

	public async Task<IReadOnlyList<CumulusCogFile>> GetCogFilesAsync(string productId, DateTime afterUtc, DateTime beforeUtc, CancellationToken ct = default(CancellationToken), ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		reporter.ReportMessage("Querying Cumulus grids...");
		string escapeDataString = Uri.EscapeDataString(afterUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
		string escapeDataString2 = Uri.EscapeDataString(beforeUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
		using HttpRequestMessage req = BuildGet($"/api/products/{productId}/cog-files?after={escapeDataString}&before={escapeDataString2}");
		using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
		resp.EnsureSuccessStatusCode();
		List<CumulusCogFile> fromJsonAsync = await resp.Content.ReadFromJsonAsync<List<CumulusCogFile>>(ct);
		reporter.ReportProgress(100);
		if (fromJsonAsync == null)
		{
			fromJsonAsync = new List<CumulusCogFile>();
		}
		return fromJsonAsync;
	}

	public string ResolveCogUrl(string cogUrl)
	{
		if (string.IsNullOrEmpty(cogUrl))
		{
			return cogUrl;
		}
		if (cogUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || cogUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			return cogUrl;
		}
		return _baseUrl + cogUrl;
	}

	public string BuildCogVsiPath(string cogUrl)
	{
		string resolveCogUrl = ResolveCogUrl(cogUrl);
		if (!string.IsNullOrEmpty(_appKey))
		{
			string resolveCogUrlTerm = (resolveCogUrl.Contains('?') ? "&" : "?");
			resolveCogUrl = resolveCogUrl + resolveCogUrlTerm + "key=" + Uri.EscapeDataString(_appKey);
		}
		return "/vsicurl/" + resolveCogUrl;
	}
}
