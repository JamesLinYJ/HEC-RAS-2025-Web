using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Utility;
using Utility.Extensions;
using Utility.Progress;

namespace Ras.Engine.Native;

public static class GPUSolver
{
	public static readonly string CudaSolverLibName = "Ras.Engine.Interop.dll";

	public static bool DevMode = false;

	public static readonly string CudaSolverDevPath = "Ras.Engine.Interop\\bin\\Release\\net10.0\\";

	public const string CudaSolverFullyQualifiedName = "Ras.Engine.Interop.CudaSolver";

	private static Type _solverType;

	private static readonly int _solutionSearchDepth = 8;

	public static bool IsInitialized { get; private set; }

	public static bool IsSupported()
	{
		if (!IsInitialized)
		{
			TryInitialize(DevMode);
		}
		return _solverType != null;
	}

	public static string[] GetDevWorkingDirectories()
	{
		return new string[1] { "C:\\Programs\\2025" };
	}

	public static string GetDevSolutionDirectory()
	{
		string directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
		for (int i = 0; i < _solutionSearchDepth; i++)
		{
			string[] files = Directory.GetFiles(directoryName);
			bool condition = false;
			string[] filesArray = files;
			foreach (string x in filesArray)
			{
				if (x.RoughlyEndsWith(".sln") || x.RoughlyEndsWith(".slnx"))
				{
					condition = true;
					break;
				}
			}
			if (condition)
			{
				break;
			}
			DirectoryInfo parent = Directory.GetParent(directoryName);
			if (parent == null)
			{
				return "";
			}
			directoryName = parent.FullName;
		}
		return directoryName;
	}

	public static void InitDevASM()
	{
		IsInitialized = true;
		string path = Path.Combine(GetDevSolutionDirectory(), CudaSolverDevPath);
		string path2 = Path.Combine(path, CudaSolverLibName);
		if (File.Exists(path2))
		{
			OS.PrependToPathIfDNE(path);
			_solverType = AssemblyLoadContext.Default.LoadFromAssemblyPath(path2).GetType("Ras.Engine.Interop.CudaSolver");
		}
	}

	private static void TryInitialize(bool checkDevPaths = false, ProgressReporter pr = null)
	{
		if (!IsInitialized)
		{
			if (pr == null)
			{
				pr = new ProgressReporter();
			}
			IsInitialized = true;
			if (TryFindExpectedAsm(checkDevPaths, pr, out var fullyQualifiedAssembly))
			{
				_solverType = AssemblyLoadContext.Default.LoadFromAssemblyPath(fullyQualifiedAssembly).GetType("Ras.Engine.Interop.CudaSolver");
			}
		}
	}

	private static bool TryFindExpectedAsm(bool checkDevPaths, ProgressReporter pr, out string fullyQualifiedAssembly)
	{
		string cudaSolverLibName = CudaSolverLibName;
		string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), cudaSolverLibName);
		if (File.Exists(path))
		{
			fullyQualifiedAssembly = path;
			pr.ReportMessage("CUDA assembly found in expected location. (" + fullyQualifiedAssembly + ")");
			return true;
		}
		pr.ReportMessage("CUDA assembly not found in expected location. (" + path + ")");
		if (!checkDevPaths)
		{
			fullyQualifiedAssembly = "";
			return false;
		}
		string path2 = Path.Combine(GetDevSolutionDirectory(), CudaSolverDevPath);
		path = Path.Combine(path2, cudaSolverLibName);
		if (File.Exists(path))
		{
			OS.PrependToPathIfDNE(path2);
			fullyQualifiedAssembly = path;
			pr.ReportMessage("CUDA assembly found in dev location. (" + fullyQualifiedAssembly + ")");
			return true;
		}
		pr.ReportMessage("CUDA assembly not found in dev location. (" + path + ")");
		string[] devWorkingDirectories = GetDevWorkingDirectories();
		for (int i = 0; i < devWorkingDirectories.Length; i++)
		{
			path = Path.Combine(devWorkingDirectories[i], cudaSolverLibName);
			if (File.Exists(path))
			{
				OS.PrependToPathIfDNE(path2);
				fullyQualifiedAssembly = path;
				pr.ReportMessage("CUDA assembly found in dev working directory. (" + fullyQualifiedAssembly + ")");
				return true;
			}
			pr.ReportMessage("CUDA assembly not found: (" + path + ")");
		}
		pr.ReportMessage("CUDA assembly not found in any of the expected locations, failed to initialize.");
		fullyQualifiedAssembly = "";
		return false;
	}

	public static IExplicitSolver GetSolver()
	{
		if (!IsSupported())
		{
			throw new Exception("GPU Solver not supported.");
		}
		return Activator.CreateInstance(_solverType) as IExplicitSolver;
	}

	public static string TryFindCompatibleGPU()
	{
		try
		{
			if (!IsInitialized)
			{
				TryInitialize(checkDevPaths: true);
			}
			if (_solverType == null)
			{
				throw new Exception("GPU Solver requested, but could not be initialized.");
			}
			return ((string)_solverType.GetMethod("TryFindCompatibleGPU")?.Invoke(null, null)) ?? "";
		}
		catch
		{
			return "";
		}
	}
}
