using System;
using System.Collections.Generic;
using System.IO;
using Ras.Layers;
using Ras.Process.RasCommands.Explorer.Actions.Parameters;
using Utility.Progress;

namespace Ras.Process.RasCommands.Explorer.Actions.ProjectActions;

public class ExportProjectionAction : ILayerAction
{
	public string Id => "project.export.projection";

	public string Name => "Export Projection (.prj)";

	public string Description => "Exports the project's coordinate system as a .prj file";

	public IReadOnlyList<IActionParameter> Parameters { get; } = new IActionParameter[1]
	{
		new FilePathParameter("filename", "Output File", "Path for the .prj file", isRequired: true, null, ".prj", "projection.prj")
	};

	public bool CanExecute(object dataSource, Project project)
	{
		if (dataSource is Project project2)
		{
			return project2.Projection != null;
		}
		return false;
	}

	public string GetDisabledReason(object dataSource, Project project)
	{
		if (!(dataSource is Project))
		{
			return "Not a project";
		}
		if ((dataSource as Project)?.Projection == null)
		{
			return "Project has no projection defined";
		}
		return null;
	}

	public object GetParameterDefault(string parameterName, object dataSource, Project project)
	{
		if (parameterName == "filename" && dataSource is Project project2)
		{
			return string.Join("_", project2.Name.Split(Path.GetInvalidFileNameChars())) + ".prj";
		}
		return null;
	}

	public ActionResult Execute(object dataSource, Project project, IDictionary<string, object> parameters, ProgressReporter reporter)
	{
		Project project2 = (Project)dataSource;
		string parameter = (string)parameters["filename"];
		try
		{
			if (project2.Projection == null)
			{
				return ActionResult.Failed("Project has no projection defined");
			}
			string wKT = project2.Projection.GetWKT();
			if (string.IsNullOrEmpty(wKT))
			{
				return ActionResult.Failed("Could not get WKT from projection");
			}
			File.WriteAllText(parameter, wKT);
			return ActionResult.Succeeded("Exported projection to " + Path.GetFileName(parameter), parameter);
		}
		catch (Exception ex)
		{
			return ActionResult.Failed(ex.Message);
		}
	}
}
