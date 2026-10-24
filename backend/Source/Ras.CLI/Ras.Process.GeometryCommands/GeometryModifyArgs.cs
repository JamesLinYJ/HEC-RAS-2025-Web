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

namespace Ras.Process.GeometryCommands;

[Verb("modify", false, null, HelpText = "Modifies topology fields of a RAS geometry file (*.h5) in place, selecting elements with an inline filter.")]
public class GeometryModifyArgs
{
	public const string Verb = "modify";

	[Value(0, MetaName = "geometry", Required = false, HelpText = "Geometry file (*.h5). Optional with --grammar (omit it to print the default schema).")]
	public string GeometryFilename { get; set; }

	[Value(1, MetaName = "set", Required = false, HelpText = "One or more selectors: a path to a collection (region/arc) with an optional [predicate], ending in either an assignment (= value) or a numeric operation (.scale(x)/.add(x)/.set(x)). The [predicate] picks which elements change. E.g. \"topology.region[type = quad AND cellsize < 10].cellsize.scale(0.5)\" (operation) or \"topology.region[area < 10000].type = cartesian\" (assignment). Operations are numeric-only; use = value for enum/bool/string fields. Pass several to change several things in one save. See the examples above. Not required with --grammar.")]
	public IEnumerable<string> Sets { get; set; }

	[Option("grammar", Required = false, HelpText = "List every path and field you can target in this geometry (no changes made), then exit.")]
	public bool Grammar { get; set; }

	[Option("case-sensitive", Required = false, HelpText = "Make string comparisons in the inline filter case-sensitive.")]
	public bool CaseSensitive { get; set; }

	[Option("dry-run", Required = false, HelpText = "Report what would change without writing the file.")]
	public bool DryRun { get; set; }

	[Option("format", Required = false, HelpText = "Output format for the change report: Text (default), Json, or Properties (grep-friendly key=value). Errors always go to stderr as text, so stdout stays parseable.")]
	public OutputFormat OutputFormat { get; set; }

	[Usage(ApplicationAlias = "ras geometry")]
	public static IEnumerable<Example> Examples => new Example[13]
	{
		new Example("List the default set of fields you can target/set (no file needed)", new GeometryModifyArgs
		{
			Grammar = true
		}),
		new Example("List what's settable in a specific geometry (real element counts)", new GeometryModifyArgs
		{
			GeometryFilename = "Geometry.h5",
			Grammar = true
		}),
		new Example("Coarsen every region 1.5x (preview first with --dry-run)", new GeometryModifyArgs
		{
			GeometryFilename = "Geometry.h5",
			Sets = new string[1] { "topology.region.cellsize.scale(1.5)" },
			DryRun = true
		}),
		new Example("Refine just the quad channel regions to half their cell size", new GeometryModifyArgs
		{
			GeometryFilename = "Geometry.h5",
			Sets = new string[1] { "topology.region[type = quad].cellsize.scale(0.5)" }
		}),
		new Example("Refine only the large quad regions (compound filter on a derived field)", new GeometryModifyArgs
		{
			GeometryFilename = "Geometry.h5",
			Sets = new string[1] { "topology.region[type = quad AND area > 500000].cellsize.scale(0.75)" }
		}),
		new Example("Enforce a minimum cell size of 5 (select the too-small ones, then set)", new GeometryModifyArgs
		{
			GeometryFilename = "Geometry.h5",
			Sets = new string[1] { "topology.region[cellsize < 5].cellsize.set(5)" }
		}),
		new Example("Nudge quad orientation by 15 degrees (add onto the current value)", new GeometryModifyArgs
		{
			GeometryFilename = "Geometry.h5",
			Sets = new string[1] { "topology.region[type = quad].RotationDegrees.add(15)" }
		}),
		new Example("Switch tiny regions to a Cartesian patch (enum field - assignment, not an operation)", new GeometryModifyArgs
		{
			GeometryFilename = "Geometry.h5",
			Sets = new string[1] { "topology.region[area < 10000].type = cartesian" }
		}),
		new Example("Denser point spacing along long breakline arcs", new GeometryModifyArgs
		{
			GeometryFilename = "Geometry.h5",
			Sets = new string[1] { "topology.arc[length > 1000].cellsize.scale(0.5)" }
		}),
		new Example("Coarsen specific arcs by index", new GeometryModifyArgs
		{
			GeometryFilename = "Geometry.h5",
			Sets = new string[1] { "topology.arc[index IN (0, 1, 2)].cellsize.scale(1.5)" }
		}),
		new Example("Pin an explicit point count on arcs that have no constant size set", new GeometryModifyArgs
		{
			GeometryFilename = "Geometry.h5",
			Sets = new string[1] { "topology.arc[cellsize IS NULL].count.set(40)" }
		}),
		new Example("Make the longer breaklines follow the terrain exactly (bool field - assignment)", new GeometryModifyArgs
		{
			GeometryFilename = "Geometry.h5",
			Sets = new string[1] { "topology.arc[length > 500].FollowExactly = true" }
		}),
		new Example("Refine the channel and coarsen the overbanks in one save (several selectors)", new GeometryModifyArgs
		{
			GeometryFilename = "Geometry.h5",
			Sets = new string[2] { "topology.region[type = quad].cellsize.scale(0.5)", "topology.region[type = cartesian].cellsize.scale(2)" }
		})
	};

