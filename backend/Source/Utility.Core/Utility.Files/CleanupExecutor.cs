using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Progress;

namespace Utility.Files;

public static class CleanupExecutor
{
	private static readonly ILogger _logger = LogHelper.GetLogger("CleanupExecutor");

	public static CleanupResult ExecuteAll(CleanupReport report, ProgressReporter pr)
	{
		return ExecuteSubset(report.Candidates, pr);
	}

	public static CleanupResult ExecuteSubset(List<CleanupCandidate> candidates, ProgressReporter pr)
	{
		CleanupResult result = new CleanupResult();
		for (int i = 0; i < candidates.Count; i++)
		{
			pr.ReportProgressFraction(i, candidates.Count);
			ExecuteCandidate(candidates[i], result, pr);
		}
		pr.ReportProgressFraction(1f);
		return result;
	}

	private static void ExecuteCandidate(CleanupCandidate candidate, CleanupResult result, ProgressReporter pr)
	{
		foreach (string auxiliaryFile in candidate.AuxiliaryFiles)
		{
			if (Directory.Exists(auxiliaryFile))
			{
				if (!TryDeleteDirectory(auxiliaryFile))
				{
					_logger.LogWarning("Failed to delete auxiliary directory: {Dir}", auxiliaryFile);
					pr.ReportWarning("Failed to delete directory: " + auxiliaryFile);
				}
			}
			else if (File.Exists(auxiliaryFile) && !FileHelpers.TryDelete(auxiliaryFile))
			{
				_logger.LogWarning("Failed to delete auxiliary file: {File}", auxiliaryFile);
				pr.ReportWarning("Failed to delete file: " + auxiliaryFile);
			}
		}
		if (FileHelpers.TryDelete(candidate.FilePath))
		{
			result.FilesDeleted++;
			result.BytesRecovered += candidate.TotalRecoverableBytes;
			TryDeleteEmptyParentDirectory(candidate.FilePath);
		}
		else
		{
			result.FilesFailed++;
			result.FailedFiles.Add(candidate.FilePath);
			_logger.LogWarning("Failed to delete: {File}", candidate.FilePath);
			pr.ReportWarning("Failed to delete: " + candidate.FilePath);
		}
	}

	private static void TryDeleteEmptyParentDirectory(string filePath)
	{
		try
		{
			string directoryName = Path.GetDirectoryName(filePath);
			if (!string.IsNullOrEmpty(directoryName) && Directory.Exists(directoryName) && !Directory.EnumerateFileSystemEntries(directoryName).Any())
			{
				Directory.Delete(directoryName);
			}
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Failed to remove empty directory for {File}", filePath);
		}
	}

	private static bool TryDeleteDirectory(string path)
	{
		try
		{
			Directory.Delete(path, recursive: true);
			return true;
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Failed to delete directory {Dir}", path);
			return false;
		}
	}
}
