using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using H5Assist;
using Utility.Attributes;
using Microsoft.Extensions.Logging;
using Utility.Extensions;
using Utility.Logging;
using Utility.Memory;

namespace Geospatial.IO.Vectors;

public static class TopologyReader
{
	private static readonly ILogger _logger = LogHelper.GetLogger("TopologyReader");

	public const string GlobalMetadataPrefix = "TopoGlobal_";

	public static string ArcConnectivityPath(string group)
	{
		return H5Reader.PathCombine(group, "Arc Connectivity");
	}

	public static string ArcInternalPointDataset(string group)
	{
		return H5Reader.PathCombine(group, "Arc Internal Points");
	}

	public static string ArcAttributeDataset(string group)
	{
		return H5Reader.PathCombine(group, "Arc Attributes");
	}

	public static string NodeDataset(string group)
	{
		return H5Reader.PathCombine(group, "Nodes");
	}

	public static string NodeAttributeDataset(string group)
	{
		return H5Reader.PathCombine(group, "Node Attributes");
	}

	public static string ControlPointDataset(string group)
	{
		return H5Reader.PathCombine(group, "Control Points");
	}

	public static string ControlPointAttributeDataset(string group)
	{
		return H5Reader.PathCombine(group, "Control Point Attributes");
	}

	public static Topology Read(H5Reader hr, string group)
	{
		Topology topology = new Topology();
		ReadGlobalMetadata(hr, group, "TopoGlobal_", topology.GlobalMetadata, MeshExtensions.GlobalMetadataDefaults);
		string arcConnectivityPath = ArcConnectivityPath(group);
		if (hr.ExistsAsDataset(arcConnectivityPath))
		{
			hr.ReadDataset(arcConnectivityPath, out int[,] values);
			int length = values.GetLength(0);
			if (values.GetLength(1) != 2)
			{
				throw new Exception("Invalid Topology saved - requires 2 columns for " + arcConnectivityPath);
			}
			for (int i = 0; i < length; i++)
			{
				topology.AddArc(new Arc(values[i, 0], values[i, 1], null));
			}
			string arcInternalPointDataset = ArcInternalPointDataset(group);
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
				else if (tryReadPointCollectionSubtype.Length != topology.Arcs.Count)
				{
					_logger.LogError("{Dataset} internal-point count ({Actual}) disagrees with arc count ({Expected}); skipping internal points.", arcInternalPointDataset, tryReadPointCollectionSubtype.Length, topology.Arcs.Count);
				}
				else
				{
					for (int intervalIndex = 0; intervalIndex < topology.Arcs.Count; intervalIndex++)
					{
						List<Point> tryReadPointCollectionSubtypeList = tryReadPointCollectionSubtype[intervalIndex];
						if (!tryReadPointCollectionSubtypeList.IsNullOrEmpty())
						{
							topology.Arcs[intervalIndex].SetPoints(tryReadPointCollectionSubtypeList);
						}
					}
				}
			}
			string arcAttributeDataset = ArcAttributeDataset(group);
			if (hr.ExistsAsDataset(arcAttributeDataset))
			{
				Table table = hr.ReadCompoundElementsAsTable(arcAttributeDataset);
				table.RestoreSchemaEnumColumns(MeshExtensions.ArcDefaults);
				topology.SetArcMetadataTable(table);
			}
		}
		string nodeDataset = NodeDataset(group);
		double[] z;
		double[] m;
		if (hr.ExistsAsDataset(nodeDataset))
		{
			Point[] tryReadPoints = HDF5VectorReader.TryReadPoints(hr, nodeDataset, out z, out m);
			if (tryReadPoints.IsNullOrEmpty())
			{
				_logger.LogWarning("{Dataset} exists but could not be read.", nodeDataset);
			}
			else
			{
				for (int intervalIndex2 = 0; intervalIndex2 < tryReadPoints.Length; intervalIndex2++)
				{
					topology.AddNode(tryReadPoints[intervalIndex2]);
				}
				string nodeAttributeDataset = NodeAttributeDataset(group);
				if (hr.ExistsAsDataset(nodeAttributeDataset))
				{
					Table nodeMetadataTable = hr.ReadCompoundElementsAsTable(nodeAttributeDataset);
					topology.SetNodeMetadataTable(nodeMetadataTable);
				}
			}
		}
		string controlPointDataset = ControlPointDataset(group);
		if (hr.ExistsAsDataset(controlPointDataset))
		{
			Point[] tryReadPointsArray = HDF5VectorReader.TryReadPoints(hr, controlPointDataset, out m, out z);
			if (tryReadPointsArray.IsNullOrEmpty())
			{
				_logger.LogWarning("{Dataset} exists but could not be read.", controlPointDataset);
			}
			else
			{
				for (int intervalIndex3 = 0; intervalIndex3 < tryReadPointsArray.Length; intervalIndex3++)
				{
					topology.AddRegionControlPoint(tryReadPointsArray[intervalIndex3]);
				}
				string controlPointAttributeDataset = ControlPointAttributeDataset(group);
				if (hr.ExistsAsDataset(controlPointAttributeDataset))
				{
					Table table2 = hr.ReadCompoundElementsAsTable(controlPointAttributeDataset);
					table2.RestoreSchemaEnumColumns(MeshExtensions.ControlPointDefaults);
					topology.SetControlPointMetadataTable(table2);
				}
			}
		}
		return topology;
	}

	public static void Write(H5Writer hw, string group, Topology topo)
	{
		if (!hw.ExistsAsGroup(group))
		{
			hw.CreateGroup(group);
		}
		WriteGlobalMetadata(hw, group, "TopoGlobal_", topo.GlobalMetadata, MeshExtensions.GlobalMetadataDefaults);
		string arcConnectivityPath = ArcConnectivityPath(group);
		hw.Delete(arcConnectivityPath);
		if (topo.Arcs.Count > 0)
		{
			int[,] intValues = new int[topo.Arcs.Count, 2];
			for (int i = 0; i < topo.Arcs.Count; i++)
			{
				Arc arc = topo.Arcs[i];
				intValues[i, 0] = arc.StartNode;
				intValues[i, 1] = arc.EndNode;
			}
			hw.WriteDataset(arcConnectivityPath, intValues);
		}
		string arcInternalPointDataset = ArcInternalPointDataset(group);
		hw.Delete(arcInternalPointDataset);
		List<IList<Point>> pointListList = new List<IList<Point>>();
		pointListList.AddRange(topo.Arcs.Select((Arc a) => a.InternalPoints));
		if (pointListList.Any((IList<Point> l) => (l?.Count ?? 0) > 0))
		{
			HDF5VectorReader.Write(hw, pointListList, arcInternalPointDataset);
		}
		string arcAttributeDataset = ArcAttributeDataset(group);
		hw.Delete(arcAttributeDataset);
		if (topo.ArcMetadata.Columns.Count > 0)
		{
			hw.WriteCompoundTable(arcAttributeDataset, topo.ArcMetadata);
		}
		string nodeDataset = NodeDataset(group);
		hw.Delete(nodeDataset);
		if (topo.Nodes.Count > 0)
		{
			HDF5VectorReader.Write(hw, topo.Nodes.Select((Node n) => n.Point).ToArray(), nodeDataset);
		}
		string nodeAttributeDataset = NodeAttributeDataset(group);
		hw.Delete(nodeAttributeDataset);
		if (topo.NodeMetadata.Columns.Count > 0)
		{
			hw.WriteCompoundTable(nodeAttributeDataset, topo.NodeMetadata);
		}
		string controlPointDataset = ControlPointDataset(group);
		hw.Delete(controlPointDataset);
		if (topo.RegionControlPoints.Count > 0)
		{
			HDF5VectorReader.Write(hw, topo.RegionControlPoints.ToArray(), controlPointDataset);
		}
		string controlPointAttributeDataset = ControlPointAttributeDataset(group);
		hw.Delete(controlPointAttributeDataset);
		if (topo.ControlPointMetadata.Columns.Count > 0 && topo.ControlPointMetadata.Rows.Count > 0)
		{
			hw.WriteCompoundTable(controlPointAttributeDataset, topo.ControlPointMetadata);
		}
	}

	public static void ReadGlobalMetadata(H5Reader hr, string group, string prefix, Table globalMetadata)
	{
		ReadGlobalMetadata(hr, group, prefix, globalMetadata, null);
	}

	public static void ReadGlobalMetadata(H5Reader hr, string group, string prefix, Table globalMetadata, IReadOnlyList<TableDefault> schema)
	{
		foreach (KeyValuePair<string, object> allAttribute in hr.ReadAllAttributes(group))
		{
			if (!allAttribute.Key.StartsWith(prefix))
			{
				continue;
			}

			string columnName = allAttribute.Key.Substring(prefix.Length);
			TableDefault enumColumn = schema?.FirstOrDefault(column => column.Name == columnName && column.Type.IsEnum);
			object value = allAttribute.Value;
			Type columnType;
			if (enumColumn != null)
			{
				value = DeserializeGlobalEnum(allAttribute.Key, enumColumn.Type, value);
				columnType = enumColumn.Type;
			}
			else
			{
				columnType = value.GetType();
			}

			globalMetadata.AddColumn(columnName, columnType);
			globalMetadata[0][columnName] = value;
		}
	}

	public static void WriteGlobalMetadata(H5Writer hw, string group, string prefix, Table globalMetadata)
	{
		WriteGlobalMetadata(hw, group, prefix, globalMetadata, null);
	}

	public static void WriteGlobalMetadata(H5Writer hw, string group, string prefix, Table globalMetadata, IReadOnlyList<TableDefault> schema)
	{
		if (globalMetadata.Columns.Count != 0 && globalMetadata.Rows.Count != 0)
		{
			TableRow tableRow = globalMetadata.Rows[0];
			for (int i = 0; i < globalMetadata.Columns.Count; i++)
			{
				string columnName = globalMetadata.Columns[i].Name;
				string attributeName = prefix + columnName;
				object value = tableRow[i];
				TableDefault enumColumn = schema?.FirstOrDefault(column => column.Name == columnName && column.Type.IsEnum);
				if (enumColumn != null)
				{
					value = SerializeGlobalEnum(attributeName, enumColumn.Type, value);
				}
				hw.WriteAttribute(group, attributeName, value);
			}
		}
	}

	private static object SerializeGlobalEnum(string attributeName, Type enumType, object value)
	{
		// Match H5Metadata's stable alias/name string representation; H5Writer's object attribute overload does not accept enum objects.
		if (value == null || value.GetType() != enumType || !Enum.IsDefined(enumType, value))
		{
			throw new InvalidDataException($"Cannot write invalid value for HDF global metadata attribute '{attributeName}' ({enumType.Name}).");
		}

		if (AliasAttribute.GetSerializeMap(enumType).TryGetValue(value, out string serializedValue) && !string.IsNullOrEmpty(serializedValue))
		{
			return serializedValue;
		}
		throw new InvalidDataException($"Cannot serialize HDF global metadata attribute '{attributeName}' ({enumType.Name}).");
	}

	private static object DeserializeGlobalEnum(string attributeName, Type enumType, object value)
	{
		// Read both the string form written here and defined integral values already present in primitive HDF attributes.
		if (value is string textValue)
		{
			if (AliasAttribute.GetDeserializeMap(enumType).TryGetValue(textValue, out object parsedValue))
			{
				return parsedValue;
			}
		}
		else if (value is byte || value is short || value is int || value is long)
		{
			try
			{
				object underlyingValue = Convert.ChangeType(value, Enum.GetUnderlyingType(enumType), System.Globalization.CultureInfo.InvariantCulture);
				object parsedValue = Enum.ToObject(enumType, underlyingValue);
				if (Enum.IsDefined(enumType, parsedValue))
				{
					return parsedValue;
				}
			}
			catch (Exception exception) when (exception is InvalidCastException || exception is FormatException || exception is OverflowException)
			{
				throw new InvalidDataException($"Invalid numeric value for HDF global metadata attribute '{attributeName}' ({enumType.Name}).", exception);
			}
		}

		throw new InvalidDataException($"Unrecognized value for HDF global metadata attribute '{attributeName}' ({enumType.Name}); the value was not applied.");
	}
}
