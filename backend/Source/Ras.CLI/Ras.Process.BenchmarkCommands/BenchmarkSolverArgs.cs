using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommandLine;
using Geospatial.GDALAssist;
using Ras.Layers;
using Ras.Process.Helpers;
using Ras.Synthetics;
using Utility.Extensions;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Process.BenchmarkCommands;

[Verb("solver", false, null, HelpText = "Creates the synthetic Thacker 2D dataset and solves it across multiple core counts to measure CPU solver scaling on this machine.")]
public class BenchmarkSolverArgs
{
	public const string Verb = "solver";

	private const double DefaultEndTimeSeconds = 20000.0;

	[Option("end-time", Required = false, HelpText = "Simulation end-time in seconds. Lower this to shorten each solve. Defaults to 20000 (~20 timesteps).")]
	public double EndTimeSeconds { get; set; } = 20000.0;

	[Option("cores", Required = false, HelpText = "Comma-separated list of core counts to benchmark, in order. Defaults to '1,4,16'.")]
	public string CoreList { get; set; } = "1,4,16";

	public bool Execute()
	{
		ProgressReporter progressReporter = ProgressReporter.ConsoleWrite();
		try
		{
			if (!TryParseCores(CoreList, progressReporter, out var cores))
			{
				return false;
			}
			if (EndTimeSeconds <= 0.0)
			{
				progressReporter.ReportError("--end-time must be positive.");
				return false;
			}
			CliHelpers.TimeStep("Initializing GDAL...", () =>
			{
				GDALSetup.InitializeMultiplatform();
			});
			Thacker2DParams thacker2DParams = Thacker2DParams.Default();
			thacker2DParams.ReIndexMesh = false;
			string path = Path.Combine(Path.GetTempPath(), "ras-thacker-bench-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			Project project = null;
			try
			{
				Console.WriteLine("Creating + saving synthetic Thacker dataset...");
				project = SyntheticTestCases.Save(thacker2DParams, Path.Combine(path, "project"), 1048576);
				Plan plan = project.Plans.First();
				plan.InternalComputeDurationSeconds = EndTimeSeconds;
				Console.WriteLine("Preparing dataset (property tables, initial conditions)...");
				string path2 = Path.Combine(path, "thacker.r2r.h5");
				OperationResult operationResult = plan.TryPrepareCompute(path2, out var readyToRun, progressReporter);
				if (!operationResult.Result)
				{
					progressReporter.ReportError("Failed to prepare Thacker dataset: " + operationResult.GetConcatenatedMessages("\n"));
					return false;
				}
				int cellCount = readyToRun.Geometry.FlowAreaLayer.First().Mesh.CellCount;
				readyToRun.Dispose();
				int endTimeSecondsComputeBaseTimestepSecondsTerm = (int)Math.Round(EndTimeSeconds / plan.ComputeBaseTimestepSeconds);
				Console.WriteLine($"Mesh: {cellCount:N0} cells | dt: {plan.ComputeBaseTimestepSeconds:0.##}s | end-time: {EndTimeSeconds:N0}s (~{endTimeSecondsComputeBaseTimestepSecondsTerm:N0} timesteps)");
				Console.WriteLine();
				Console.WriteLine($"{"Cores",6}  {"Wall (s)",10}  {"Cell-steps/s",16}  {"Speedup",8}");
				double totalSeconds2 = 0.0;
				for (int intervalIndex = 0; intervalIndex < cores.Length; intervalIndex++)
				{
					int core = cores[intervalIndex];
					string path3 = Path.Combine(path, $"run-{core}c.res.h5");
					File.Copy(path2, path3, overwrite: true);
					double totalSeconds;
					using (Result result = new Result(path3))
					{
						result.Plan.CoreCount = core;
						Stopwatch stopwatch = Stopwatch.StartNew();
						OperationResult operationResult2 = result.Compute((Action a) =>
						{
							a();
						});
						stopwatch.Stop();
						if (!operationResult2.Result)
						{
							progressReporter.ReportError($"Solve failed at {core} core(s): " + operationResult2.GetConcatenatedMessages("\n"));
							return false;
						}
						totalSeconds = stopwatch.Elapsed.TotalSeconds;
					}
					if (intervalIndex == 0)
					{
						totalSeconds2 = totalSeconds;
					}
					double totalSecondsCellCountTerm = ((totalSeconds > 0.0) ? ((double)cellCount * (double)endTimeSecondsComputeBaseTimestepSecondsTerm / totalSeconds) : 0.0);
					string totalSeconds2TotalSecondsTerm = ((totalSeconds2 > 0.0) ? ((totalSeconds2 / totalSeconds).ToString("0.00") + "x") : "-");
					Console.WriteLine($"{core,6}  {totalSeconds,10:0.00}  {totalSecondsCellCountTerm,16:N0}  {totalSeconds2TotalSecondsTerm,8}");
				}
				return true;
			}
			finally
			{
				DisposeProjectLayers(project);
				TryDeleteDirectory(path);
			}
		}
		catch (Exception ex)
		{
			progressReporter.ReportError("Error: " + ex.Message);
			return false;
		}
	}

	private static void DisposeProjectLayers(Project prj)
	{
		if (prj != null)
		{
			DisposeAll(prj.Geometries);
			DisposeAll(prj.BoundaryConditions);
			DisposeAll(prj.Plans);
			DisposeAll(prj.Terrains);
			DisposeAll(prj.NValues);
			DisposeAll(prj.ClassificationLayers);
		}
	}

	private static void DisposeAll<T>(IEnumerable<T> items) where T : IDisposable
	{
		if (items == null)
		{
			return;
		}
		foreach (T localValue in items)
		{
			try
			{
				localValue?.Dispose();
			}
			catch
			{
			}
		}
	}

	private static void TryDeleteDirectory(string dir)
	{
		try
		{
			if (Directory.Exists(dir))
			{
				GC.Collect();
				GC.WaitForPendingFinalizers();
				Directory.Delete(dir, recursive: true);
			}
		}
		catch
		{
		}
	}

	private static bool TryParseCores(string raw, ProgressReporter pr, out int[] cores)
	{
		cores = null;
		List<int> intList = new List<int>();
		string[] parts = (raw ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (string part in parts)
		{
			if (!int.TryParse(part, out var result) || result < 1)
			{
				pr.ReportError("Invalid core count: " + part.InDoubleQuotes() + ". Provide positive integers, e.g. '1,4,16'.");
				return false;
			}
			intList.Add(result);
		}
		if (intList.Count == 0)
		{
			pr.ReportError("No core counts provided.");
			return false;
		}
		cores = intList.ToArray();
		return true;
	}
}
