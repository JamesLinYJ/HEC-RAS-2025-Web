using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommandLine;
using CommandLine.Text;
using Geospatial.GDALAssist;
using Ras.Layers;
using Ras.Process.Helpers;
using Utility.Filtering;

namespace Ras.Process.PlanCommands;

[Verb("modify", false, null, HelpText = "Opens a RAS plan file (*.h5), changes one or more settings, and saves it in place.")]
public class PlanModifyArgs
{
	public const string Verb = "modify";

	[Value(0, MetaName = "plan", Required = false, HelpText = "Plan file (*.h5). Optional with --grammar (omit it to print the default schema).")]
	public string PlanFilename { get; set; }

	[Value(1, MetaName = "set", Required = false, HelpText = "One or more assignments, each \"<field> = value\" (e.g. \"corecount = 8\"). The field names come from the plan's settings - run --grammar to list them. Enum and bool values are case-insensitive (\"solvertype = gpu\", \"usefbf = true\"). Not required with --grammar.")]
	public IEnumerable<string> Sets { get; set; }

	[Option("grammar", Required = false, HelpText = "List every field you can target in this plan (no changes made), then exit.")]
	public bool Grammar { get; set; }

	[Option("case-sensitive", Required = false, HelpText = "Make string comparisons in assignment values case-sensitive.")]
	public bool CaseSensitive { get; set; }

	[Option("dry-run", Required = false, HelpText = "Report what would change without writing the file.")]
	public bool DryRun { get; set; }

	[Option("format", Required = false, HelpText = "Output format for the change report: Text (default), Json, or Properties (grep-friendly key=value). Errors always go to stderr as text, so stdout stays parseable.")]
	public OutputFormat OutputFormat { get; set; }

	[Usage(ApplicationAlias = "ras plan")]
	public static IEnumerable<Example> Examples => new Example[8]
	{
		new Example("List the fields you can target/set (no file needed)", new PlanModifyArgs
		{
			Grammar = true
		}),
		new Example("List the fields and their current values in a specific plan", new PlanModifyArgs
		{
			PlanFilename = "Plan.p01.hdf",
			Grammar = true
		}),
		new Example("Switch the plan to the GPU solver (preview first with --dry-run)", new PlanModifyArgs
		{
			PlanFilename = "Plan.p01.hdf",
			Sets = new string[1] { "solvertype = GPU" },
			DryRun = true
		}),
		new Example("Set the compute core count", new PlanModifyArgs
		{
			PlanFilename = "Plan.p01.hdf",
			Sets = new string[1] { "corecount = 8" }
		}),
		new Example("Change several settings in one save", new PlanModifyArgs
		{
			PlanFilename = "Plan.p01.hdf",
			Sets = new string[3] { "solvertype = CPU", "corecount = 12", "usefbf = true" }
		}),
		new Example("Switch to the diffusion-wave equation set, solved implicitly", new PlanModifyArgs
		{
			PlanFilename = "Plan.p01.hdf",
			Sets = new string[2] { "equationset = DWE", "timestepping = Implicit" }
		}),
		new Example("Set the plan's own time window (quote dates; durations carry a unit)", new PlanModifyArgs
		{
			PlanFilename = "Plan.p01.hdf",
			Sets = new string[4] { "timewindowmode = Plan", "starttime = '2024-03-01 06:00'", "computetimestep = 30s", "baseoutputinterval = 1h" }
		}),
		new Example("Report the changes as JSON (for scripting)", new PlanModifyArgs
		{
			PlanFilename = "Plan.p01.hdf",
			Sets = new string[1] { "corecount = 8" },
			OutputFormat = OutputFormat.Json
		})
	};

