using System;
using System.Collections.Generic;
using System.IO;
using Ras.Layers;
using Ras.Process.RasCommands.Explorer.Actions.Parameters;
using Utility.Progress;

namespace Ras.Process.RasCommands.Explorer.Actions.Terrain;

public class ExportFullCopyAction : ILayerAction
{
	public string Id => "terrain.export.full";

	public string Name => "Export Full Copy";

	public string Description => "Creates a complete copy of the terrain with all raster files";

	public IReadOnlyList<IActionParameter> Parameters { get; } = new IActionParameter[1]
	{
		new FilePathParameter("filename", "Output File", "Path for the new terrain file", isRequired: true, null, ".h5", "terrain-export.h5")
	};

	public bool CanExecute(object dataSource, Project project)
	{
		if (dataSource is Ras.Layers.Terrain terrain)
		{
			return terrain.LayerCount > 0;
		}
		return false;
	}

	public string GetDisabledReason(object dataSource, Project project)
	{
		if (!(dataSource is Ras.Layers.Terrain terrain))
		{
			return "Not a terrain";
		}
		if (terrain.LayerCount == 0)
		{
			return "Terrain has no layers";
		}
		return null;
	}

	public object GetParameterDefault(string parameterName, object dataSource, Project project)
	{
		if (parameterName == "filename" && dataSource is Ras.Layers.Terrain terrain)
		{
			return string.Join("_", terrain.Name.Split(Path.GetInvalidFileNameChars())) + " (Full Copy).h5";
		}
		return null;
	}

	public ActionResult Execute(object dataSource, Project project, IDictionary<string, object> parameters, ProgressReporter reporter)
	{
		Ras.Layers.Terrain terrain = (Ras.Layers.Terrain)dataSource;
		string parameter = (string)parameters["filename"];
		try
		{
			terrain.ExportFullCopy(parameter);
			return ActionResult.Succeeded("Exported terrain to " + parameter, parameter);
		}
		catch (Exception ex)
		{
			return ActionResult.Failed(ex.Message);
		}
	}
}
