using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Ras.Optimization;

public sealed class CalibrationJournal : IDisposable
{
	private static readonly ILogger _logger = LogHelper.GetLogger("CalibrationJournal");

	public const string FileName = "calibration.journal.jsonl";

	private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
	{
		NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
		Converters = { (JsonConverter)new JsonStringEnumConverter() }
	};

	private readonly object _lock = new object();

	private readonly StreamWriter _writer;

	private bool _failed;

	private CalibrationJournal(StreamWriter writer)
	{
		_writer = writer;
	}

	public static CalibrationJournal CreateNew(string path, JournalFingerprint fingerprint, IReadOnlyList<RunResult> priorRuns = null)
	{
		if (string.IsNullOrEmpty(path))
		{
			throw new ArgumentException("A journal path is required.", "path");
		}
		if (fingerprint == null)
		{
			throw new ArgumentNullException("fingerprint");
		}
		RotateExisting(path);
		StreamWriter streamWriter = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
		streamWriter.WriteLine(JsonSerializer.Serialize(fingerprint, _jsonOptions));
		if (priorRuns != null)
		{
			foreach (RunResult priorRun in priorRuns)
			{
				streamWriter.WriteLine(JsonSerializer.Serialize(priorRun, _jsonOptions));
			}
		}
		streamWriter.Flush();
		return new CalibrationJournal(streamWriter);
	}

	public static bool TryLoad(string path, out JournalFingerprint fingerprint, out List<RunResult> runs)
	{
		fingerprint = null;
		runs = null;
		if (string.IsNullOrEmpty(path) || !File.Exists(path))
		{
			return false;
		}
		string[] allLines;
		try
		{
			allLines = File.ReadAllLines(path);
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Failed to read journal {Path}.", path);
			return false;
		}
		if (allLines.Length == 0)
		{
			return false;
		}
		try
		{
			fingerprint = JsonSerializer.Deserialize<JournalFingerprint>(allLines[0], _jsonOptions);
		}
		catch (JsonException exception2)
		{
			_logger.LogWarning(exception2, "Journal {Path} has an unreadable header.", path);
			return false;
		}
		if (fingerprint == null || fingerprint.ParameterNames == null)
		{
			return false;
		}
		runs = new List<RunResult>();
		for (int i = 1; i < allLines.Length; i++)
		{
			if (!string.IsNullOrWhiteSpace(allLines[i]))
			{
				RunResult runResult;
				try
				{
					runResult = JsonSerializer.Deserialize<RunResult>(allLines[i], _jsonOptions);
				}
				catch (JsonException)
				{
					_logger.LogWarning("Skipping unreadable journal line {Line} in {Path} (torn write?).", i + 1, path);
					continue;
				}
				if (runResult != null && runResult.Physical != null && !(runResult.FailureReason == "Cancelled"))
				{
					runs.Add(runResult);
				}
			}
		}
		return true;
	}

	public void Append(RunResult result)
	{
		if (result == null || result.FailureReason == "Cancelled")
		{
			return;
		}
		lock (_lock)
		{
			if (_failed)
			{
				return;
			}
			try
			{
				_writer.WriteLine(JsonSerializer.Serialize(result, _jsonOptions));
				_writer.Flush();
			}
			catch (Exception exception)
			{
				_failed = true;
				_logger.LogWarning(exception, "Journal append failed; journaling disabled for the rest of this calibration.");
			}
		}
	}

	public void Dispose()
	{
		lock (_lock)
		{
			try
			{
				_writer.Dispose();
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Failed to close the journal cleanly.");
			}
		}
	}

	private static void RotateExisting(string path)
	{
		if (File.Exists(path))
		{
			string directoryName = Path.GetDirectoryName(path);
			string now = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
			string path2 = Path.Combine(directoryName, "calibration.journal." + now + ".jsonl");
			int unity = 1;
			while (File.Exists(path2))
			{
				path2 = Path.Combine(directoryName, $"calibration.journal.{now}-{unity++}.jsonl");
			}
			File.Move(path, path2);
			_logger.LogInformation("Rotated previous journal to {Backup}.", path2);
		}
	}
}
