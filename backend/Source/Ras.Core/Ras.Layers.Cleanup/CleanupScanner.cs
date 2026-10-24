using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Utility.Files;
using Utility.Logging;

namespace Ras.Layers.Cleanup;

public static class CleanupScanner
{
	private static readonly ILogger _logger = LogHelper.GetLogger("CleanupScanner");

	private const string BackupPrefix = "Backup ";

	private const string TimestampFormat = "yyyy-MM-dd HH_mm_ss";

	private const string BackupsDirSuffix = " Backups";

	private static readonly Regex UniqueSuffixPattern = new Regex(" \\(\\d+\\)$", RegexOptions.Compiled);

	public static CleanupReport Scan(Project project, bool verbose = false)
	{
		return Scan(project, CleanupPolicyDefaults.Geometry.Default, CleanupPolicyDefaults.Plans.Default, CleanupPolicyDefaults.Results.Default, CleanupPolicyDefaults.Results.CleanAuxiliaryFiles, verbose);
	}

	public static CleanupReport Scan(Project project, CleanupPolicy geometryPolicy, CleanupPolicy planPolicy, CleanupPolicy resultPolicy, bool cleanResultAuxiliary = true, bool verbose = false)
	{
		CleanupReport cleanupReport = new CleanupReport();
		DateTime now = DateTime.Now;
		List<CleanupEvaluation> evaluations = (verbose ? cleanupReport.Evaluations : null);
		ScanBackupDirectories(project.GeometriesDirectory, BackupCategory.Geometry, geometryPolicy, now, cleanupReport, evaluations);
		ScanBackupDirectories(project.PlansDirectory, BackupCategory.Plan, planPolicy, now, cleanupReport, evaluations);
		ScanResultDirectory(project.ResultsDirectory, resultPolicy, now, cleanupReport, cleanResultAuxiliary, evaluations);
		return cleanupReport;
	}

	private static void ScanBackupDirectories(string parentDir, BackupCategory category, CleanupPolicy policy, DateTime now, CleanupReport report, List<CleanupEvaluation> evaluations)
	{
		if (string.IsNullOrEmpty(parentDir) || !Directory.Exists(parentDir))
		{
			return;
		}
		string[] directories;
		try
		{
			directories = Directory.GetDirectories(parentDir, "* Backups");
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Failed to enumerate backup directories in {Dir}", parentDir);
			return;
		}
		string[] directoriesArray = directories;
		foreach (string directory in directoriesArray)
		{
			string fileName = Path.GetFileName(directory);
			string parentName = fileName.Substring(0, fileName.Length - " Backups".Length);
			string[] files;
			try
			{
				files = Directory.GetFiles(directory, "*.h5");
			}
			catch (Exception exception2)
			{
				_logger.LogWarning(exception2, "Failed to enumerate files in {Dir}", directory);
				continue;
			}
			List<(string, DateTime, long)> valueTuples = new List<(string, DateTime, long)>();
			string category2 = CategoryLabel(category);
			string[] filesArray = files;
			foreach (string filePath in filesArray)
			{
				long fileSize = GetFileSize(filePath);
				report.TotalFilesScanned++;
				report.TotalStorageBytes += fileSize;
				if (IsAutoBackup(filePath, out var timestamp))
				{
					valueTuples.Add((filePath, timestamp, fileSize));
					continue;
				}
				report.NamedBackupsSkipped++;
				evaluations?.Add(new CleanupEvaluation
				{
					FilePath = filePath,
					SizeBytes = fileSize,
					Timestamp = File.GetLastWriteTime(filePath),
					Category = category2,
					ParentName = parentName,
					Retained = true,
					Reason = "Named backup (protected)"
				});
			}
			List<CleanupCandidate> collection = ApplyRetentionPolicy(valueTuples, category2, parentName, policy, now, includeAuxiliaryFiles: false, evaluations);
			report.Candidates.AddRange(collection);
		}
	}

