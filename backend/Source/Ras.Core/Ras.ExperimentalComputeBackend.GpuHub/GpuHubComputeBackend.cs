using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Ras.Layers;
using Utility.Communication;
using Utility.Logging;
using Utility.Progress;

namespace Ras.ExperimentalComputeBackend.GpuHub;

public sealed class GpuHubComputeBackend : IComputeBackend
{
	private static readonly ILogger _logger = LogHelper.GetLogger("GpuHubComputeBackend");

	private const int DefaultCpuJobCores = 4;

	private const int DefaultCpuJobRamMb = 8192;

	private readonly IGpuHubCredentialProvider _creds;

	private readonly GpuHubClient _client;

	public string Name => "GPU Hub";

	public ComputeBackendInfo Info => new ComputeBackendInfo
	{
		Id = "gpu-hub",
		DisplayName = "GPU Hub (Remote)",
		IsRemote = true,
		RequiresUpload = true,
		SupportsGpu = true,
		SupportsCpu = true,
		MaxConcurrency = 0,
		SupportsExperiments = true,
		SupportsBatchStatus = true
	};

	public GpuHubComputeBackend(IGpuHubCredentialProvider creds)
	{
		_creds = creds;
		_client = new GpuHubClient(creds);
	}

	public async ValueTask<bool> IsAvailableAsync(CancellationToken ct = default(CancellationToken))
	{
		if (!(await _creds.HasTokenAsync(ct)))
		{
			return false;
		}
		return await _client.PingAsync(ct);
	}

