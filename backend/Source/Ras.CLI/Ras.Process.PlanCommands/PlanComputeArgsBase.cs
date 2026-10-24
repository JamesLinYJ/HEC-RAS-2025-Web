using CommandLine;
using Ras.Layers;
using Ras.Process.Commands;
using Utility.Progress;

namespace Ras.Process.PlanCommands;

public abstract class PlanComputeArgsBase
{
	[Option("if-exists", Required = false, HelpText = "What to do when the output file already exists:\n\t'force': delete the existing file and overwrite it.\n\t'append': write to a numbered copy instead (e.g. 'output (3).h5')\n\t'error': report a message and refuse to overwrite.\n\t'default' (default): error when you name the output explicitly, append when it's auto-derived.")]
	public string IfExists { get; set; }

	[Option("core-count", Required = false, HelpText = "Overwrite core-count saved on the plan. 0 := Use plan value, -1 := Use all-available.")]
	public int CoreCount { get; set; }

	[Option("processor", Required = false, HelpText = "Override the plan's hardware target: 'CPU' or 'GPU'. GPU implies SWE + Explicit time-stepping; combining 'gpu' with '--equation dwe' or '--time-stepping implicit' is rejected currently.")]
	public string Processor { get; set; }

	[Option("equation", Required = false, HelpText = "Override the plan's equation set: 'SWE' (Shallow Water Equations) or 'DWE' (Diffusion Wave Equation). DWE only supports implicit time-stepping and CPU currently.")]
	public string EquationSet { get; set; }

	[Option("time-stepping", Required = false, HelpText = "Override the plan's time-stepping scheme: 'Explicit' or 'Implicit'. Only valid for the CPU SWE processor.")]
	public string TimeStepping { get; set; }

	[Option("name", Required = false, HelpText = "Set the plan name embedded in the output result. If not provided, the source plan's name is kept.")]
	public string Name { get; set; }

	protected bool ApplyComputeOverrides(Plan pl, ProgressReporter pr)
	{
		return SolveHelpers.ApplyPlanOverrides(pl, CoreCount, Processor, EquationSet, TimeStepping, pr);
	}
}
