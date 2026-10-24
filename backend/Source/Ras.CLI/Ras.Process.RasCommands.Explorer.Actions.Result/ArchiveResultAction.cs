using System;
using System.Collections.Generic;
using Ras.Layers;
using Utility.Progress;

namespace Ras.Process.RasCommands.Explorer.Actions.Result;

public class ArchiveResultAction : ILayerAction
{
	public string Id => "result.archive";

	public string Name => "Archive Result";

	public string Description => "Creates a timestamped backup copy of the result file";

	public IReadOnlyList<IActionParameter> Parameters { get; } = Array.Empty<IActionParameter>();

	public bool CanExecute(object dataSource, Project project)
	{
		if (dataSource is Ras.Layers.Result result)
		{
			return !string.IsNullOrEmpty(result.Filename);
		}
		return false;
	}

	public string GetDisabledReason(object dataSource, Project project)
	{
		if (!(dataSource is Ras.Layers.Result))
		{
			return "Not a result";
		}
		return "No result file to archive";
	}

	public object GetParameterDefault(string parameterName, object dataSource, Project project)
	{
		return null;
	}

	public ActionResult Execute(object dataSource, Project project, IDictionary<string, object> parameters, ProgressReporter reporter)
	{
		Ras.Layers.Result result = (Ras.Layers.Result)dataSource;
		try
		{
			string fileCreated;
			return ActionResult.FromOperationResult(result.ArchiveResult(out fileCreated), "Archived to " + fileCreated);
		}
		catch (Exception ex)
		{
			return ActionResult.Failed(ex.Message);
		}
	}
}
