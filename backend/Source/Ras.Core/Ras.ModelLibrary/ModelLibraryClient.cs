using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Amazon.S3.Model;
using Utility.Communication;
using Utility.Files;
using Utility.Progress;

namespace Ras.ModelLibrary;

public sealed class ModelLibraryClient : IDisposable
{
	public const string ServerUrl = "https://s3.hecdev.net";

	public const string ModelBucket = "ras-model-uploads";

	private readonly S3Client _client;

	private bool _isDisposed;

	public ModelLibraryClient(string accessKey, string secretKey)
	{
		if (string.IsNullOrEmpty(accessKey))
		{
			throw new ArgumentException("Access key is required.", "accessKey");
		}
		if (string.IsNullOrEmpty(secretKey))
		{
			throw new ArgumentException("Secret key is required.", "secretKey");
		}
		_client = new S3Client("https://s3.hecdev.net", accessKey, secretKey, forcePathStyleQueries: true);
	}

	public async Task PushAsync(string uniqueId, string zipPath, string owner = "", ProgressReporter reporter = null, bool force = false)
	{
		if (string.IsNullOrEmpty(uniqueId))
		{
			throw new ArgumentException("Model ID is required.", "uniqueId");
		}
		if (string.IsNullOrEmpty(zipPath))
		{
			throw new ArgumentException("Zip path is required.", "zipPath");
		}
		if (!File.Exists(zipPath))
		{
			throw new FileNotFoundException("Model zip not found.", zipPath);
		}
		bool notForce = !force;
		if (notForce)
		{
			notForce = await ExistsAsync(uniqueId);
		}
		if (notForce)
		{
			throw new InvalidOperationException("A model with ID '" + uniqueId + "' already exists. Pass force to overwrite it.");
		}
		await _client.PushSingleFileAsync("ras-model-uploads", uniqueId, zipPath, owner, reporter);
	}

	public async Task PushDirectoryAsync(string uniqueId, string projectDirectory, string owner = "", ProgressReporter reporter = null, bool force = false)
	{
		if (string.IsNullOrEmpty(projectDirectory))
		{
			throw new ArgumentException("Project directory is required.", "projectDirectory");
		}
		if (!Directory.Exists(projectDirectory))
		{
			throw new DirectoryNotFoundException("Project directory not found: " + projectDirectory);
		}
		bool notForce = !force;
		if (notForce)
		{
			notForce = await ExistsAsync(uniqueId);
		}
		if (notForce)
		{
			throw new InvalidOperationException("A model with ID '" + uniqueId + "' already exists. Pass force to overwrite it.");
		}
		string zip = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
		try
		{
			reporter?.ReportMessage("Zipping {fn}", projectDirectory);
			await Task.Run(() =>
			{
				ZipFile.CreateFromDirectory(projectDirectory, zip, CompressionLevel.Optimal, includeBaseDirectory: true);
			});
			await PushAsync(uniqueId, zip, owner, reporter, force);
		}
		finally
		{
			FileHelpers.TryDelete(zip);
		}
	}

	public async Task PullAsync(string uniqueId, string destinationZipPath, ProgressReporter reporter = null)
	{
		if (string.IsNullOrEmpty(uniqueId))
		{
			throw new ArgumentException("Model ID is required.", "uniqueId");
		}
		if (string.IsNullOrEmpty(destinationZipPath))
		{
			throw new ArgumentException("Destination path is required.", "destinationZipPath");
		}
		string directoryName = Path.GetDirectoryName(destinationZipPath);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		await _client.PullSingleFileParallelAsync("ras-model-uploads", uniqueId, destinationZipPath, reporter, 8, 8388608L);
	}

	public async Task<string> PullToTempAsync(string uniqueId, ProgressReporter reporter = null)
	{
		string tmp = Path.GetTempFileName();
		try
		{
			await PullAsync(uniqueId, tmp, reporter);
			return tmp;
		}
		catch
		{
			FileHelpers.TryDelete(tmp);
			throw;
		}
	}

	public async Task<string> PullAndExtractAsync(string uniqueId, string parentDirectory, ProgressReporter reporter = null)
	{
		if (string.IsNullOrEmpty(parentDirectory))
		{
			throw new ArgumentException("Parent directory is required.", "parentDirectory");
		}
		Directory.CreateDirectory(parentDirectory);
		string pullToTempAsync = await PullToTempAsync(uniqueId, reporter);
		try
		{
			string path = Path.Combine(parentDirectory, uniqueId);
			path = FileHelpers.GetUniqueDirectory(path);
			reporter?.ReportMessage("Extracting {fn}", uniqueId);
			ZipFile.ExtractToDirectory(pullToTempAsync, path, overwriteFiles: true);
			string[] directories = Directory.GetDirectories(path);
			string[] files = Directory.GetFiles(path);
			if (directories.Length == 1 && files.Length == 0)
			{
				string pathNewGuidTerm = path + "_TMP_" + Guid.NewGuid();
				Directory.Move(path, pathNewGuidTerm);
				string directory = Directory.GetDirectories(pathNewGuidTerm)[0];
				string fileName = Path.GetFileName(directory);
				path = Path.Combine(parentDirectory, fileName);
				if (Directory.Exists(path) && Directory.GetFiles(path).Length != 0)
				{
					path = FileHelpers.GetUniqueDirectory(path);
				}
				Directory.Move(directory, path);
				Directory.Delete(pathNewGuidTerm, recursive: true);
			}
			return path;
		}
		finally
		{
			FileHelpers.TryDelete(pullToTempAsync);
		}
	}

	public async Task<bool> ExistsAsync(string uniqueId)
	{
		if (string.IsNullOrEmpty(uniqueId))
		{
			return false;
		}
		return await _client.ObjectExists("ras-model-uploads", uniqueId);
	}

	public async Task<List<ModelLibraryEntry>> ListAsync(string idPrefix = "")
	{
		List<S3Object> listObjectsAsync = await _client.ListObjectsAsync("ras-model-uploads", idPrefix, fetchOwner: true);
		List<ModelLibraryEntry> modelLibraryEntries = new List<ModelLibraryEntry>(listObjectsAsync.Count);
		foreach (S3Object listObjectsAsync2 in listObjectsAsync)
		{
			modelLibraryEntries.Add(new ModelLibraryEntry(listObjectsAsync2.Key, listObjectsAsync2.Size.GetValueOrDefault(), listObjectsAsync2.LastModified.GetValueOrDefault(), listObjectsAsync2.Owner?.DisplayName ?? ""));
		}
		return modelLibraryEntries;
	}

	public void Dispose()
	{
		if (!_isDisposed)
		{
			_client.Dispose();
			_isDisposed = true;
		}
	}
}
