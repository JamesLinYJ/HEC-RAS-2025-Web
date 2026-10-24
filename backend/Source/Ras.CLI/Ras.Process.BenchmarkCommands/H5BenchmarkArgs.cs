using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CommandLine;
using H5Assist;
using H5Assist.Identifiers;
using Utility.Extensions;
using Utility.Files;
using Utility.Progress;

namespace Ras.Process.BenchmarkCommands;

[Verb("h5", false, null, HelpText = "Writes an HDF5 file (serial and parallel) to check performance for this CPU/Disk.")]
public class H5BenchmarkArgs
{
	public const string Verb = "h5";

	[Option("preserve", Required = false, HelpText = "Preserves the written H5 file. Defaults to false.")]
	public bool PreserveFile { get; set; }

	public Task<bool> ExecuteAsync()
	{
		return Task.FromResult(Execute());
	}

	public bool Execute()
	{
		ProgressReporter progressReporter = ProgressReporter.ConsoleWrite();
		PrintMachineStats(progressReporter);
		string tempFn = GetTempFn();
		progressReporter.ReportMessage("Writing to temp file " + tempFn.InDoubleQuotes());
		using (H5Writer hw = new H5Writer(tempFn))
		{
			int ordinal = 10000;
			int nCols = 1000000;
			int nRowsPerChunk = 1;
			TestCompressible_Serial(hw, ordinal / 10, nCols, nRowsPerChunk, progressReporter);
			TestIncompressible_Serial(hw, ordinal / 10, nCols, nRowsPerChunk, progressReporter);
			TestCompressible_Parallel(hw, ordinal, nCols, nRowsPerChunk, progressReporter);
			TestIncompressible_Parallel(hw, ordinal, nCols, nRowsPerChunk, progressReporter);
		}
		if (!PreserveFile)
		{
			progressReporter.ReportMessage("Deleting file...");
			File.Delete(tempFn);
		}
		else
		{
			progressReporter.ReportMessage("File kept: " + tempFn.InDoubleQuotes());
		}
		return true;
	}

	public static void PrintMachineStats(ProgressReporter pr)
	{
		pr.ReportMessage("Machine: " + Environment.MachineName);
		pr.ReportMessage("Core Count: " + Environment.ProcessorCount);
		pr.ReportMessage("Framework: " + RuntimeInformation.FrameworkDescription);
		pr.ReportMessage("OS: " + RuntimeInformation.OSDescription + " (" + RuntimeInformation.OSArchitecture.ToString() + ")");
		pr.ReportMessage("Process: " + RuntimeInformation.ProcessArchitecture);
		pr.ReportMessage("Runtime: " + RuntimeInformation.RuntimeIdentifier);
	}

	private static void PrefixMessage(ProgressReporter pr, bool serial, bool compressible, int nRows, int nCols)
	{
		string serialTerm = (serial ? "SERIAL" : "PARALLEL");
		string compressibleTerm = (compressible ? "COMPRESSIBLE" : "INCOMPRESSIBLE");
		pr.ReportMessage("");
		pr.ReportMessage(serialTerm + " | " + compressibleTerm + " (" + nRows + " x " + nCols + ")");
	}

	private static void TestCompressible_Serial(H5Writer hw, int nRows, int nCols, int nRowsPerChunk, ProgressReporter pr)
	{
		float[] floatValues = new float[nCols];
		GenerateCompressibleData(floatValues);
		PrefixMessage(pr, serial: true, compressible: true, nRows, nCols);
		TestSerial(hw, floatValues, nRows, nCols, nRowsPerChunk, pr);
	}

	private static void TestIncompressible_Serial(H5Writer hw, int nRows, int nCols, int nRowsPerChunk, ProgressReporter pr)
	{
		float[] floatValues = new float[nCols];
		GenerateIncompressibleData(floatValues);
		PrefixMessage(pr, serial: true, compressible: false, nRows, nCols);
		TestSerial(hw, floatValues, nRows, nCols, nRowsPerChunk, pr);
	}

