using System;
using System.Collections.Generic;
using Ras.Layers;
using Ras.Process.RasCommands.Explorer.Actions.Parameters;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Process.RasCommands.Explorer.Actions.GeometryActions;

public class ComputePropertyTablesAction : ILayerAction
{
	public string Id => "geometry.compute.propertytables";

	public string Name => "Compute Property Tables";

	public string Description => "Computes hydraulic property tables from terrain and mesh associations";

	public IReadOnlyList<IActionParameter> Parameters { get; } = new IActionParameter[1]
	{
		new BoolParameter("forceRecompute", "Force Recompute", "Recompute tables even if they already exist")
	};

	public bool CanExecute(object dataSource, Project project)
	{
		if (!(dataSource is Geometry { FlowAreaLayer: var flowAreaLayer }))
		{
			return false;
		}
		if (flowAreaLayer == null)
		{
			return false;
		}
		return flowAreaLayer.Count > 0;
	}

	public string GetDisabledReason(object dataSource, Project project)
	{
		if (!(dataSource is Geometry))
		{
			return "Not a geometry";
		}
		Geometry geometry = dataSource as Geometry;
		if (geometry.FlowAreaLayer == null || geometry.FlowAreaLayer.Count == 0)
		{
			return "No flow areas defined";
		}
		return null;
	}

	public object GetParameterDefault(string parameterName, object dataSource, Project project)
	{
		return null;
	}

	public ActionResult Execute(object dataSource, Project project, IDictionary<string, object> parameters, ProgressReporter reporter)
	{
		Geometry geometry = (Geometry)dataSource;
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
			reporter.ReportMessage("Computing property tables for " + geometry.Name + "...");
			OperationResult operationResult = geometry.FlowAreaLayer.ComputePropertyTables(forceRecompute, reporter);
			if (operationResult.Result)
			{
				reporter.ReportMessage("Saving geometry...");
				geometry.Save();
				return ActionResult.FromOperationResult(operationResult, "Property tables computed successfully");
			}
			return ActionResult.FromOperationResult(operationResult, "Property table computation failed");
		}
		catch (Exception ex)
		{
			return ActionResult.Failed(ex.Message);
		}
	}
}
