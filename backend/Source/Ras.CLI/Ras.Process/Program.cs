using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommandLine;
using Ras.Process.BenchmarkCommands;
using Ras.Process.BoundaryConditionCommands;
using Ras.Process.Commands;
using Ras.Process.DevCommands;
using Ras.Process.GeometryCommands;
using Ras.Process.H5Commands;
using Ras.Process.HelpersCommands;
using Ras.Process.LibraryCommands;
using Ras.Process.PlanCommands;
using Ras.Process.ProjectCommands;
using Ras.Process.RasterCommands;
using Ras.Process.ResultCommands;
using Ras.Process.S3Commands;
using Ras.Process.SampleCommands;
using Ras.Process.TerrainCommands;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Ras.Runtime;
using Utility.CommandLine;
using Utility.Extensions;
using Utility.Logging;

namespace Ras.Process;

public class Program
{
	private static readonly Parser _parser = new Parser((ParserSettings s) =>
	{
		s.HelpWriter = Console.Error;
		s.CaseInsensitiveEnumValues = true;
	});

	public const string ShowHiddenEnvVar = "RAS_DEV";

	private static bool ShowHiddenVerbs
	{
		get
		{
			if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RAS_DEV")))
			{
				return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RAS_S3_USER"));
			}
			return true;
		}
	}

	public static async Task<int> Main(string[] args)
	{
		string logDirectory = Environment.GetEnvironmentVariable("RAS_CLI_LOG_DIRECTORY");
		if (string.IsNullOrWhiteSpace(logDirectory))
		{
			logDirectory = LogHelper.DefaultLogLocation();
		}
		Directory.CreateDirectory(logDirectory);
		string path = Path.Combine(logDirectory, "ras-7-process.log");
		Logger logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.File(path, LogEventLevel.Verbose, "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}", null, 1073741824L, null, buffered: false, shared: false, null, RollingInterval.Infinite, rollOnFileSizeLimit: false, 31).CreateLogger();
		LogHelper.LoggerFactory.AddSerilog(logger);
		if (args.Length == 1 && args[0].Contains('?'))
		{
			args[0] = "--help";
		}
		if (args.Length == 0)
		{
			return ExitCode(new ShowGUI().Execute());
		}
		if (ShowGUI.LooksLikeProjectFile(args[0]))
		{
			ShowGUI? launch = ParseProjectFirstLaunchArguments(args);
			return ExitCode(launch?.Execute() ?? false);
		}
		string argsTerm = args[0];
		string[] args2 = args.Skip(1).ToArray();
		if (ShouldConfigureRuntime(args, argsTerm, args2))
		{
			RasRuntimeBootstrap.Configure();
		}
		if (argsTerm == "dev")
		{
			return ExitCode(await RunDev(args2));
		}
		if (argsTerm == "library")
		{
			return ExitCode(await RunLibrary(args2));
		}
		if (argsTerm.RoughlyEquals("h5"))
		{
			return ExitCode(RunH5(args2));
		}
		if (argsTerm.RoughlyEquals("benchmark"))
		{
			return ExitCode(RunBenchmark(args2));
		}
		if (argsTerm.RoughlyEquals("raster"))
		{
			return ExitCode(RunRaster(args2));
		}
		if (argsTerm.RoughlyEquals("terrain"))
		{
			return ExitCode(RunTerrain(args2));
		}
		if (argsTerm.RoughlyEquals("project"))
		{
			return ExitCode(RunProject(args2));
		}
		if (argsTerm.RoughlyEquals("geometry"))
		{
			return ExitCode(RunGeometry(args2));
		}
		if (argsTerm.RoughlyEquals("boundarycondition") || argsTerm.RoughlyEquals("bc"))
		{
			return ExitCode(RunBoundaryCondition(args2));
		}
		if (argsTerm.RoughlyEquals("plan"))
		{
			return ExitCode(RunPlan(args2));
		}
		if (argsTerm.RoughlyEquals("result"))
		{
			return ExitCode(RunResult(args2));
		}
		if (argsTerm.RoughlyEquals("sample"))
		{
			return ExitCode(await RunSample(args2));
		}
		if (argsTerm.RoughlyEquals("s3"))
		{
			return ExitCode(await RunS3(args2));
		}
		if (argsTerm.RoughlyEquals("helpers"))
		{
			return ExitCode(RunHelpers(args2));
		}
		return ExitCode(RunTopLevel(args));
	}