	private static void TestSerial(H5Writer hw, float[] data, int nRows, int nCols, int nRowsPerChunk, ProgressReporter pr)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		string nRowsNColsTerm = "/dset-" + nRows + "-" + nCols + "-" + Guid.NewGuid().ToString().Substring(0, 4);
		using (H5DatasetID dataset = hw.CreateAndOpen2dExtendibleDataset<float>(nRowsNColsTerm, nRowsPerChunk, nCols))
		{
			for (int i = 0; i < nRows; i++)
			{
				hw.AddRow(dataset, data);
				pr.ReportProgressFraction((float)i / (float)nRows);
			}
		}
		stopwatch.Stop();
		WriteStats(hw.CompressedSize(nRowsNColsTerm), (long)nRows * (long)nCols * 4, stopwatch.Elapsed, pr);
	}

	private static void TestCompressible_Parallel(H5Writer hw, int nRows, int nCols, int nRowsPerChunk, ProgressReporter pr)
	{
		float[] floatValues = new float[nCols];
		GenerateCompressibleData(floatValues);
		PrefixMessage(pr, serial: false, compressible: true, nRows, nCols);
		TestParallel(hw, floatValues, nRows, nCols, nRowsPerChunk, pr);
	}

	private static void TestIncompressible_Parallel(H5Writer hw, int nRows, int nCols, int nRowsPerChunk, ProgressReporter pr)
	{
		float[] floatValues = new float[nCols];
		GenerateIncompressibleData(floatValues);
		PrefixMessage(pr, serial: false, compressible: false, nRows, nCols);
		TestParallel(hw, floatValues, nRows, nCols, nRowsPerChunk, pr);
	}

	private static void TestParallel(H5Writer hw, float[] data, int nRows, int nCols, int nRowsPerChunk, ProgressReporter pr)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		string nRowsNColsTerm = "/dset-" + nRows + "-" + nCols + "-" + Guid.NewGuid().ToString().Substring(0, 4);
		using (H5DatasetID dataset = hw.CreateAndOpen2dExtendibleDataset<float>(nRowsNColsTerm, nRowsPerChunk, nCols))
		{
			hw.SetExtent(dataset, nRows, nCols);
			hw.WriteParallelByRow(dataset, (int r) => data, pr);
		}
		stopwatch.Stop();
		WriteStats(hw.CompressedSize(nRowsNColsTerm), (long)nRows * (long)nCols * 4, stopwatch.Elapsed, pr);
	}

	private static void WriteStats(long compSz, long uncompSz, TimeSpan elapsed, ProgressReporter pr)
	{
		double compSzOverTotalSeconds = (double)compSz / elapsed.TotalSeconds;
		double uncompSzOverTotalSeconds = (double)uncompSz / elapsed.TotalSeconds;
		pr.ReportMessage("Time: " + elapsed.ToPrettyString());
		pr.ReportMessage("Speed: " + FileHelpers.PrettyByteCount((long)uncompSzOverTotalSeconds) + "/s Effective  | " + FileHelpers.PrettyByteCount((long)compSzOverTotalSeconds) + "/s Actual");
		pr.ReportMessage("Size: " + FileHelpers.PrettyByteCount(uncompSz) + " Uncompressed | " + FileHelpers.PrettyByteCount(compSz) + " Compressed");
		pr.ReportMessage("Compression Ratio: " + ((double)uncompSz / (double)compSz).ToString("F2") + "x");
	}

	private static void GenerateCompressibleData(Memory<float> buffer)
	{
		Span<float> floatSpan = buffer.Span;
		for (int i = 0; i < floatSpan.Length; i++)
		{
			floatSpan[i] = i % 10;
		}
	}

	private static void GenerateIncompressibleData(Memory<float> buffer)
	{
		Span<float> floatSpan = buffer.Span;
		Random shared = Random.Shared;
		for (int i = 0; i < floatSpan.Length; i++)
		{
			floatSpan[i] = (float)shared.NextDouble();
		}
	}

	private static string GetTempFn()
	{
		return Path.Combine(Path.GetTempPath(), "bench-" + Guid.NewGuid().ToString().Substring(0, 8) + ".h5");
	}
}