	private static void ScanResultDirectory(string resultsDir, CleanupPolicy policy, DateTime now, CleanupReport report, bool cleanResultAuxiliary, List<CleanupEvaluation> evaluations)
	{
		if (string.IsNullOrEmpty(resultsDir) || !Directory.Exists(resultsDir))
		{
			return;
		}
		string[] files;
		try
		{
			files = Directory.GetFiles(resultsDir, "*.h5");
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Failed to enumerate result files in {Dir}", resultsDir);
			return;
		}
		Dictionary<string, List<(string, DateTime, long)>> valueTupleListByString = new Dictionary<string, List<(string, DateTime, long)>>();
		string[] filesArray = files;
		foreach (string filesArray2 in filesArray)
		{
			long fileSize = GetFileSize(filesArray2);
			report.TotalFilesScanned++;
			report.TotalStorageBytes += fileSize;
			string resultExportDirectory = GetResultExportDirectory(filesArray2);
			if (Directory.Exists(resultExportDirectory))
			{
				long directorySize = GetDirectorySize(resultExportDirectory);
				report.TotalStorageBytes += directorySize;
			}
			string resultBaseName = GetResultBaseName(filesArray2);
			DateTime lastWriteTime = File.GetLastWriteTime(filesArray2);
			if (!valueTupleListByString.ContainsKey(resultBaseName))
			{
				valueTupleListByString[resultBaseName] = new List<(string, DateTime, long)>();
			}
			valueTupleListByString[resultBaseName].Add((filesArray2, lastWriteTime, fileSize));
		}
		string category = CategoryLabel(BackupCategory.Result);
		foreach (KeyValuePair<string, List<(string, DateTime, long)>> localValue in valueTupleListByString)
		{
			string key = localValue.Key;
			List<(string, DateTime, long)> localValueList = localValue.Value;
			if (localValueList.Count <= 1)
			{
				foreach (var localValue2 in localValueList)
				{
					evaluations?.Add(new CleanupEvaluation
					{
						FilePath = localValue2.Item1,
						SizeBytes = localValue2.Item3,
						Timestamp = localValue2.Item2,
						Category = category,
						ParentName = key,
						Retained = true,
						Reason = "Only result (protected)"
					});
				}
				continue;
			}
			localValueList.Sort(((string path, DateTime timestamp, long size) a, (string path, DateTime timestamp, long size) b) => b.timestamp.CompareTo(a.timestamp));
			(string, DateTime, long) tuple = localValueList[0];
			evaluations?.Add(new CleanupEvaluation
			{
				FilePath = tuple.Item1,
				SizeBytes = tuple.Item3,
				Timestamp = tuple.Item2,
				Category = category,
				ParentName = key,
				Retained = true,
				Reason = "Newest result (protected)"
			});
			List<CleanupCandidate> collection = ApplyRetentionPolicy(localValueList.Skip(1).ToList(), category, key, policy, now, cleanResultAuxiliary, evaluations);
			report.Candidates.AddRange(collection);
		}
	}

	internal static List<CleanupCandidate> ApplyRetentionPolicy(List<(string path, DateTime timestamp, long size)> files, string category, string layerName, CleanupPolicy policy, DateTime now, bool includeAuxiliaryFiles = false, List<CleanupEvaluation> evaluations = null)
	{
		List<CleanupCandidate> cleanupCandidates = new List<CleanupCandidate>();
		List<(string, DateTime, long)> valueTuples = new List<(string, DateTime, long)>();
		List<(string, DateTime, long)> valueTuplesList = new List<(string, DateTime, long)>();
		List<(string, DateTime, long)> valueTuples2 = new List<(string, DateTime, long)>();
		List<(string, DateTime, long)> valueTuples3 = new List<(string, DateTime, long)>();
		foreach (var file in files)
		{
			TimeSpan timeSpan = now - file.timestamp;
			if (timeSpan <= policy.KeepAll)
			{
				evaluations?.Add(new CleanupEvaluation
				{
					FilePath = file.path,
					SizeBytes = file.size,
					Timestamp = file.timestamp,
					Category = category,
					ParentName = layerName,
					Retained = true,
					Reason = $"Within keep-all window ({timeSpan.Days} days old)"
				});
			}
			else if (timeSpan <= policy.KeepHourly)
			{
				valueTuples.Add(file);
			}
			else if (timeSpan <= policy.KeepDaily)
			{
				valueTuplesList.Add(file);
			}
			else if (timeSpan <= policy.KeepWeekly)
			{
				valueTuples2.Add(file);
			}
			else
			{
				valueTuples3.Add(file);
			}
		}
		ThinByGroup(valueTuples, ((string path, DateTime timestamp, long size) f) => (Date: f.timestamp.Date, Hour: f.timestamp.Hour), "Superseded by hourly retention", "Retained as hourly representative", category, layerName, includeAuxiliaryFiles, cleanupCandidates, evaluations);
		ThinByGroup(valueTuplesList, ((string path, DateTime timestamp, long size) f) => f.timestamp.Date, "Superseded by daily retention", "Retained as daily representative", category, layerName, includeAuxiliaryFiles, cleanupCandidates, evaluations);
		ThinByGroup(valueTuples2, ((string path, DateTime timestamp, long size) f) => ISOWeek(f.timestamp), "Superseded by weekly retention", "Retained as weekly representative", category, layerName, includeAuxiliaryFiles, cleanupCandidates, evaluations);
		foreach (var localValue in valueTuples3)
		{
			cleanupCandidates.Add(CreateCandidate(localValue, category, layerName, "Beyond retention window", includeAuxiliaryFiles));
			evaluations?.Add(new CleanupEvaluation
			{
				FilePath = localValue.Item1,
				SizeBytes = localValue.Item3,
				Timestamp = localValue.Item2,
				Category = category,
				ParentName = layerName,
				Retained = false,
				Reason = "Beyond retention window"
			});
		}
		return cleanupCandidates;
	}