	public bool Execute()
	{
		GDALSetup.InitializeMultiplatform();
		if (Grammar)
		{
			return PrintGrammar();
		}
		if (string.IsNullOrWhiteSpace(GeometryFilename))
		{
			Console.Error.WriteLine("A geometry file is required.");
			return false;
		}
		List<string> stringList = Sets?.ToList() ?? new List<string>();
		if (stringList.Count == 0)
		{
			Console.Error.WriteLine("At least one assignment is required. Run --grammar to list what's settable.");
			return false;
		}
		FilterOptions options = new FilterOptions
		{
			CaseSensitiveStrings = CaseSensitive
		};
		if (!CliHelpers.TryResolveCoreFile(GeometryFilename, CoreFileType.Geometry, (string fn) => new Geometry(fn), out var result))
		{
			return false;
		}
		using (result)
		{
			GeometrySelectorNode root = new GeometrySelectorNode(result);
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
				if (!resolution.HasAssignment && !resolution.HasOperation)
				{
					Console.Error.WriteLine("Invalid assignment \"" + textValue + "\": nothing to change (expected '... = value' or an operation like '....cellsize.scale(1.5)').");
					return false;
				}
				valueTuples.Add((textValue, resolution));
			}
			ModifyReport modifyReport = new ModifyReport
			{
				File = GeometryFilename,
				DryRun = DryRun
			};
			foreach (var localValue in valueTuples)
			{
				string selector2 = localValue.Item1;
				SelectorResolution selectorResolution = localValue.Item2;
				ModifyChange modifyChange = new ModifyChange
				{
					Selector = selector2,
					Field = selectorResolution.TerminalField,
					Operation = (selectorResolution.HasOperation ? selectorResolution.Operation.ToString() : null),
					Value = (selectorResolution.HasOperation ? null : selectorResolution.AssignmentLiteral.ToString())
				};
				if (selectorResolution.IsScalar)
				{
					modifyChange.Target = selectorResolution.ScalarNode.Name;
					if (selectorResolution.ScalarNode.Fields.TrySetValue(selectorResolution.TerminalFieldNormalized, selectorResolution.AssignmentValue, out var error3))
					{
						modifyChange.Changed = 1;
					}
					else
					{
						modifyChange.Skipped.Add(error3);
					}
				}
				else
				{
					modifyChange.Target = selectorResolution.Collection.Name;
					modifyChange.Matched = selectorResolution.MatchedIndices.Count;
					modifyChange.Total = selectorResolution.Collection.Count;
					modifyChange.Changed = ApplySet(selectorResolution, out var skips);
					modifyChange.Skipped = skips;
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
				summary = "No elements changed; nothing to save.";
			}
			else
			{
				result.Save();
				modifyReport.Saved = true;
				summary = $"Saved {GeometryFilename} ({modifyReport.Changed} change(s)).";
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
		bool notIsNullOrWhiteSpace = !string.IsNullOrWhiteSpace(GeometryFilename);
		if (notIsNullOrWhiteSpace && !CliHelpers.LooksLikeCoreFile(GeometryFilename, CoreFileType.Geometry))
		{
			return false;
		}
		Geometry geometry = null;
		try
		{
			GeometrySelectorNode root;
			if (notIsNullOrWhiteSpace)
			{
				geometry = new Geometry(GeometryFilename);
				root = new GeometrySelectorNode(geometry);
				Console.WriteLine("Settable grammar for " + Path.GetFileName(GeometryFilename));
			}
			else
			{
				root = GeometrySelectorNode.CreateSchema();
				Console.WriteLine("Settable grammar (default schema - pass a geometry file to reflect its actual elements)");
			}
			Console.WriteLine("Target a path with: ras geometry modify <file> \"<path> = value\"  (or .scale(x)/.add(x)/.set(x)).");
			Console.WriteLine("Collections are marked []; add a [predicate] on one to choose which elements change.");
			Console.WriteLine();
			Console.WriteLine(SelectorGrammar.Describe(root, notIsNullOrWhiteSpace));
			return true;
		}
		finally
		{
			geometry?.Dispose();
		}
	}

	private static int ApplySet(SelectorResolution resolution, out List<string> skips)
	{
		skips = new List<string>();
		int ordinal = 0;
		foreach (int matchedIndex in resolution.MatchedIndices)
		{
			ISelectorElement element = resolution.Collection.GetElement(matchedIndex);
			object result;
			if (resolution.HasOperation)
			{
				element.TryGetValue(resolution.TerminalFieldNormalized, out var localValue);
				if (!resolution.Operation.TryApply(localValue, resolution.TerminalFieldType, out result, out var error))
				{
					skips.Add($"{resolution.Collection.Name} {matchedIndex}: {error}");
					continue;
				}
			}
			else
			{
				result = resolution.AssignmentValue;
			}
			if (element.TrySetValue(resolution.TerminalFieldNormalized, result, out var error2))
			{
				ordinal++;
			}
			else
			{
				skips.Add(error2);
			}
		}
		return ordinal;
	}
}
