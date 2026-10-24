using System.Collections.Generic;
using Geospatial.Vectors.Topology;
using Ras.Hydraulics;
using Ras.Layers;
using Ras.Process.RasCommands.Explorer.FeatureTable;

namespace Ras.Process.RasCommands.Explorer.SubLayers;

public static class GeometrySubLayerProvider
{
	public static IEnumerable<RasTreeNode> GetSubLayers(Geometry geo, RasTreeNode parent)
	{
		Topology topology = geo.ConceptualMeshLayer?.Topology;
		string displaySuffix = ((topology != null) ? "[Loaded]" : "[Empty]");
		RasTreeNode rasTreeNode = new RasTreeNode("TopologyLayer", parent.FullPath + "/TopologyLayer", RasNodeType.SubLayer, parent)
		{
			DataSource = geo.ConceptualMeshLayer,
			DisplaySuffix = displaySuffix
		};
		if (topology != null)
		{
			int arcsTopologyTerm = topology.Arcs?.Count ?? 0;
			RasTreeNode rasTreeNode2 = new RasTreeNode("Arcs", rasTreeNode.FullPath + "/Arcs", RasNodeType.SubLayer, rasTreeNode)
			{
				DataSource = new TopologyArcsWrapper(topology),
				DisplaySuffix = $"[{arcsTopologyTerm}]"
			};
			rasTreeNode.Children.Add(rasTreeNode2);
			int completedRegionsTopologyTerm = topology.CompletedRegions?.Count ?? 0;
			RasTreeNode rasTreeNode3 = new RasTreeNode("Regions", rasTreeNode.FullPath + "/Regions", RasNodeType.SubLayer, rasTreeNode)
			{
				DataSource = new TopologyRegionsWrapper(topology),
				DisplaySuffix = $"[{completedRegionsTopologyTerm}]"
			};
			rasTreeNode.Children.Add(rasTreeNode3);
			rasTreeNode.ChildrenLoaded = true;
		}
		yield return rasTreeNode;
		FlowAreaLayer flowAreaLayer = geo.FlowAreaLayer;
		if (flowAreaLayer != null && flowAreaLayer.Count > 0)
		{
			int count = geo.FlowAreaLayer.Count;
			string displaySuffix2 = ((count == 1) ? "[1 mesh]" : $"[{count} meshes]");
			RasTreeNode flowAreaNode = new RasTreeNode("FlowAreaLayer", parent.FullPath + "/FlowAreaLayer", RasNodeType.SubLayer, parent)
			{
				DataSource = geo.FlowAreaLayer,
				DisplaySuffix = displaySuffix2
			};
			yield return flowAreaNode;
			FlowArea flowArea = geo.FlowAreaLayer[0];
			string displaySuffix3;
			object dataSource;
			if (flowArea?.PropertyTableSignature != null)
			{
				displaySuffix3 = "[Computed]";
				dataSource = flowArea.PropertyTableSignature;
			}
			else if (flowArea?.PropertyTables != null)
			{
				displaySuffix3 = "[No Signature]";
				dataSource = flowArea;
			}
			else
			{
				displaySuffix3 = "[Not Computed]";
				dataSource = null;
			}
			RasTreeNode rasTreeNode4 = new RasTreeNode("Property Tables", parent.FullPath + "/FlowAreaLayer/PropertyTables", RasNodeType.SubLayer, flowAreaNode)
			{
				DataSource = dataSource,
				DisplaySuffix = displaySuffix3
			};
			flowAreaNode.Children.Add(rasTreeNode4);
			flowAreaNode.ChildrenLoaded = true;
		}
		if (geo.CulvertBarrelLayer != null)
		{
			string displaySuffix4 = ((geo.CulvertBarrelLayer.Count > 0) ? $"[{geo.CulvertBarrelLayer.Count}]" : "[Empty]");
			yield return new RasTreeNode("CulvertBarrels", parent.FullPath + "/CulvertBarrels", RasNodeType.SubLayer, parent)
			{
				DataSource = geo.CulvertBarrelLayer,
				DisplaySuffix = displaySuffix4
			};
		}
		OpeningPropertiesLayer openingPropertiesLayer = geo.OpeningPropertiesLayer;
		if (openingPropertiesLayer != null && openingPropertiesLayer.Count > 0)
		{
			yield return new RasTreeNode("CulvertOpeningProperties", parent.FullPath + "/CulvertOpeningProperties", RasNodeType.SubLayer, parent)
			{
				DataSource = geo.OpeningPropertiesLayer,
				DisplaySuffix = $"[{geo.OpeningPropertiesLayer.Count}]"
			};
		}
		BarrelPropertiesLayer barrelPropertiesLayer = geo.BarrelPropertiesLayer;
		if (barrelPropertiesLayer != null && barrelPropertiesLayer.Count > 0)
		{
			yield return new RasTreeNode("CulvertBarrelProperties", parent.FullPath + "/CulvertBarrelProperties", RasNodeType.SubLayer, parent)
			{
				DataSource = geo.BarrelPropertiesLayer,
				DisplaySuffix = $"[{geo.BarrelPropertiesLayer.Count}]"
			};
		}
	}
}
