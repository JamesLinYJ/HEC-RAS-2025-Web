using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommandLine;
using Utility.Extensions;
using Utility.Files;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Process.BenchmarkCommands;

[Verb("hash", false, null, HelpText = "Writes a random binary file to a temp directory, then hashes it with different algorithms to check performance for this CPU/Disk.")]
public class HashBenchmarkArgs
{
	public const string Verb = "hash";

	[Option("file-size", Required = false, HelpText = "Set the file size to use. Units must be one of {B, KB, MB, GB, TB, KiB, MiB, GiB, TiB}.Ex: '1MB', '1.2 GiB', etc. Defaults to 1GB.")]
	public string FileSize { get; set; } = "1GB";

	[Option("file", Required = false, HelpText = "Compute hashes on this specific file, instead a randomly generated file.")]
	public string Filename { get; set; }

	[Option("rash-block", Required = false, HelpText = "Set the RASH block-size to this value (see 'file-size' for acceptable argument formatting). Defaults to 1MiB. Other block sizes will produce different RASH values.")]
	public string UserRashBlockSize { get; set; }

	public Task<bool> ExecuteAsync()
	{
		return Task.FromResult(Execute());
	}

	public bool Execute()
	{
		ProgressReporter progressReporter = ProgressReporter.ConsoleWrite();
		H5BenchmarkArgs.PrintMachineStats(progressReporter);
		OperationResult operationResult = FileHelpers.TryParseAsFileSize(FileSize, out var sizeInBytes);
		if (!operationResult)
		{
			progressReporter.ReportMessage(operationResult.GetConcatenatedMessages());
			return false;
		}
		int rashBlockSize = 1048576;
		if (!string.IsNullOrEmpty(UserRashBlockSize))
		{
			operationResult = FileHelpers.TryParseAsFileSize(UserRashBlockSize, out var sizeInBytes2);
			if (!operationResult)
			{
				progressReporter.ReportMessage(operationResult.GetConcatenatedMessages());
				return false;
			}
			rashBlockSize = (int)sizeInBytes2;
		}
		bool condition;
		string getTempFnTerm;
		if (string.IsNullOrEmpty(Filename))
		{
			condition = true;
			getTempFnTerm = GetTempFn();
			GenerateTempFile(getTempFnTerm, sizeInBytes, progressReporter);
		}
		else
		{
			condition = false;
			if (!File.Exists(Filename))
			{
				progressReporter.ReportMessage("File not found: " + Filename);
				return false;
			}
			getTempFnTerm = Filename;
		}
		try
		{
			RunHashBenches(getTempFnTerm, rashBlockSize, progressReporter);
			return true;
		}
		catch (Exception ex)
		{
			progressReporter.ReportMessage(ex.ToString());
			return false;
		}
		finally
		{
			if (condition)
			{
				progressReporter.ReportMessage("Cleaning up...");
				if (File.Exists(getTempFnTerm))
				{
					File.Delete(getTempFnTerm);
				}
			}
		}
	}

	private void GenerateTempFile(string fn, long actualFs, ProgressReporter pr)
	{
		pr.ReportMessage("Writing to temp file " + fn.InDoubleQuotes());
		FileHelpers.WriteIncompressibleFile(fn, actualFs);
		pr.ReportMessage("File written.");
	}

	private void RunHashBenches(string fn, int rashBlockSize, ProgressReporter pr)
	{
		long length = new FileInfo(fn).Length;
		RunLoop("RashMD5", 5, length, () => FileHelpers.ComputeRashMD5(fn, rashBlockSize), pr);
		RunLoop("RashXX3", 5, length, () => FileHelpers.ComputeRashXX3(fn, rashBlockSize), pr);
		RunLoop("MD5", 5, length, () => FileHelpers.ComputeFileMD5(fn), pr);
		RunLoop("SHA256", 5, length, () => FileHelpers.ComputeFileSHA256(fn), pr);
	}

	private static void RunLoop(string algName, int loopCount, long fileSize, Func<string> hashAction, ProgressReporter pr)
	{
		pr.ReportMessage("Warming up: " + algName);
		hashAction();
		TimeSpan zero = TimeSpan.Zero;
		for (int i = 0; i < loopCount; i++)
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			string hashAction2 = hashAction();
			stopwatch.Stop();
			zero += stopwatch.Elapsed;
			Console.WriteLine(stopwatch.Elapsed.ToString() + " - " + algName + " - " + hashAction2);
		}
		Console.WriteLine(algName + " throughput: " + FileHelpers.PrettyByteCount((long)((double)fileSize / (zero / loopCount).TotalSeconds)) + "/sec");
	}

	private static void PrefixMessage(ProgressReporter pr, bool serial, bool compressible, int nRows, int nCols)
	{
		string serialTerm = (serial ? "SERIAL" : "PARALLEL");
		string compressibleTerm = (compressible ? "COMPRESSIBLE" : "INCOMPRESSIBLE");
		pr.ReportMessage("");
		pr.ReportMessage(serialTerm + " | " + compressibleTerm + " (" + nRows + " x " + nCols + ")");
	}

	private static void WriteStats(long compSz, long uncompSz, TimeSpan elapsed, ProgressReporter pr)
	{
		double compSzOverTotalSeconds = (double)compSz / elapsed.TotalSeconds;
		pr.ReportMessage("Time: " + elapsed.ToPrettyString() + " (" + FileHelpers.PrettyByteCount((long)compSzOverTotalSeconds) + "/s)");
		pr.ReportMessage("Uncompressed Size: " + FileHelpers.PrettyByteCount(uncompSz));
		pr.ReportMessage("Compressed Size: " + FileHelpers.PrettyByteCount(compSz));
		pr.ReportMessage("Compression Ratio: " + ((double)uncompSz / (double)compSz).ToString("F2") + "x");
	}

	private static string GetTempFn()
	{
		return Path.Combine(Path.GetTempPath(), "bench-" + Guid.NewGuid().ToString().Substring(0, 8) + ".bin");
	}
}
