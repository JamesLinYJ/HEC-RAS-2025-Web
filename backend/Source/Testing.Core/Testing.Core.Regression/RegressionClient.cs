using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Ras.Helpers;
using Ras.Layers;
using Utility.Logging;
using Utility.Progress;

namespace Testing.Core.Regression;

public class RegressionClient
{
	private static readonly ILogger _logger = LogHelper.GetLogger("RegressionClient");

	private static readonly HttpClient _http = new HttpClient();

	private readonly Func<RegressionClientSettings> _settings;

	private RegressionClientSettings Settings => _settings();

	private string BaseUrl => (Settings.BaseUrl ?? "").TrimEnd('/');

	public RegressionClient(Func<RegressionClientSettings> settingsProvider)
	{
		_settings = settingsProvider;
	}

	public async Task<RegressionClientConfig> GetClientConfigAsync()
	{
		try
		{
			return await _http.GetFromJsonAsync<RegressionClientConfig>(BaseUrl + "/api/client-config");
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Could not fetch client config from {Url}", BaseUrl);
			return null;
		}
	}

	public async Task<List<ModelRepoIdName>> ListProjectsAsync()
	{
		return await _http.GetFromJsonAsync<List<ModelRepoIdName>>(BaseUrl + "/project/all/latest");
	}

	public async Task<List<ModelRepoIdName>> ListGeometriesAsync()
	{
		return await _http.GetFromJsonAsync<List<ModelRepoIdName>>(BaseUrl + "/geometry/all/latest");
	}

	public async Task<ModelRepoProject> FetchProjectAsync(int projectId)
	{
		return await FetchOrNull<ModelRepoProject>($"{BaseUrl}/project/id/{projectId}");
	}

	public async Task<ModelRepoProject> FetchProjectByNameAsync(string name)
	{
		return await FetchOrNull<ModelRepoProject>(BaseUrl + "/project/byName?name=" + Uri.EscapeDataString(name));
	}

	public async Task<ModelRepoGeometry> FetchGeometryAsync(int geometryId)
	{
		return await FetchOrNull<ModelRepoGeometry>($"{BaseUrl}/geometry/id/{geometryId}");
	}

	public async Task<ModelRepoGeometry> FetchGeometryByNameAsync(string name)
	{
		return await FetchOrNull<ModelRepoGeometry>(BaseUrl + "/geometry/byName?name=" + Uri.EscapeDataString(name));
	}

	public async Task<List<CommitSummary>> ListCommitsAsync(string branch = null, long? sinceUnix = null, long? untilUnix = null)
	{
		List<string> stringList = new List<string>();
		if (!string.IsNullOrWhiteSpace(branch))
		{
			stringList.Add("branch=" + Uri.EscapeDataString(branch));
		}
		if (sinceUnix.HasValue)
		{
			stringList.Add($"sinceUnix={sinceUnix}");
		}
		if (untilUnix.HasValue)
		{
			stringList.Add($"untilUnix={untilUnix}");
		}
		string textValue = BaseUrl + "/commits";
		if (stringList.Count > 0)
		{
			textValue = textValue + "?" + string.Join("&", stringList);
		}
		return await _http.GetFromJsonAsync<List<CommitSummary>>(textValue);
	}

