using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Progress;

namespace Ras.ExperimentalComputeBackend.GpuHub;

public class GpuHubClient
{
	private static readonly ILogger _logger = LogHelper.GetLogger("GpuHubClient");

	private static readonly HttpClient _httpClient = new HttpClient();

	private static readonly HttpClient _transferClient = new HttpClient
	{
		Timeout = Timeout.InfiniteTimeSpan
	};

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	public const string ProjectSlug = "ras-2025-remote-compute";

	public const string UploadBucket = "gpu-hub-temporary-files";

	private readonly IGpuHubCredentialProvider _creds;

	private ProjectResponse _cachedProject;

	public GpuHubClient(IGpuHubCredentialProvider creds)
	{
		_creds = creds;
	}

	private async Task<HttpRequestMessage> BuildRequest(HttpMethod method, string path, CancellationToken ct)
	{
		HttpRequestMessage req = new HttpRequestMessage(method, await _creds.GetBaseUrlAsync(ct) + "/api" + path);
		HttpRequestHeaders headers = req.Headers;
		headers.Authorization = new AuthenticationHeaderValue("Bearer", await _creds.GetBearerTokenAsync(ct));
		return req;
	}

	public async Task<CreateJobResponse> CreateJobAsync(CreateJobRequest data, CancellationToken ct = default(CancellationToken))
	{
		using HttpRequestMessage req = await BuildRequest(HttpMethod.Post, "/jobs", ct);
		req.Content = JsonContent.Create(data, null, JsonOptions);
		using HttpResponseMessage res = await _httpClient.SendAsync(req, ct);
		res.EnsureSuccessStatusCode();
		return await res.Content.ReadFromJsonAsync<CreateJobResponse>(JsonOptions, ct);
	}

	public async Task<JobStatusResponse> GetJobStatusAsync(int jobId, CancellationToken ct = default(CancellationToken))
	{
		using HttpRequestMessage req = await BuildRequest(HttpMethod.Get, $"/jobs/{jobId}", ct);
		using HttpResponseMessage res = await _httpClient.SendAsync(req, ct);
		res.EnsureSuccessStatusCode();
		return await res.Content.ReadFromJsonAsync<JobStatusResponse>(JsonOptions, ct);
	}

	public async Task<IReadOnlyList<JobStatusResponse>> GetJobsBatchAsync(IReadOnlyCollection<int> jobIds, CancellationToken ct = default(CancellationToken))
	{
		List<JobStatusResponse> results = new List<JobStatusResponse>();
		foreach (int[] chunk in jobIds.Chunk(100))
		{
			string chunkTerm = string.Join(",", chunk);
			using HttpRequestMessage req = await BuildRequest(HttpMethod.Get, "/jobs/batch?ids=" + chunkTerm, ct);
			using HttpResponseMessage res = await _httpClient.SendAsync(req, ct);
			res.EnsureSuccessStatusCode();
			JobsBatchResponse jobsBatchResponse = await res.Content.ReadFromJsonAsync<JobsBatchResponse>(JsonOptions, ct);
			if (jobsBatchResponse != null && jobsBatchResponse.Jobs != null)
			{
				results.AddRange(jobsBatchResponse.Jobs);
			}
		}
		return results;
	}

	public async Task CancelJobAsync(int jobId, CancellationToken ct = default(CancellationToken))
	{
		using HttpRequestMessage req = await BuildRequest(HttpMethod.Patch, $"/jobs/{jobId}", ct);
		req.Content = JsonContent.Create(new JobUpdateRequest
		{
			Status = "cancelled"
		}, null, JsonOptions);
		using HttpResponseMessage httpResponseMessage = await _httpClient.SendAsync(req, ct);
		httpResponseMessage.EnsureSuccessStatusCode();
	}

	public async Task<JobArtifactDownload[]> GetJobArtifactDownloadsAsync(int jobId, CancellationToken ct = default(CancellationToken))
	{
		using HttpRequestMessage req = await BuildRequest(HttpMethod.Get, $"/jobs/{jobId}/artifacts/download", ct);
		using HttpResponseMessage res = await _httpClient.SendAsync(req, ct);
		res.EnsureSuccessStatusCode();
		return await res.Content.ReadFromJsonAsync<JobArtifactDownload[]>(JsonOptions, ct);
	}

	public async Task<ProjectResponse> GetProjectBySlugAsync(string slug, CancellationToken ct = default(CancellationToken))
	{
		if (_cachedProject != null && _cachedProject.Slug == slug)
		{
			return _cachedProject;
		}
		using HttpRequestMessage req = await BuildRequest(HttpMethod.Get, "/projects/" + Uri.EscapeDataString(slug), ct);
		using HttpResponseMessage res = await _httpClient.SendAsync(req, ct);
		res.EnsureSuccessStatusCode();
		_cachedProject = await res.Content.ReadFromJsonAsync<ProjectResponse>(JsonOptions, ct);
		return _cachedProject;
	}

	public async Task<ExperimentResponse> CreateExperimentAsync(CreateExperimentRequest data, CancellationToken ct = default(CancellationToken))
	{
		using HttpRequestMessage req = await BuildRequest(HttpMethod.Post, "/experiments", ct);
		req.Content = JsonContent.Create(data, null, JsonOptions);
		using HttpResponseMessage res = await _httpClient.SendAsync(req, ct);
		res.EnsureSuccessStatusCode();
		return await res.Content.ReadFromJsonAsync<ExperimentResponse>(JsonOptions, ct);
	}

	public async Task DownloadFileAsync(string presignedUrl, string localFilePath, ProgressReporter reporter = null, CancellationToken ct = default(CancellationToken))
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		reporter.ReportMessage("Downloading to {fn}", localFilePath);
		using HttpResponseMessage response = await _transferClient.GetAsync(presignedUrl, HttpCompletionOption.ResponseHeadersRead, ct);
		response.EnsureSuccessStatusCode();
		long totalBytes = response.Content.Headers.ContentLength ?? (-1);
		using Stream contentStream = await response.Content.ReadAsStreamAsync(ct);
		using FileStream fs = new FileStream(localFilePath, FileMode.Create, FileAccess.Write);
		byte[] buffer = new byte[81920];
		long totalRead = 0L;
		while (true)
		{
			int readAsync;
			int bytesRead = (readAsync = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct));
			if (readAsync <= 0)
			{
				break;
			}
			await fs.WriteAsync(buffer, 0, bytesRead, ct);
			totalRead += bytesRead;
			if (totalBytes > 0)
			{
				int prog = (int)(100.0 * (double)totalRead / (double)totalBytes);
				reporter.ReportProgress(prog);
			}
		}
		reporter.ReportMessage("Download completed.");
		reporter.ReportProgress(100);
	}

	public async Task<bool> PingAsync(CancellationToken ct = default(CancellationToken))
	{
		_ = 1;
		try
		{
			using HttpRequestMessage req = await BuildRequest(HttpMethod.Get, "/system/status", ct);
			using HttpResponseMessage httpResponseMessage = await _httpClient.SendAsync(req, ct);
			return httpResponseMessage.IsSuccessStatusCode;
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "GPU-Hub ping failed");
			return false;
		}
	}
}