	public async Task<ComputeOutcome> ComputeAsync(ComputeRequest request, ComputeContext context, CancellationToken ct = default(CancellationToken))
	{
		ProgressReporter reporter = context.Reporter ?? ProgressReporter.None();
		int jobId = 0;
		try
		{
			using S3Client s3 = _creds.TryCreateS3Client();
			if (s3 == null)
			{
				return ComputeOutcome.Fail(OperationResult.Fail("S3 credentials not configured. Set them in Settings > Credentials."));
			}
			string s3Key = $"uploads/{Guid.NewGuid()}/{Path.GetFileName(request.ReadyToRunPath)}";
			reporter.ReportMessage("Uploading to S3: " + s3Key);
			await s3.PushSingleFileAsync("gpu-hub-temporary-files", s3Key, request.ReadyToRunPath, "", reporter.SubTask("Upload", 0.1f, 0.2f));
			string presignedInputUrl = s3.GetPresignedURL("gpu-hub-temporary-files", s3Key);
			reporter.ReportMessage("Resolving GPU-Hub project...");
			ProjectResponse projectResponse = await _client.GetProjectBySlugAsync("ras-2025-remote-compute", ct);
			int? experimentId = null;
			if (!string.IsNullOrEmpty(request.ExperimentKey) && int.TryParse(request.ExperimentKey, out var result))
			{
				experimentId = result;
			}
			bool preferGpuHintsTerm = !request.Hints.PreferGpu;
			reporter.ReportMessage(preferGpuHintsTerm ? "Creating remote compute job (CPU)..." : "Creating remote compute job...");
			CreateJobResponse createJobResponse = await _client.CreateJobAsync(new CreateJobRequest
			{
				ProjectId = projectResponse.Id,
				Name = request.DisplayName,
				Parameters = new Dictionary<string, object> { ["i"] = presignedInputUrl },
				ExperimentId = experimentId,
				GpuCount = (preferGpuHintsTerm ? new int?(0) : ((int?)null)),
				CpuCores = (preferGpuHintsTerm ? new int?((request.Hints.CpuCores > 0) ? request.Hints.CpuCores : 4) : ((int?)null)),
				RamMb = (preferGpuHintsTerm ? new int?((request.Hints.RamMb > 0) ? request.Hints.RamMb : 8192) : ((int?)null))
			}, ct);
			jobId = createJobResponse.Id;
			reporter.ReportMessage($"Job created: {createJobResponse.Id} (status: {createJobResponse.Status})");
			reporter.ReportMessage("Waiting for remote compute to complete...");
			string lastStatus = "";
			JobArtifactDownload[] jobArtifactDownloadsAsync;
			while (true)
			{
				if (reporter.CancelRequested || ct.IsCancellationRequested)
				{
					await TryCancelJobAsync(jobId);
					return ComputeOutcome.Fail(OperationResult.Fail(GeneralMessageCodes.GetUserOperationCancelled("Remote Compute")));
				}
				await Task.Delay(5000, ct);
				JobStatusResponse jobStatusResponse = await _client.GetJobStatusAsync(jobId, ct);
				if (jobStatusResponse.Status != lastStatus)
				{
					reporter.ReportMessage("Job status: " + jobStatusResponse.Status);
					lastStatus = jobStatusResponse.Status;
				}
				if (jobStatusResponse.Progress > 0)
				{
					reporter.ReportProgress(30 + (int)((double)jobStatusResponse.Progress * 0.5));
				}
				if (jobStatusResponse.Status == "completed")
				{
					reporter.ReportMessage("Remote compute completed successfully.");
					reporter.ReportMessage("Fetching job artifacts...");
					jobArtifactDownloadsAsync = await _client.GetJobArtifactDownloadsAsync(jobId, ct);
					if (jobArtifactDownloadsAsync != null)
					{
						break;
					}
					return ComputeOutcome.Fail(OperationResult.Fail("No artifacts returned by completed job."));
				}
				if (jobStatusResponse.Status == "failed")
				{
					string errorMessage = (string.IsNullOrEmpty(jobStatusResponse.ErrorMessage) ? "Unknown error" : jobStatusResponse.ErrorMessage);
					return ComputeOutcome.Fail(OperationResult.Fail("Remote compute failed: " + errorMessage));
				}
				if (jobStatusResponse.Status == "cancelled")
				{
					return ComputeOutcome.Fail(OperationResult.Fail("Remote compute was cancelled on the server."));
				}
			}
			JobArtifactDownload jobArtifactDownload = jobArtifactDownloadsAsync.FirstOrDefault((JobArtifactDownload a) => a.Name != null && a.Name.EndsWith(".h5", StringComparison.OrdinalIgnoreCase));
			if (jobArtifactDownload == null)
			{
				return ComputeOutcome.Fail(OperationResult.Fail("No result artifact found in completed job."));
			}
			string directoryName = Path.GetDirectoryName(request.ResultOutputPath);
			if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			reporter.ReportMessage("Downloading result: " + jobArtifactDownload.Name);
			await _client.DownloadFileAsync(jobArtifactDownload.Url, request.ResultOutputPath, reporter.SubTask("Download", 0.8f, 0.15f), ct);
			OperationResult operationResult = Result.TryLoad(request.ResultOutputPath, out var res);
			if (!operationResult.Result)
			{
				return ComputeOutcome.Fail(operationResult);
			}
			reporter.ReportMessage("Result downloaded: " + res.Name);
			reporter.ReportProgress(100);
			return ComputeOutcome.Success(res);
		}
		catch (OperationCanceledException)
		{
			await TryCancelJobAsync(jobId);
			return ComputeOutcome.Fail(OperationResult.Fail(GeneralMessageCodes.GetUserOperationCancelled("Remote Compute")));
		}
		catch (Exception ex2)
		{
			_logger.LogError(ex2, "Remote compute failed for {Name}", request.DisplayName);
			return ComputeOutcome.Fail(OperationResult.Fail("Remote Compute", ex2));
		}
	}

	private async Task TryCancelJobAsync(int jobId)
	{
		if (jobId <= 0)
		{
			return;
		}
		try
		{
			await _client.CancelJobAsync(jobId);
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Failed to cancel hub job {JobId}", jobId);
		}
	}
}
