using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Geospatial.Vectors.Graph;
using Geospatial.Vectors.Topology;
using H5Assist;
using Microsoft.Extensions.Logging;
using Utility.Extensions;
using Utility.Logging;
using Utility.Memory;

namespace Geospatial.IO.Vectors;

public static class GraphReader
{
	private static readonly ILogger _logger = LogHelper.GetLogger("GraphReader");

	public const string GlobalMetadataPrefix = "GraphGlobal_";

	public static Graph Read(H5Reader hr, string group)
	{
		Graph graph = new Graph();
		TopologyReader.ReadGlobalMetadata(hr, group, "GraphGlobal_", graph.GlobalMetadata);
		string arcConnectivityPath = TopologyReader.ArcConnectivityPath(group);
		if (hr.ExistsAsDataset(arcConnectivityPath))
		{
			hr.ReadDataset(arcConnectivityPath, out int[,] values);
			int length = values.GetLength(0);
			if (values.GetLength(1) != 2)
			{
				throw new Exception("Invalid Graph saved - requires 2 columns for " + arcConnectivityPath);
			}
			for (int i = 0; i < length; i++)
			{
				graph.AddArc(new GraphArc(values[i, 0], values[i, 1]));
			}
			string arcInternalPointDataset = TopologyReader.ArcInternalPointDataset(group);
			if (hr.ExistsAsDataset(arcInternalPointDataset))
			{
				List<Point>[] tryReadPointCollectionSubtype = HDF5VectorReader.TryReadPointCollectionSubtype(hr, arcInternalPointDataset, (Memory<Point> pts, Memory<double> zs, Memory<double> ys) =>
				{
					Span<Point> ptsSpan = pts.Span;
					List<Point> points = new List<Point>(ptsSpan.Length);
					for (int j = 0; j < ptsSpan.Length; j++)
					{
						points.Add(ptsSpan[j]);
					}
					return points;
				});
				if (tryReadPointCollectionSubtype == null)
				{
					_logger.LogError("{Dataset} exists but its internal points could not be read; loading arcs with no internal points.", arcInternalPointDataset);
				}
				else if (tryReadPointCollectionSubtype.Length != graph.Arcs.Count)
				{
					_logger.LogError("{Dataset} internal-point count ({Actual}) disagrees with arc count ({Expected}); skipping internal points.", arcInternalPointDataset, tryReadPointCollectionSubtype.Length, graph.Arcs.Count);
				}
				else
				{
					for (int intervalIndex = 0; intervalIndex < graph.Arcs.Count; intervalIndex++)
					{
						List<Point> tryReadPointCollectionSubtypeList = tryReadPointCollectionSubtype[intervalIndex];
						if (!tryReadPointCollectionSubtypeList.IsNullOrEmpty())
						{
							graph.Arcs[intervalIndex].SetPoints(tryReadPointCollectionSubtypeList);
						}
					}
				}
			}
			string arcAttributeDataset = TopologyReader.ArcAttributeDataset(group);
			if (hr.ExistsAsDataset(arcAttributeDataset))
			{
				Table table = hr.ReadCompoundElementsAsTable(arcAttributeDataset);
				HealMetadataTable(table, graph.Arcs.Count, "Arc");
				graph.SetArcMetadataTable(table);
			}
		}
		string nodeDataset = TopologyReader.NodeDataset(group);
		if (hr.ExistsAsDataset(nodeDataset))
		{
			Point[] tryReadPoints = HDF5VectorReader.TryReadPoints(hr, nodeDataset, out var _, out var _);
			if (tryReadPoints.IsNullOrEmpty())
			{
				_logger.LogWarning("{Dataset} exists but could not be read.", nodeDataset);
			}
			else
			{
				for (int intervalIndex2 = 0; intervalIndex2 < tryReadPoints.Length; intervalIndex2++)
				{
					graph.AddNode(tryReadPoints[intervalIndex2]);
				}
				string nodeAttributeDataset = TopologyReader.NodeAttributeDataset(group);
				if (hr.ExistsAsDataset(nodeAttributeDataset))
				{
					Table table2 = hr.ReadCompoundElementsAsTable(nodeAttributeDataset);
					HealMetadataTable(table2, graph.Nodes.Count, "Node");
					graph.SetNodeMetadataTable(table2);
				}
			}
		}
		graph.RebuildNodeToArcPointers();
		return graph;
	}

	private static void HealMetadataTable(Table table, int expectedRowCount, string elementType)
	{
		table.InitValueTypesToDefault = true;
		if (table.Rows.Count < expectedRowCount)
		{
			_logger.LogWarning("Incorrect row count for '{ElementType}', appending. {ExpectedRowCount} vs {ActualRowCount}", elementType, expectedRowCount, table.Rows.Count);
			for (int i = table.Rows.Count; i < expectedRowCount; i++)
			{
				table.AddNewRow();
			}
		}
		else if (table.Rows.Count > expectedRowCount)
		{
			_logger.LogWarning("Incorrect row count for '{ElementType}', trimming. {ExpectedRowCount} vs {ActualRowCount}", elementType, expectedRowCount, table.Rows.Count);
			table.RemoveRowRange(expectedRowCount, table.Rows.Count - expectedRowCount);
		}
	}

	public static void Write(H5Writer hw, string group, Graph graph)
	{
		if (!hw.ExistsAsGroup(group))
		{
			hw.CreateGroup(group);
		}
		TopologyReader.WriteGlobalMetadata(hw, group, "GraphGlobal_", graph.GlobalMetadata);
		string arcConnectivityPath = TopologyReader.ArcConnectivityPath(group);
		hw.Delete(arcConnectivityPath);
		if (graph.Arcs.Count > 0)
		{
			int[,] intValues = new int[graph.Arcs.Count, 2];
			for (int i = 0; i < graph.Arcs.Count; i++)
			{
				GraphArc graphArc = graph.Arcs[i];
				intValues[i, 0] = graphArc.StartNode;
				intValues[i, 1] = graphArc.EndNode;
			}
			hw.WriteDataset(arcConnectivityPath, intValues);
		}
		string arcInternalPointDataset = TopologyReader.ArcInternalPointDataset(group);
		hw.Delete(arcInternalPointDataset);
		List<IList<Point>> pointListList = new List<IList<Point>>();
		pointListList.AddRange(((IEnumerable<GraphArc>)graph.Arcs).Select((Func<GraphArc, IList<Point>>)((GraphArc a) => a.InternalPoints)));
		if (pointListList.Any((IList<Point> l) => (l?.Count ?? 0) > 0))
		{
			HDF5VectorReader.Write(hw, pointListList, arcInternalPointDataset);
		}
		string arcAttributeDataset = TopologyReader.ArcAttributeDataset(group);
		hw.Delete(arcAttributeDataset);
		if (graph.ArcMetadata.Columns.Count > 0)
		{
			hw.WriteCompoundTable(arcAttributeDataset, graph.ArcMetadata);
		}
		string nodeDataset = TopologyReader.NodeDataset(group);
		hw.Delete(nodeDataset);
		if (graph.Nodes.Count > 0)
		{
			HDF5VectorReader.Write(hw, graph.Nodes.Select((Node n) => n.Point).ToArray(), nodeDataset);
		}
		string nodeAttributeDataset = TopologyReader.NodeAttributeDataset(group);
		hw.Delete(nodeAttributeDataset);
		if (graph.NodeMetadata.Columns.Count > 0)
		{
			hw.WriteCompoundTable(nodeAttributeDataset, graph.NodeMetadata);
		}
	}
}
