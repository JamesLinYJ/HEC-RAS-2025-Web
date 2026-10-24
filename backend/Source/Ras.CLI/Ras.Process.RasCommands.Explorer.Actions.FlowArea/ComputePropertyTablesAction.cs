using System;
using System.Collections.Generic;
using Ras.Layers;
using Ras.Process.RasCommands.Explorer.Actions.Parameters;
using Utility.Progress;

namespace Ras.Process.RasCommands.Explorer.Actions.FlowArea;

public class ComputePropertyTablesAction : ILayerAction
{
	public string Id => "flowarea.compute.propertytables";

	public string Name => "Compute Property Tables";

	public string Description => "Computes mesh property tables from terrain and n-value associations";

	public IReadOnlyList<IActionParameter> Parameters { get; } = new IActionParameter[1]
	{
		new BoolParameter("forceRecompute", "Force Recompute", "Recompute tables even if they already exist and appear valid")
	};

	public bool CanExecute(object dataSource, Project project)
	{
		if (dataSource is FlowAreaLayer flowAreaLayer)
		{
			return flowAreaLayer.Count > 0;
		}
		return false;
	}

	public string GetDisabledReason(object dataSource, Project project)
	{
		if (!(dataSource is FlowAreaLayer))
		{
			return "Not a flow area layer";
		}
		return "No flow areas defined";
	}

	public object GetParameterDefault(string parameterName, object dataSource, Project project)
	{
		return null;
	}

	public ActionResult Execute(object dataSource, Project project, IDictionary<string, object> parameters, ProgressReporter reporter)
	{
		FlowAreaLayer flowAreaLayer = (FlowAreaLayer)dataSource;
		bool condition = default;
		int unity;
		if (parameters.TryGetValue("forceRecompute", out var localValue))
		{
			if (localValue is bool)
			{
				condition = (bool)localValue;
				unity = 1;
			}
			else
			{
				unity = 0;
			}
		}
		else
		{
			unity = 0;
		}
		bool forceRecompute = (byte)((uint)unity & (condition ? 1u : 0u)) != 0;
		try
		{
			return ActionResult.FromOperationResult(flowAreaLayer.ComputePropertyTables(forceRecompute, reporter), "Property tables computed successfully");
		}
		catch (Exception ex)
		{
			return ActionResult.Failed(ex.Message);
		}
	}
}