	internal static ShowGUI? ParseProjectFirstLaunchArguments(string[] args)
	{
		if (args.Length == 0) return null;
		ShowGUI launch = new ShowGUI { ProjectFile = args[0] };
		for (int index = 1; index < args.Length; index++)
		{
			string argument = args[index];
			if (argument.StartsWith("-stream=", StringComparison.OrdinalIgnoreCase))
			{
				launch.StreamPipeName = argument[8..];
				continue;
			}
			if (string.Equals(argument, "-stream", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(argument, "--stream", StringComparison.OrdinalIgnoreCase))
			{
				if (++index >= args.Length)
				{
					Console.Error.WriteLine("Missing value for -stream.");
					return null;
				}
				launch.StreamPipeName = args[index];
				continue;
			}
			if (string.Equals(argument, "-safemode", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(argument, "--safemode", StringComparison.OrdinalIgnoreCase))
			{
				launch.SafeMode = true;
				continue;
			}
			if (string.Equals(argument, "-crashed", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(argument, "--crashed", StringComparison.OrdinalIgnoreCase))
			{
				launch.PostCrash = true;
				continue;
			}
			if (string.Equals(argument, "-debug", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(argument, "--debug", StringComparison.OrdinalIgnoreCase))
			{
				launch.DebugMode = true;
				continue;
			}
			Console.Error.WriteLine($"Unknown desktop launch option '{argument}'.");
			return null;
		}
		return launch;
	}

	private static bool IsHelpOrVersionRequest(string[] args)
	{
		return args.Any(argument => argument is "-h" or "--help" or "-?" or "--version" or "version");
	}

	private static bool ShouldConfigureRuntime(string[] args, string argsTerm, string[] subcommandArgs)
	{
		if (IsHelpOrVersionRequest(args) || ShowGUI.IsLaunchVerb(argsTerm)) return false;
		if (!OperatingSystem.IsWindows()
			&& string.Equals(argsTerm, "dev", StringComparison.OrdinalIgnoreCase)
			&& subcommandArgs.Length > 0
			&& (string.Equals(subcommandArgs[0], "update", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(subcommandArgs[0], "checkupdate", StringComparison.OrdinalIgnoreCase)))
		{
			return false;
		}
		return true;
	}

	private static int ExitCode(bool success)
	{
		if (!success)
		{
			return -1;
		}
		return 0;
	}

	private static Task<bool> RunDev(string[] args)
	{
		return _parser.ParseArguments<Push, Pull, Test, VerifyManifest, UpdateManifest, Release, PushProject, PullProject, Delist, DelistProject, CheckUpdate, Update, CheckMeshArgs, TiffArgs, ExtractTerrainArgs>(args).MapResult((Push opts) => opts.ExecuteAsync(), (Pull opts) => opts.ExecuteAsync(), (Test opts) => opts.ExecuteAsync(), (VerifyManifest opts) => opts.ExecuteAsync(), (UpdateManifest opts) => opts.ExecuteAsync(), (Release opts) => opts.ExecuteAsync(), (PushProject opts) => opts.ExecuteAsync(), (PullProject opts) => opts.ExecuteAsync(), (Delist opts) => opts.ExecuteAsync(), (DelistProject opts) => opts.ExecuteAsync(), (CheckUpdate opts) => opts.ExecuteAsync(), (Update opts) => opts.ExecuteAsync(), (CheckMeshArgs opts) => opts.ExecuteAsync(), (TiffArgs opts) => opts.ExecuteAsync(), (ExtractTerrainArgs opts) => opts.ExecuteAsync(), (IEnumerable<Error> errs) => Task.FromResult(result: false));
	}

	private static bool RunH5(string[] args)
	{
		return _parser.ParseArguments<DiffArgs, SqueezeArgs, ExploreArgs, CreatePatchArgs, RestorePatchArgs>(args).MapResult((DiffArgs opts) => opts.Execute(), (SqueezeArgs opts) => opts.Execute(), (ExploreArgs opts) => opts.Execute(), (CreatePatchArgs opts) => opts.Execute(), (RestorePatchArgs opts) => opts.Execute(), (IEnumerable<Error> errs) => false);
	}

	private static bool RunBenchmark(string[] args)
	{
		return _parser.ParseArguments<H5BenchmarkArgs, HashBenchmarkArgs, BenchmarkSolverArgs>(args).MapResult((H5BenchmarkArgs opts) => opts.Execute(), (HashBenchmarkArgs opts) => opts.Execute(), (BenchmarkSolverArgs opts) => TryCatchPrint(() => opts.Execute()), (IEnumerable<Error> errs) => false);
	}

	private static bool RunRaster(string[] args)
	{
		return _parser.ParseArguments<TranslateArgs, CalcArgs, RasterExploreArgs, RasterInfoArgs, RasterRenderArgs>(args).MapResult((TranslateArgs opts) => opts.Execute(), (CalcArgs opts) => opts.Execute(), (RasterExploreArgs opts) => opts.Execute(), (RasterInfoArgs opts) => opts.Execute(), (RasterRenderArgs opts) => TryCatchPrint(() => opts.Execute()), (IEnumerable<Error> errs) => false);
	}

	private static bool RunTerrain(string[] args)
	{
		return _parser.ParseArguments<TerrainCreateArgs, TerrainAddArgs, TerrainInfoArgs, TerrainRemoveArgs, TerrainExportArgs>(args).MapResult((TerrainCreateArgs opts) => TryCatchPrint(() => opts.Execute()), (TerrainAddArgs opts) => TryCatchPrint(() => opts.Execute()), (TerrainInfoArgs opts) => TryCatchPrint(() => opts.Execute()), (TerrainRemoveArgs opts) => TryCatchPrint(() => opts.Execute()), (TerrainExportArgs opts) => TryCatchPrint(() => opts.Execute()), (IEnumerable<Error> errs) => false);
	}

	private static bool RunProject(string[] args)
	{
		return _parser.ParseArguments<ProjectCreateArgs, ProjectInfoArgs, ProjectAddArgs, ProjectAddNewArgs, ProjectRemoveArgs, ProjectListGeometryArgs, ProjectListPlanArgs, ProjectListBoundaryConditionArgs, ProjectListTerrainArgs, ProjectListResultArgs, ProjectListNValueArgs, ProjectListClassificationArgs, ProjectSolveArgs, ProjectPrepareArgs, ProjectCleanupArgs, ProjectExploreArgs>(args).MapResult((ProjectCreateArgs opts) => TryCatchPrint(() => opts.Execute()), (ProjectInfoArgs opts) => TryCatchPrint(() => opts.Execute()), (ProjectAddArgs opts) => TryCatchPrint(() => opts.Execute()), (ProjectAddNewArgs opts) => TryCatchPrint(() => opts.Execute()), (ProjectRemoveArgs opts) => TryCatchPrint(() => opts.Execute()), (ProjectListGeometryArgs opts) => TryCatchPrint(() => opts.Execute()), (ProjectListPlanArgs opts) => TryCatchPrint(() => opts.Execute()), (ProjectListBoundaryConditionArgs opts) => TryCatchPrint(() => opts.Execute()), (ProjectListTerrainArgs opts) => TryCatchPrint(() => opts.Execute()), (ProjectListResultArgs opts) => TryCatchPrint(() => opts.Execute()), (ProjectListNValueArgs opts) => TryCatchPrint(() => opts.Execute()), (ProjectListClassificationArgs opts) => TryCatchPrint(() => opts.Execute()), (ProjectSolveArgs opts) => TryCatchPrint(() => opts.Execute()), (ProjectPrepareArgs opts) => TryCatchPrint(() => opts.Execute()), (ProjectCleanupArgs opts) => TryCatchPrint(() => opts.Execute()), (ProjectExploreArgs opts) => TryCatchPrint(() => opts.Execute()), (IEnumerable<Error> errs) => false);
	}

	private static bool RunGeometry(string[] args)
	{
		return _parser.ParseArguments<GeometryInfoArgs, GeometryModifyArgs, GeometryGenerateMeshArgs>(args).MapResult((GeometryInfoArgs opts) => TryCatchPrint(() => opts.Execute()), (GeometryModifyArgs opts) => TryCatchPrint(() => opts.Execute()), (GeometryGenerateMeshArgs opts) => TryCatchPrint(() => opts.Execute()), (IEnumerable<Error> errs) => false);
	}

	private static bool RunBoundaryCondition(string[] args)
	{
		return _parser.ParseArguments<BoundaryConditionInfoArgs, Dummy>(args).MapResult((BoundaryConditionInfoArgs opts) => TryCatchPrint(() => opts.Execute()), (Dummy _) => false, (IEnumerable<Error> errs) => false);
	}

	private static bool RunPlan(string[] args)
	{
		return _parser.ParseArguments<PlanInfoArgs, PlanModifyArgs, PlanSolveArgs, PlanPrepareArgs>(args).MapResult((PlanInfoArgs opts) => TryCatchPrint(() => opts.Execute()), (PlanModifyArgs opts) => TryCatchPrint(() => opts.Execute()), (PlanSolveArgs opts) => TryCatchPrint(() => opts.Execute()), (PlanPrepareArgs opts) => TryCatchPrint(() => opts.Execute()), (IEnumerable<Error> errs) => false);
	}

	private static bool RunResult(string[] args)
	{
		return _parser.ParseArguments<ResultInfoArgs, ResultMapArgs, ResultPostProcessArgs>(args).MapResult((ResultInfoArgs opts) => TryCatchPrint(() => opts.Execute()), (ResultMapArgs opts) => TryCatchPrint(() => opts.Execute()), (ResultPostProcessArgs opts) => TryCatchPrint(() => opts.Execute()), (IEnumerable<Error> errs) => false);
	}

	private static Task<bool> RunSample(string[] args)
	{
		return _parser.ParseArguments<SampleListArgs, SampleInfoArgs, SamplePullArgs, SamplePushArgs>(args).MapResult((SampleListArgs opts) => TryCatchPrintAsync(() => opts.ExecuteAsync()), (SampleInfoArgs opts) => TryCatchPrintAsync(() => opts.ExecuteAsync()), (SamplePullArgs opts) => TryCatchPrintAsync(() => opts.ExecuteAsync()), (SamplePushArgs opts) => TryCatchPrintAsync(() => opts.ExecuteAsync()), (IEnumerable<Error> errs) => Task.FromResult(result: false));
	}

	private static Task<bool> RunLibrary(string[] args)
	{
		return _parser.ParseArguments<LibraryListArgs, LibraryInfoArgs, LibraryPullArgs, LibraryPushArgs>(args).MapResult((LibraryListArgs opts) => TryCatchPrintAsync(() => opts.ExecuteAsync()), (LibraryInfoArgs opts) => TryCatchPrintAsync(() => opts.ExecuteAsync()), (LibraryPullArgs opts) => TryCatchPrintAsync(() => opts.ExecuteAsync()), (LibraryPushArgs opts) => TryCatchPrintAsync(() => opts.ExecuteAsync()), (IEnumerable<Error> errs) => Task.FromResult(result: false));
	}

	private static bool RunHelpers(string[] args)
	{
		return _parser.ParseArguments<HelpersZipArgs, HelpersExtractArgs, HelpersHashArgs>(args).MapResult((HelpersZipArgs opts) => TryCatchPrint(() => opts.Execute()), (HelpersExtractArgs opts) => TryCatchPrint(() => opts.Execute()), (HelpersHashArgs opts) => TryCatchPrint(() => opts.Execute()), (IEnumerable<Error> errs) => false);
	}

	private static Task<bool> RunS3(string[] args)
	{
		return _parser.ParseArguments<S3ListBucketsArgs, S3ListBucketArgs, S3PresignArgs, S3PullArgs, S3PushArgs>(args).MapResult((S3ListBucketsArgs opts) => TryCatchPrintAsync(() => opts.ExecuteAsync()), (S3ListBucketArgs opts) => TryCatchPrintAsync(() => opts.ExecuteAsync()), (S3PresignArgs opts) => TryCatchPrintAsync(() => opts.ExecuteAsync()), (S3PullArgs opts) => TryCatchPrintAsync(() => opts.ExecuteAsync()), (S3PushArgs opts) => TryCatchPrintAsync(() => opts.ExecuteAsync()), (IEnumerable<Error> errs) => Task.FromResult(result: false));
	}

	private static bool RunTopLevel(string[] args)
	{
		List<Type> typeValues = new List<Type>
		{
			typeof(BenchmarkArgs),
			typeof(BoundaryConditionArgs),
			typeof(GeometryArgs),
			typeof(H5Args),
			typeof(HelpersArgs),
			typeof(PlanArgs),
			typeof(ProjectArgs),
			typeof(RasterArgs),
			typeof(ResultArgs),
			typeof(S3Args),
			typeof(SampleArgs),
			typeof(TerrainArgs),
			typeof(CloneArgs),
			typeof(HealthCheckArgs),
			typeof(InfoArgs),
			typeof(MeshArgs),
			typeof(PrepareArgs),
			typeof(ShowGUI),
			typeof(SolveArgs)
		};
		if (ShowHiddenVerbs)
		{
			typeValues.Add(typeof(DevArgs));
			typeValues.Add(typeof(LibraryArgs));
		}
		return _parser.ParseArguments(args, typeValues.ToArray()).MapResult((object opts) =>
		{
			BenchmarkArgs benchmarkArgs = opts as BenchmarkArgs;
			if (benchmarkArgs != null)
			{
				return TryCatchPrint(() => benchmarkArgs.Execute());
			}
			BoundaryConditionArgs boundaryConditionArgs = opts as BoundaryConditionArgs;
			if (boundaryConditionArgs != null)
			{
				return TryCatchPrint(() => boundaryConditionArgs.Execute());
			}
			GeometryArgs geometryArgs = opts as GeometryArgs;
			if (geometryArgs != null)
			{
				return TryCatchPrint(() => geometryArgs.Execute());
			}
			H5Args h5Args = opts as H5Args;
			if (h5Args != null)
			{
				return TryCatchPrint(() => h5Args.Execute());
			}
			HelpersArgs helpersArgs = opts as HelpersArgs;
			if (helpersArgs != null)
			{
				return TryCatchPrint(() => helpersArgs.Execute());
			}
			PlanArgs planArgs = opts as PlanArgs;
			if (planArgs != null)
			{
				return TryCatchPrint(() => planArgs.Execute());
			}
			ProjectArgs projectArgs = opts as ProjectArgs;
			if (projectArgs != null)
			{
				return TryCatchPrint(() => projectArgs.Execute());
			}
			RasterArgs rasterArgs = opts as RasterArgs;
			if (rasterArgs != null)
			{
				return TryCatchPrint(() => rasterArgs.Execute());
			}
			ResultArgs resultArgs = opts as ResultArgs;
			if (resultArgs != null)
			{
				return TryCatchPrint(() => resultArgs.Execute());
			}
			S3Args s3Args = opts as S3Args;
			if (s3Args != null)
			{
				return TryCatchPrint(() => s3Args.Execute());
			}
			SampleArgs sampleArgs = opts as SampleArgs;
			if (sampleArgs != null)
			{
				return TryCatchPrint(() => sampleArgs.Execute());
			}
			TerrainArgs terrainArgs = opts as TerrainArgs;
			if (terrainArgs != null)
			{
				return TryCatchPrint(() => terrainArgs.Execute());
			}
			DevArgs devArgs = opts as DevArgs;
			if (devArgs != null)
			{
				return TryCatchPrint(() => devArgs.Execute());
			}
			LibraryArgs libraryArgs = opts as LibraryArgs;
			if (libraryArgs != null)
			{
				return TryCatchPrint(() => libraryArgs.Execute());
			}
			CloneArgs cloneArgs = opts as CloneArgs;
			if (cloneArgs != null)
			{
				return TryCatchPrint(() => cloneArgs.Execute());
			}
			HealthCheckArgs healthCheckArgs = opts as HealthCheckArgs;
			if (healthCheckArgs != null)
			{
				return TryCatchPrint(() => healthCheckArgs.Execute());
			}
			InfoArgs infoArgs = opts as InfoArgs;
			if (infoArgs != null)
			{
				return TryCatchPrint(() => infoArgs.Execute());
			}
			MeshArgs meshArgs = opts as MeshArgs;
			if (meshArgs != null)
			{
				return TryCatchPrint(() => meshArgs.Execute());
			}
			PrepareArgs prepareArgs = opts as PrepareArgs;
			if (prepareArgs != null)
			{
				return TryCatchPrint(() => prepareArgs.Execute());
			}
			ShowGUI showGUI = opts as ShowGUI;
			if (showGUI != null)
			{
				return TryCatchPrint(() => showGUI.Execute());
			}
			SolveArgs solveArgs = opts as SolveArgs;
			return solveArgs != null && TryCatchPrint(() => solveArgs.Execute());
		}, (IEnumerable<Error> errs) => CheckErrors(errs));
	}

	private static bool CheckErrors(IEnumerable<Error> errs)
	{
		foreach (Error err in errs)
		{
			if (err.Tag != ErrorType.VersionRequestedError && err.Tag != ErrorType.HelpVerbRequestedError && err.Tag != ErrorType.HelpRequestedError)
			{
				return false;
			}
		}
		return true;
	}

	public static async Task<int> CallMainAsync(string args)
	{
		return await Main(CommandLineHelpers.SplitIntoCLIArgs(args));
	}

	private static bool TryCatchPrint(Func<bool> act)
	{
		try
		{
			return act();
		}
		catch (Exception ex)
		{
			Console.WriteLine(ex.Message);
			Console.WriteLine(ex.StackTrace);
			return false;
		}
	}

	private static async Task<bool> TryCatchPrintAsync(Func<Task<bool>> act)
	{
		try
		{
			return await act();
		}
		catch (Exception ex)
		{
			Console.WriteLine(ex.Message);
			Console.WriteLine(ex.StackTrace);
			return false;
		}
	}
}
