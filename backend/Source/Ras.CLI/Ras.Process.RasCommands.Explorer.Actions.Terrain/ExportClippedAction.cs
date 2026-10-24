using System;
using System.Collections.Generic;
using System.IO;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Process.RasCommands.Explorer.Actions.Parameters;
using Utility.Progress;

namespace Ras.Process.RasCommands.Explorer.Actions.Terrain;

public class ExportClippedAction : ILayerAction
{
	public string Id => "terrain.export.clipped";

	public string Name => "Export Clipped";

	public string Description => "Exports a subset of the terrain within the specified extent";

	public IReadOnlyList<IActionParameter> Parameters { get; } = new IActionParameter[2]
	{
		new FilePathParameter("filename", "Output File", "Path for the clipped terrain file", isRequired: true, null, ".h5", "terrain-clipped.h5"),
		new ExtentParameter("extent", "Clip Extent", "Bounding box to clip terrain to")
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
		if (dataSource is Ras.Layers.Terrain terrain)
		{
			if (parameterName == "extent")
			{
				return terrain.ValidExtent;
			}
			if (parameterName == "filename")
			{
				return string.Join("_", terrain.Name.Split(Path.GetInvalidFileNameChars())) + " (Clipped).h5";
			}
		}
		return null;
	}

	public ActionResult Execute(object dataSource, Project project, IDictionary<string, object> parameters, ProgressReporter reporter)
	{
		Ras.Layers.Terrain terrain = (Ras.Layers.Terrain)dataSource;
		string parameter = (string)parameters["filename"];
		Extent exportExt = (Extent)parameters["extent"];
		try
		{
			terrain.ExportClipped(parameter, exportExt, 0f, reporter);
			return ActionResult.Succeeded("Exported clipped terrain to " + parameter, parameter);
		}
		catch (Exception ex)
		{
			return ActionResult.Failed(ex.Message);
		}
	}
}
