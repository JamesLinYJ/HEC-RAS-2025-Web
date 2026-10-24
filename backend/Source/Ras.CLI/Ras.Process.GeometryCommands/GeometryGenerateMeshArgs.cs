using System;
using System.IO;
using CommandLine;
using Geospatial.GDALAssist;
using Ras.Layers;
using Ras.Process.H5Commands;
using Ras.Process.Helpers;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Process.GeometryCommands;

[Verb("generate-mesh", false, null, HelpText = "Generates the computational mesh from an existing RAS geometry file (*.h5) and saves it in place.")]
public class GeometryGenerateMeshArgs
{
	public const string Verb = "generate-mesh";

	[Value(0, MetaName = "geometry", Required = true, HelpText = "Geometry file (*.h5).")]
	public string Source { get; set; }

	[Option("squeeze", Required = false, HelpText = "If set, will squeeze the file after successfully generating a mesh.")]
	public bool Squeeze { get; set; }

	public bool Execute()
	{
		ProgressReporter pr = ProgressReporter.ConsoleWrite();
		try
		{
			CliHelpers.TimeStep("Initializing GDAL...", () =>
			{
				GDALSetup.InitializeMultiplatform();
			});
			if (!File.Exists(Source))
			{
				pr.ReportError("Source file does not exist.");
				return false;
			}
			Geometry geo = new Geometry(Source);
			try
			{
				OperationResult operationResult = CliHelpers.TimeStep("Computing Mesh...", () => geo.RecomputeMesh(pr));
				if (!operationResult.Result)
				{
					pr.ReportError("Mesh generation failed: " + operationResult.GetConcatenatedMessages("\n"));
					return false;
				}
				CliHelpers.TimeStep("Saving Geometry...", () =>
				{
					geo.Save();
				});
				if (Squeeze)
				{
					return new SqueezeArgs
					{
						Source = Source
					}.Execute();
				}
				return true;
			}
			finally
			{
				if (geo != null)
				{
					((IDisposable)geo).Dispose();
				}
			}
		}
		catch (Exception ex)
		{
			pr.ReportError("Error: " + ex.Message);
			return false;
		}
	}
}
