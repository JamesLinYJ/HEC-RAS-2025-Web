using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommandLine;
using Geospatial.Vectors.Topology;
using Ras.Layers;
using Utility.Extensions;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Process.Commands;

[Verb("checkmesh", false, null, HelpText = "Attempt to generate the computational mesh of a RAS geometry.")]
public class CheckMeshArgs
{
	public const string Verb = "checkmesh";

	[Option('s', "source", Required = true, HelpText = "Either a filepath to a RAS geometry, or a directory containing multiple RAS geometries.")]
	public string Source { get; set; }

	[Option("recurse", Required = false, HelpText = "Only valid if the given source is a directory. Recurse through the directory to find geometry files. Defaults to false.")]
	public bool Recurse { get; set; }

	public Task<bool> ExecuteAsync()
	{
		return Task.FromResult(Execute());
	}

	public bool Execute()
	{
		ProgressReporter progressReporter = ProgressReporter.ConsoleWrite();
		if (Directory.Exists(Source))
		{
			int ordinal = 0;
			List<Geometry> geometries = new List<Geometry>();
			foreach (string enumerateFile in Directory.EnumerateFiles(Source, "*.h5", Recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
			{
				if (!Geometry.IsValid(enumerateFile))
				{
					continue;
				}
				try
				{
					Geometry geometry = new Geometry(enumerateFile);
					if (!Check(geometry, progressReporter))
					{
						geometries.Add(geometry);
					}
				}
				catch
				{
					progressReporter.ReportMessage("Error loading " + enumerateFile + " (SKIPPING)");
					continue;
				}
				ordinal++;
			}
			if (geometries.Count > 0)
			{
				progressReporter.ReportMessage($"\nThe following {geometries.Count}/{ordinal} geometries failed to compute their mesh:");
				foreach (Geometry geometry3 in geometries)
				{
					progressReporter.ReportMessage(geometry3.Name + " - " + geometry3.Filename);
				}
				return false;
			}
		}
		else
		{
			if (!File.Exists(Source))
			{
				progressReporter.ReportMessage("Provided source is not a File or Directory.");
				return false;
			}
			Geometry geometry2 = new Geometry(Source);
			if (!Check(geometry2, progressReporter))
			{
				progressReporter.ReportMessage("Failed to compute " + geometry2.Name + "'s mesh:");
				return false;
			}
		}
		progressReporter.ReportMessage("All meshes computed sucessfully");
		return true;
	}

	private bool Check(Geometry geo, ProgressReporter reporter)
	{
		reporter.ReportMessage("Checking " + geo.Name);
		try
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			OperationResult operationResult = geo.ConceptualMeshLayer.Topology.BuildConceptualMesh(out var _, out var finalMesh);
			stopwatch.Stop();
			if (!operationResult.Result)
			{
				reporter.ReportMessage("Error on geometry: " + geo.Name + " - " + operationResult.GetConcatenatedMessages("\n"));
				return false;
			}
			reporter.ReportMessage("Mesh generated in " + stopwatch.Elapsed.ToPrettyString() + ". (" + finalMesh.CellCount + " Cells)");
			return true;
		}
		catch (Exception ex)
		{
			reporter.ReportMessage("Error on geometriy: " + geo.Name + " - " + ex.Message);
			reporter.ReportMessage(ex.StackTrace);
			return false;
		}
	}
}
