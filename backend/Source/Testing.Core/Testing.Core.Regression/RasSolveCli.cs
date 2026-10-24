using System;
using System.Threading.Tasks;
using Utility.Processes;

namespace Testing.Core.Regression;

public class RasSolveCli
{
	public enum SolveEra
	{
		Unknown,
		Original,
		SolverFlag,
		Positional,
		Subcommands
	}

	private static readonly (string Commit, SolveEra Era)[] EraTable = new (string, SolveEra)[4]
	{
		("6bbdc6b9b0d745720ae3da1117d30140c1d1f4ad", SolveEra.Subcommands),
		("7fddf21f4b464a45d4069a3836953cb4a92f0ec7", SolveEra.Positional),
		("66f05d9ef94817f96aa77392cdd8791c293c6399", SolveEra.SolverFlag),
		("ea6afdc9f5b8ae05bded99f9f1f698bcc4de4c0a", SolveEra.Original)
	};

	public SolveEra Era { get; }

	public RasSolveCli(Func<string, bool> isAncestor)
	{
		Era = SolveEra.Unknown;
		(string, SolveEra)[] eraTable = EraTable;
		for (int i = 0; i < eraTable.Length; i++)
		{
			var (localValue, solveEra) = eraTable[i];
			if (isAncestor(localValue))
			{
				Era = solveEra;
				break;
			}
		}
	}

	public RasSolveCli(SolveEra era)
	{
		Era = era;
	}

	public static async Task<SolveEra> ResolveAsync(Func<string, Task<bool>> isAncestorAsync)
	{
		(string Commit, SolveEra Era)[] eraTable = EraTable;
		for (int i = 0; i < eraTable.Length; i++)
		{
			var (localValue, era) = eraTable[i];
			if (await isAncestorAsync(localValue))
			{
				return era;
			}
		}
		return SolveEra.Unknown;
	}

	public static Func<string, bool> GitAncestryOracle(string repoDir, string runCommit)
	{
		return (string eraCommit) => !string.IsNullOrEmpty(repoDir) && !string.IsNullOrEmpty(runCommit) && RunGit(repoDir, "merge-base --is-ancestor " + eraCommit + " " + runCommit) == 0;
	}

	public static string Prepare(string source, string output)
	{
		return $"prepare -s \"{source}\" -o \"{output}\" --force";
	}

	public static string MeshSqueeze(string source)
	{
		return "mesh -s \"" + source + "\" --squeeze";
	}

	public string SolvePlan(string source, string output, string solver)
	{
		bool notIsNullOrEmpty = !string.IsNullOrEmpty(solver);
		bool notIsNullOrEmpty2 = !string.IsNullOrEmpty(output);
		switch (Era)
		{
		case SolveEra.Subcommands:
		{
			string sourceTerm = "plan solve \"" + source + "\"";
			if (notIsNullOrEmpty2)
			{
				sourceTerm = sourceTerm + " \"" + output + "\"";
			}
			sourceTerm += " --if-exists force";
			if (notIsNullOrEmpty)
			{
				sourceTerm = sourceTerm + " --processor " + solver;
			}
			return sourceTerm;
		}
		case SolveEra.Positional:
		{
			string sourceTerm2 = "solve \"" + source + "\"";
			if (notIsNullOrEmpty2)
			{
				sourceTerm2 = sourceTerm2 + " \"" + output + "\"";
			}
			sourceTerm2 += " --force";
			if (notIsNullOrEmpty)
			{
				sourceTerm2 = sourceTerm2 + " --solver " + solver;
			}
			return sourceTerm2;
		}
		case SolveEra.SolverFlag:
		{
			string sourceTerm3 = "solve -s \"" + source + "\"";
			if (notIsNullOrEmpty2)
			{
				sourceTerm3 = sourceTerm3 + " -o \"" + output + "\"";
			}
			sourceTerm3 += " --force";
			if (notIsNullOrEmpty)
			{
				sourceTerm3 = sourceTerm3 + " --solver " + solver;
			}
			return sourceTerm3;
		}
		case SolveEra.Original:
		{
			string outputTerm = "solve -s \"" + source + "\"";
			if (notIsNullOrEmpty2)
			{
				outputTerm = outputTerm + " -o \"" + output + "\"";
			}
			return outputTerm + " --force";
		}
		default:
			return null;
		}
	}

	private static int RunGit(string repoDir, string args)
	{
		try
		{
			return ProcessHelper.RunAndCapture("git", "-C \"" + repoDir + "\" " + args).ExitCode;
		}
		catch
		{
			return -1;
		}
	}
}
