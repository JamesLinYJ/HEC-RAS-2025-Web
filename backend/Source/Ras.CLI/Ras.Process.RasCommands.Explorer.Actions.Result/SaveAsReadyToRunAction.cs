using System;
using System.Collections.Generic;
using System.IO;
using Ras.Layers;
using Ras.Process.RasCommands.Explorer.Actions.Parameters;
using Utility.Progress;

namespace Ras.Process.RasCommands.Explorer.Actions.Result;

public class SaveAsReadyToRunAction : ILayerAction
{
	public string Id => "result.saveAsReadyToRun";

	public string Name => "Save as Ready-to-Run";

	public string Description => "Saves the result with embedded Geometry, Plan, and Boundary Conditions into a single file that can be run independently";

	public IReadOnlyList<IActionParameter> Parameters { get; } = new IActionParameter[1]
	{
		new FilePathParameter("outputPath", "Output File", "Path for the ready-to-run HDF5 file", isRequired: true, null, ".r2r.h5", "Result.r2r.h5")
	};

	public bool CanExecute(object dataSource, Project project)
	{
		if (!(dataSource is Ras.Layers.Result result))
		{
			return false;
		}
		if (result.Geometry != null && result.Plan != null)
		{
			return result.BoundaryConditions != null;
		}
		return false;
	}

	public string GetDisabledReason(object dataSource, Project project)
	{
		if (!(dataSource is Ras.Layers.Result result))
		{
			return "Not a result";
		}
		if (result.Geometry == null)
		{
			return "Result has no associated geometry";
		}
		if (result.Plan == null)
		{
			return "Result has no associated plan";
		}
		if (result.BoundaryConditions == null)
		{
			return "Result has no associated boundary conditions";
		}
		return "Cannot create ready-to-run file";
	}

	public object GetParameterDefault(string parameterName, object dataSource, Project project)
	{
		if (parameterName == "outputPath" && dataSource is Ras.Layers.Result { Name: var localValue })
		{
			if (localValue == null)
			{
				localValue = "Result";
			}
			string textValue = localValue;
			return string.Join("_", textValue.Split(Path.GetInvalidFileNameChars())) + ".r2r.h5";
		}
		return null;
	}

	public ActionResult Execute(object dataSource, Project project, IDictionary<string, object> parameters, ProgressReporter reporter)
	{
		Ras.Layers.Result result = (Ras.Layers.Result)dataSource;
		if (!parameters.TryGetValue("outputPath", out var localValue) || !(localValue is string textValue))
		{
			return ActionResult.Failed("Output path is required");
		}
		try
		{
			reporter.ReportMessage("Saving ready-to-run file: " + Path.GetFileName(textValue));
			reporter.ReportProgress(10);
			result.SaveAsReadyToRun(textValue);
			reporter.ReportProgress(100);
			return ActionResult.Succeeded("Saved ready-to-run file", textValue);
		}
		catch (Exception ex)
		{
			return ActionResult.Failed(ex.Message);
		}
	}
}
