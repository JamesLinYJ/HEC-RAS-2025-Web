using System.IO;
using CommandLine;
using Geospatial.GDALAssist;
using Ras.Layers;
using Ras.Layers.Results;
using Ras.Process.Helpers;
using Utility.Extensions;
using Utility.Progress;

namespace Ras.Process.ResultCommands;

[Verb("postprocess", false, null, HelpText = "Post-process a result (*.h5), generating its transposed time-series datasets for fast lookups. The post-process file is always written to its default location: a 'PostProcess.h5' inside a folder named after the result, alongside the result file.")]
public class ResultPostProcessArgs
{
	public const string Verb = "postprocess";

	[Value(0, MetaName = "result", Required = true, HelpText = "Result file (*.h5) to post-process.")]
	public string ResultFilename { get; set; }

	[Option("force", Required = false, HelpText = "Delete any existing post-process file and recompute it from scratch. Without this, an existing post-process file that matches the result is left untouched.")]
	public bool Force { get; set; }

	public bool Execute()
	{
		CliHelpers.TimeStep("Initializing GDAL...", () =>
		{
			GDALSetup.InitializeMultiplatform();
		});
		if (!TryResolveResult(out var result))
		{
			return false;
		}
		using (result)
		{
			ProgressReporter progressReporter = ProgressReporter.ConsoleWrite();
			string expectedPostProcessFilename = result.ExpectedPostProcessFilename();
			if (Force && File.Exists(expectedPostProcessFilename))
			{
				progressReporter.ReportMessage("Deleting existing post-process file: " + expectedPostProcessFilename.InDoubleQuotes());
				File.Delete(expectedPostProcessFilename);
			}
			try
			{
				PostProcessor postProcessor = result.GetPostProcessor();
				if (!Force && postProcessor.DefaultDatasetsExist())
				{
					progressReporter.ReportMessage("Post-process file already exists and is up to date: " + expectedPostProcessFilename.InDoubleQuotes());
					return true;
				}
				progressReporter.ReportMessage("Post-processing result to " + expectedPostProcessFilename.InDoubleQuotes() + "...");
				postProcessor.EnsureDefaultDatasets(progressReporter);
				progressReporter.ReportMessage("Post-process complete.");
				return true;
			}
			finally
			{
				result.DisposePostProcessor();
			}
		}
	}

	private bool TryResolveResult(out Result result)
	{
		return CliHelpers.TryResolveFile(ResultFilename, (string fn) => new Result(fn), out result);
	}
}