	private static void ThinByGroup<TKey>(List<(string path, DateTime timestamp, long size)> files, Func<(string path, DateTime timestamp, long size), TKey> keySelector, string deleteReason, string retainReason, string category, string layerName, bool includeAuxiliaryFiles, List<CleanupCandidate> candidates, List<CleanupEvaluation> evaluations)
	{
		foreach (IGrouping<TKey, (string, DateTime, long)> groupBy in files.GroupBy(keySelector))
		{
			List<(string, DateTime, long)> orderByDescending = groupBy.OrderByDescending(((string path, DateTime timestamp, long size) f) => f.timestamp).ToList();
			evaluations?.Add(new CleanupEvaluation
			{
				FilePath = orderByDescending[0].Item1,
				SizeBytes = orderByDescending[0].Item3,
				Timestamp = orderByDescending[0].Item2,
				Category = category,
				ParentName = layerName,
				Retained = true,
				Reason = retainReason
			});
			for (int unity = 1; unity < orderByDescending.Count; unity++)
			{
				candidates.Add(CreateCandidate(orderByDescending[unity], category, layerName, deleteReason, includeAuxiliaryFiles));
				evaluations?.Add(new CleanupEvaluation
				{
					FilePath = orderByDescending[unity].Item1,
					SizeBytes = orderByDescending[unity].Item3,
					Timestamp = orderByDescending[unity].Item2,
					Category = category,
					ParentName = layerName,
					Retained = false,
					Reason = deleteReason
				});
			}
		}
	}

	private static CleanupCandidate CreateCandidate((string path, DateTime timestamp, long size) file, string category, string layerName, string reason, bool includeAuxiliaryFiles)
	{
		List<string> auxiliaryFiles = new List<string>();
		long directorySize = 0L;
		if (includeAuxiliaryFiles)
		{
			string resultExportDirectory = GetResultExportDirectory(file.path);
			if (Directory.Exists(resultExportDirectory))
			{
				auxiliaryFiles.Add(resultExportDirectory);
				directorySize = GetDirectorySize(resultExportDirectory);
			}
		}
		return new CleanupCandidate
		{
			FilePath = file.path,
			SizeBytes = file.size,
			Timestamp = file.timestamp,
			Category = category,
			ParentName = layerName,
			Reason = reason,
			AuxiliaryFiles = auxiliaryFiles,
			TotalRecoverableBytes = file.size + directorySize
		};
	}

	private static string CategoryLabel(BackupCategory category)
	{
		return category switch
		{
			BackupCategory.Geometry => "Geometry backup", 
			BackupCategory.Plan => "Plan backup", 
			BackupCategory.Result => "Old result", 
			_ => "File", 
		};
	}

	private static (int year, int week) ISOWeek(DateTime dt)
	{
		return (year: System.Globalization.ISOWeek.GetYear(dt), week: System.Globalization.ISOWeek.GetWeekOfYear(dt));
	}

	internal static bool IsAutoBackup(string filePath, out DateTime timestamp)
	{
		timestamp = default;
		return TryParseBackupTimestamp(Path.GetFileNameWithoutExtension(filePath), out timestamp);
	}

	internal static bool TryParseBackupTimestamp(string filenameWithoutExtension, out DateTime timestamp)
	{
		timestamp = default;
		if (!filenameWithoutExtension.StartsWith("Backup "))
		{
			return false;
		}
		string input = filenameWithoutExtension.Substring("Backup ".Length);
		input = UniqueSuffixPattern.Replace(input, "");
		return DateTime.TryParseExact(input, "yyyy-MM-dd HH_mm_ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);
	}

	internal static string GetResultBaseName(string filePath)
	{
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
		return UniqueSuffixPattern.Replace(fileNameWithoutExtension, "");
	}

	private static string GetResultExportDirectory(string resultFilePath)
	{
		string? directoryName = Path.GetDirectoryName(resultFilePath);
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(resultFilePath);
		return Path.Combine(directoryName, fileNameWithoutExtension).Trim();
	}

	private static long GetFileSize(string path)
	{
		try
		{
			return new FileInfo(path).Length;
		}
		catch
		{
			return 0L;
		}
	}

	private static long GetDirectorySize(string path)
	{
		try
		{
			return new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum((FileInfo f) => f.Length);
		}
		catch
		{
			return 0L;
		}
	}
}