	public bool Execute()
	{
		GDALSetup.InitializeMultiplatform();
		if (Grammar)
		{
			return PrintGrammar();
		}
		if (string.IsNullOrWhiteSpace(PlanFilename))
		{
			Console.Error.WriteLine("A plan file is required.");
			return false;
		}
		List<string> stringList = Sets?.ToList() ?? new List<string>();
		if (stringList.Count == 0)
		{
			Console.Error.WriteLine("At least one assignment is required (e.g. \"corecount = 8\"). Run --grammar to list fields.");
			return false;
		}
		FilterOptions options = new FilterOptions
		{
			CaseSensitiveStrings = CaseSensitive
		};
		if (!CliHelpers.TryResolveCoreFile(PlanFilename, CoreFileType.Plan, (string fn) => new Plan(fn), out var result))
		{
			return false;
		}
		using (result)
		{
			PlanSelectorNode root = new PlanSelectorNode(result);
			List<(string, SelectorResolution)> valueTuples = new List<(string, SelectorResolution)>();
			foreach (string textValue in stringList)
			{
				if (!PathSelector.TryParse(textValue, options, out var selector, out var error))
				{
					Console.Error.WriteLine("Invalid assignment \"" + textValue + "\": " + error);
					return false;
				}
				if (!SelectorResolver.TryResolve(root, selector, options, out var resolution, out var error2))
				{
					Console.Error.WriteLine("Invalid assignment \"" + textValue + "\": " + error2);
					return false;
				}
				if (!resolution.IsScalar)
				{
					Console.Error.WriteLine("Invalid assignment \"" + textValue + "\": a plan only has scalar fields (e.g. \"corecount = 8\").");
					return false;
				}
				if (!resolution.HasAssignment)
				{
					Console.Error.WriteLine("Invalid assignment \"" + textValue + "\": nothing to change (expected '<field> = value').");
					return false;
				}
				valueTuples.Add((textValue, resolution));
			}
			ModifyReport modifyReport = new ModifyReport
			{
				File = PlanFilename,
				DryRun = DryRun
			};
			foreach (var localValue in valueTuples)
			{
				string selector2 = localValue.Item1;
				SelectorResolution selectorResolution = localValue.Item2;
				ModifyChange modifyChange = new ModifyChange
				{
					Selector = selector2,
					Target = selectorResolution.ScalarNode.Name,
					Field = selectorResolution.TerminalField,
					Value = selectorResolution.AssignmentLiteral.ToString()
				};
				if (selectorResolution.ScalarNode.Fields.TrySetValue(selectorResolution.TerminalFieldNormalized, selectorResolution.AssignmentValue, out var error3))
				{
					modifyChange.Changed = 1;
				}
				else
				{
					modifyChange.Skipped.Add(error3);
				}
				modifyReport.Changed += modifyChange.Changed;
				modifyReport.Changes.Add(modifyChange);
			}
			string summary;
			if (DryRun)
			{
				summary = $"Dry run - {modifyReport.Changed} change(s) not written.";
			}
			else if (modifyReport.Changed == 0)
			{
				summary = "No fields changed; nothing to save.";
			}
			else
			{
				result.Save();
				modifyReport.Saved = true;
				summary = $"Saved {PlanFilename} ({modifyReport.Changed} change(s)).";
			}
			WriteReport(modifyReport, summary);
			return true;
		}
	}

	private void WriteReport(ModifyReport report, string summary)
	{
		Console.WriteLine(OutputFormat switch
		{
			OutputFormat.Json => JsonSerializer.Serialize(report, OutputFormats.Json), 
			OutputFormat.Properties => OutputFormats.Properties(report), 
			_ => report.RenderText(summary), 
		});
	}

	private bool PrintGrammar()
	{
		bool notIsNullOrWhiteSpace = !string.IsNullOrWhiteSpace(PlanFilename);
		if (notIsNullOrWhiteSpace && !CliHelpers.LooksLikeCoreFile(PlanFilename, CoreFileType.Plan))
		{
			return false;
		}
		Plan plan = null;
		try
		{
			PlanSelectorNode root;
			if (notIsNullOrWhiteSpace)
			{
				plan = new Plan(PlanFilename);
				root = new PlanSelectorNode(plan);
				Console.WriteLine("Settable grammar for " + Path.GetFileName(PlanFilename));
			}
			else
			{
				root = PlanSelectorNode.CreateSchema();
				Console.WriteLine("Settable grammar (default schema - pass a plan file to reflect its actual values)");
			}
			Console.WriteLine("Target a field with: ras plan modify <file> \"<field> = value\".");
			Console.WriteLine();
			Console.WriteLine(SelectorGrammar.Describe(root, showCounts: false));
			return true;
		}
		finally
		{
			plan?.Dispose();
		}
	}
}
