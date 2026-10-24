using System.Collections.Generic;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Process.RasCommands.Explorer.FeatureTable;

namespace Ras.Process.RasCommands.Explorer.SubLayers;

public static class BoundaryConditionSubLayerProvider
{
	public static IEnumerable<RasTreeNode> GetSubLayers(BoundaryCondition bc, RasTreeNode parent)
	{
		yield return CreateBCLinesNode(bc, parent);
		yield return CreateMetLayerNode("Precipitation", bc.Precipitation, parent);
		yield return CreateMetLayerNode("Wind", bc.Wind, parent);
		yield return CreateMetLayerNode("Evapotranspiration", bc.Evapotranspiration, parent);
		yield return CreateMetLayerNode("Air Temperature", bc.AirTemperature, parent);
		yield return CreateMetLayerNode("Air Pressure", bc.AirPressure, parent);
		yield return CreateMetLayerNode("Air Density", bc.AirDensity, parent);
		yield return CreateMetLayerNode("Humidity", bc.Humidity, parent);
		yield return CreateMetStationsNode(bc, parent);
		yield return CreateInitialConditionsNode(bc, parent);
	}

	private static RasTreeNode CreateBCLinesNode(BoundaryCondition bc, RasTreeNode parent)
	{
		BoundaryConditionLineLayer boundaryConditionLines = bc.BoundaryConditionLines;
		string displaySuffix;
		object dataSource;
		if (boundaryConditionLines == null)
		{
			displaySuffix = "[No Data]";
			dataSource = null;
		}
		else
		{
			int count = boundaryConditionLines.Count;
			displaySuffix = ((count > 0) ? $"[{count}]" : "[No Data]");
			dataSource = new BCLinesWrapper(boundaryConditionLines);
		}
		return new RasTreeNode("BC Lines", parent.FullPath + "/BCLines", RasNodeType.SubLayer, parent)
		{
			DataSource = dataSource,
			DisplaySuffix = displaySuffix
		};
	}

	private static RasTreeNode CreateMetLayerNode(string name, SpatialBoundaryConditionBase layer, RasTreeNode parent)
	{
		string metLayerStatus = GetMetLayerStatus(layer);
		string replace = name.Replace(" ", "");
		return new RasTreeNode(name, parent.FullPath + "/" + replace, RasNodeType.SubLayer, parent)
		{
			DataSource = layer,
			DisplaySuffix = metLayerStatus
		};
	}

	private static string GetMetLayerStatus(SpatialBoundaryConditionBase layer)
	{
		if (layer == null)
		{
			return "[No Data]";
		}
		if (!layer.IsEnabled)
		{
			return "[Disabled]";
		}
		return layer.SpatialDataType switch
		{
			SpatialDataType.Constant => "[Constant]", 
			SpatialDataType.Point => "[Point/Gauge]", 
			SpatialDataType.Gridded => "[Gridded]", 
			_ => "[Enabled]", 
		};
	}

	private static RasTreeNode CreateMetStationsNode(BoundaryCondition bc, RasTreeNode parent)
	{
		MeteorologicStationLayer metStations = bc.MetStations;
		string displaySuffix;
		if (metStations == null)
		{
			displaySuffix = "[No Data]";
		}
		else
		{
			int count = metStations.Count;
			displaySuffix = ((count > 0) ? $"[{count}]" : "[No Data]");
		}
		return new RasTreeNode("Met Stations", parent.FullPath + "/MetStations", RasNodeType.SubLayer, parent)
		{
			DataSource = metStations,
			DisplaySuffix = displaySuffix
		};
	}

	private static RasTreeNode CreateInitialConditionsNode(BoundaryCondition bc, RasTreeNode parent)
	{
		bool hasInitialWaterSurface = bc.InitialWaterSurface != null;
		bool hasInitialVelocityOrHasInitialVelocityXOrHasInitialVelocityY = bc.InitialVelocity != null || bc.InitialVelocityX != null || bc.InitialVelocityY != null;
		string displaySuffix;
		if (hasInitialWaterSurface & hasInitialVelocityOrHasInitialVelocityXOrHasInitialVelocityY)
		{
			displaySuffix = "[WS + Velocity]";
		}
		else if (hasInitialWaterSurface)
		{
			displaySuffix = "[Water Surface]";
		}
		else
		{
			displaySuffix = ((!hasInitialVelocityOrHasInitialVelocityXOrHasInitialVelocityY) ? "[No Data]" : "[Velocity]");
		}
		return new RasTreeNode("Initial Conditions", parent.FullPath + "/InitialConditions", RasNodeType.SubLayer, parent)
		{
			DataSource = bc,
			DisplaySuffix = displaySuffix
		};
	}
}
