using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommandLine;
using Ras.Layers;
using Utility.Extensions;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Process.Commands;

[Verb("clone", false, null, HelpText = "Clones a RAS file.")]
public class CloneArgs
{
	public const string Verb = "clone";

	[Option('s', "source", Required = true, HelpText = "Source filename to clone. Expected to be one of our core file types, Geometry/Plan/BC/Result/Terrain/NV.")]
	public string SourceFilename { get; set; }

	[Option('o', "output", Required = true, HelpText = "Destination filename. Should not exist. E.G. \"./TerrainOut.h5\"")]
	public string OutputFilename { get; set; }

	[Option('f', "force", Required = false, HelpText = "If set, destination file will be deleted if necessary. Defaults to false.")]
	public bool ForceOverwrite { get; set; }

	[Option("geometry-only", Required = false, HelpText = "Saves only the geometry portion of a result file. Only valid if source file is a result. Defaults to false.")]
	public bool GeometryOnly { get; set; }

	[Option("regenerate-mesh", Required = false, HelpText = "Force regenerate the mesh from inputs. Only valid if the output is a geometry file. Defaults to false.")]
	public bool RecomputeMesh { get; set; }

	[Option("clean-mesh", Required = false, HelpText = "Removes the computed mesh (and property tables) from the file. Only valid if the output is a geometry file. Defaults to false.")]
	public bool CleanMesh { get; set; }

	public Task<bool> ExecuteAsync()
	{
		return Task.FromResult(Execute());
	}

	public bool Execute()
	{
		ProgressReporter progressReporter = ProgressReporter.ConsoleWrite();
		if (!File.Exists(SourceFilename))
		{
			progressReporter.ReportMessage("Source file does not exist.");
			return false;
		}
		if (File.Exists(OutputFilename) && !ForceOverwrite)
		{
			progressReporter.ReportMessage("Output file already exists. If you wish to overwrite it, rerun with --force.");
			return false;
		}
		if (!Project.IsValidCoreFile(SourceFilename, out var fileType))
		{
			progressReporter.ReportMessage("Could not determine source file type.");
			return false;
		}
		CheckDeleteOutput(OutputFilename, progressReporter);
		Stopwatch stopwatch = Stopwatch.StartNew();
		switch (fileType)
		{
		case CoreFileType.Geometry:
		{
			using (Geometry geometry = new Geometry(SourceFilename))
			{
				if (RecomputeMesh)
				{
					OperationResult operationResult = geometry.RecomputeMesh(progressReporter);
					if (!operationResult)
					{
						progressReporter.ReportMessage(operationResult.GetConcatenatedMessages());
						return false;
					}
				}
				else if (CleanMesh)
				{
					geometry.FlowAreaLayer.Clear();
				}
				geometry.SaveAs(OutputFilename);
			}
			break;
		}
		case CoreFileType.BoundaryCondition:
		{
			using (BoundaryCondition boundaryCondition = new BoundaryCondition(SourceFilename))
			{
				boundaryCondition.SaveAs(OutputFilename);
			}
			break;
		}
		case CoreFileType.Plan:
		{
			using (Plan plan = new Plan(SourceFilename))
			{
				plan.SaveAs(OutputFilename);
			}
			break;
		}
		case CoreFileType.Result:
			File.Copy(SourceFilename, OutputFilename, overwrite: true);
			break;
		case CoreFileType.Terrain:
		{
			progressReporter.ReportMessage("TODO: Not cloning terrain tiff files yet.");
			using (Terrain terrain = new Terrain(SourceFilename))
			{
				terrain.SaveAs(OutputFilename);
			}
			break;
		}
		default:
			throw new NotSupportedException();
		}
		Project.MigrateProjectMetadata(SourceFilename, OutputFilename);
		stopwatch.Stop();
		progressReporter.ReportMessage(OutputFilename.InDoubleQuotes() + " saved in " + stopwatch.Elapsed.ToPrettyString());
		return true;
	}

	private static void CheckDeleteOutput(string fn, ProgressReporter pr)
	{
		if (File.Exists(fn))
		{
			pr.ReportMessage("Output file exists, deleting...");
			File.Delete(fn);
		}
	}
}