	public async Task<OperationResult> UploadProjectAsync(string projectDirectory, string uploadName, string folder = null, string description = null, ProgressReporter reporter = null)
	{
		RegressionClientSettings settings = Settings;
		reporter?.ReportMessage("Hashing project contents...");
		string contentHash = await Task.Run(() => ContentHashing.HashDirectory(projectDirectory));
		var (operationResult, exampleProjectManifest) = await ProjectHelpers.PushProject(new PushProjectParams
		{
			Server = settings.S3Server,
			Bucket = settings.Bucket,
			S3Key = settings.S3Key,
			S3Secret = settings.S3Secret,
			Directory = projectDirectory,
			SkipGlobalManifest = true,
			Reporter = reporter
		});
		if (!operationResult.Result)
		{
			return operationResult;
		}
		reporter?.ReportMessage("Registering \"" + uploadName + "\" with the testing utility...");
		RegisterProjectRequest registerProjectRequest = new RegisterProjectRequest
		{
			S3Key = exampleProjectManifest.Details.ManifestJSONKey,
			RasVersion = "N/A",
			Name = uploadName,
			ContentHash = contentHash,
			Folder = folder,
			Description = description
		};
		try
		{
			HttpResponseMessage httpResponseMessage = await _http.PostAsJsonAsync(BaseUrl + "/project/register", registerProjectRequest);
			if (!httpResponseMessage.IsSuccessStatusCode)
			{
				object statusCode = (int)httpResponseMessage.StatusCode;
				return OperationResult.Fail($"Failed to register \"{uploadName}\" in the testing repo ({statusCode}){await ReadErrorDetail(httpResponseMessage)}.");
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error registering project {Name}", uploadName);
			return OperationResult.Fail("Registering \"" + uploadName + "\"", ex);
		}
		return OperationResult.Success();
	}

	public async Task<OperationResult> DownloadProjectAsync(string manifestS3Key, string destinationDir, ProgressReporter reporter = null)
	{
		RegressionClientSettings settings = Settings;
		reporter?.ReportMessage("Downloading project from the testing server...");
		PullProjectParams pullParams = new PullProjectParams
		{
			Server = settings.S3Server,
			Bucket = settings.Bucket,
			S3Key = settings.S3Key,
			S3Secret = settings.S3Secret,
			ManifestKey = manifestS3Key,
			Directory = destinationDir,
			ForceOverwriteExisting = true,
			Reporter = reporter
		};
		try
		{
			return (await ProjectHelpers.PullProject(pullParams)).Item1;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error downloading project from key {Key}", manifestS3Key);
			return OperationResult.Fail("Downloading the project", ex);
		}
	}

	public async Task<OperationResult> UploadGeometryAsync(Geometry geometry, string uploadName, string folder = null, string description = null)
	{
		string tempGeoPath = Path.GetTempFileName();
		File.Delete(tempGeoPath);
		try
		{
			geometry.SaveGeometryBackup(tempGeoPath);
			return await UploadGeometryAsync(tempGeoPath, uploadName, folder, description);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error snapshotting geometry {Name} for upload", uploadName);
			return OperationResult.Fail("Snapshotting geometry \"" + uploadName + "\"", ex);
		}
		finally
		{
			if (File.Exists(tempGeoPath))
			{
				File.Delete(tempGeoPath);
			}
		}
	}

	public async Task<OperationResult> UploadGeometryAsync(string geometryH5Path, string uploadName, string folder = null, string description = null)
	{
		_ = 1;
		try
		{
			using MultipartFormDataContent formData = new MultipartFormDataContent();
			using FileStream fileStream = File.OpenRead(geometryH5Path);
			StreamContent content = new StreamContent(fileStream);
			formData.Add(content, "file", "geometry.h5");
			formData.Add(new StringContent(uploadName), "name");
			formData.Add(new StringContent("N/A"), "rasVersion");
			if (!string.IsNullOrWhiteSpace(folder))
			{
				formData.Add(new StringContent(folder), "folder");
			}
			if (!string.IsNullOrWhiteSpace(description))
			{
				formData.Add(new StringContent(description), "description");
			}
			HttpResponseMessage httpResponseMessage = await _http.PostAsync(BaseUrl + "/geometry/upload", formData);
			if (!httpResponseMessage.IsSuccessStatusCode)
			{
				object statusCode = (int)httpResponseMessage.StatusCode;
				return OperationResult.Fail($"Failed to upload geometry \"{uploadName}\" ({statusCode}){await ReadErrorDetail(httpResponseMessage)}.");
			}
			return OperationResult.Success();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error uploading geometry {Name}", uploadName);
			return OperationResult.Fail("Uploading geometry \"" + uploadName + "\"", ex);
		}
	}

	public async Task<OperationResult> ReconcileInterestPointsAsync(int projectId, List<ReconcileInterestPointRequest> points)
	{
		_ = 1;
		try
		{
			HttpResponseMessage httpResponseMessage = await _http.PostAsJsonAsync($"{BaseUrl}/project/{projectId}/interestPoints/reconcile", points);
			if (!httpResponseMessage.IsSuccessStatusCode)
			{
				object statusCode = (int)httpResponseMessage.StatusCode;
				return OperationResult.Fail($"Failed to update interest points ({statusCode}){await ReadErrorDetail(httpResponseMessage)}.");
			}
			return OperationResult.Success();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error reconciling interest points for project {ProjectId}", projectId);
			return OperationResult.Fail("Updating interest points", ex);
		}
	}

	public async Task<OperationResult> MoveObjectFolderAsync(ModelRepoObjectType type, int objectId, string folder)
	{
		string typeName = ModelRepoObject.TypeEnumToString(type);
		try
		{
			MoveObjectFolderRequest moveObjectFolderRequest = new MoveObjectFolderRequest
			{
				Folder = folder
			};
			HttpResponseMessage httpResponseMessage = await _http.PostAsJsonAsync($"{BaseUrl}/object/{typeName}/{objectId}/folder", moveObjectFolderRequest);
			if (!httpResponseMessage.IsSuccessStatusCode)
			{
				object statusCode = (int)httpResponseMessage.StatusCode;
				return OperationResult.Fail($"Failed to move to folder ({statusCode}){await ReadErrorDetail(httpResponseMessage)}.");
			}
			return OperationResult.Success();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error moving {Type} {ObjectId} to folder", typeName, objectId);
			return OperationResult.Fail("Moving to the folder", ex);
		}
	}

	public async Task<OperationResult> RenameObjectAsync(ModelRepoObjectType type, int objectId, string newName)
	{
		string typeName = ModelRepoObject.TypeEnumToString(type);
		try
		{
			RenameObjectRequest renameObjectRequest = new RenameObjectRequest
			{
				Name = newName
			};
			HttpResponseMessage httpResponseMessage = await _http.PostAsJsonAsync($"{BaseUrl}/object/{typeName}/{objectId}/rename", renameObjectRequest);
			if (!httpResponseMessage.IsSuccessStatusCode)
			{
				object statusCode = (int)httpResponseMessage.StatusCode;
				return OperationResult.Fail($"Failed to rename ({statusCode}){await ReadErrorDetail(httpResponseMessage)}.");
			}
			return OperationResult.Success();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error renaming {Type} {ObjectId}", typeName, objectId);
			return OperationResult.Fail("Renaming the object", ex);
		}
	}

	public async Task<OperationResult> SetObjectDescriptionAsync(ModelRepoObjectType type, int objectId, string description)
	{
		string typeName = ModelRepoObject.TypeEnumToString(type);
		try
		{
			SetObjectDescriptionRequest setObjectDescriptionRequest = new SetObjectDescriptionRequest
			{
				Description = description
			};
			HttpResponseMessage httpResponseMessage = await _http.PostAsJsonAsync($"{BaseUrl}/object/{typeName}/{objectId}/description", setObjectDescriptionRequest);
			if (!httpResponseMessage.IsSuccessStatusCode)
			{
				object statusCode = (int)httpResponseMessage.StatusCode;
				return OperationResult.Fail($"Failed to set the description ({statusCode}){await ReadErrorDetail(httpResponseMessage)}.");
			}
			return OperationResult.Success();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error setting description on {Type} {ObjectId}", typeName, objectId);
			return OperationResult.Fail("Setting the description", ex);
		}
	}

	public async Task<OperationResult> QueueRunAsync(AddTestRequest request)
	{
		_ = 1;
		try
		{
			HttpResponseMessage httpResponseMessage = await _http.PostAsJsonAsync(BaseUrl + "/queue/add", request);
			if (!httpResponseMessage.IsSuccessStatusCode)
			{
				object statusCode = (int)httpResponseMessage.StatusCode;
				return OperationResult.Fail($"Failed to queue the run ({statusCode}){await ReadErrorDetail(httpResponseMessage)}.");
			}
			return OperationResult.Success();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error queueing a run for commit {Commit}", request?.CommitHash);
			return OperationResult.Fail("Queueing the run", ex);
		}
	}

	private async Task<T> FetchOrNull<T>(string url) where T : class
	{
		HttpResponseMessage httpResponseMessage = await _http.GetAsync(url);
		if (!httpResponseMessage.IsSuccessStatusCode)
		{
			return null;
		}
		return await httpResponseMessage.Content.ReadFromJsonAsync<T>();
	}

	private static async Task<string> ReadErrorDetail(HttpResponseMessage response)
	{
		try
		{
			string trim = (await response.Content.ReadAsStringAsync()).Trim().Trim('"');
			if (string.IsNullOrWhiteSpace(trim) || trim.Length > 300)
			{
				return "";
			}
			return ": " + trim;
		}
		catch
		{
			return "";
		}
	}
}
