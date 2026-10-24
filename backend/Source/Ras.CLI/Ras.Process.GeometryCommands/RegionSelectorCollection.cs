using System;
using System.Collections.Generic;
using Geospatial.Vectors.Topology;
using Utility.Filtering;
using Utility.Memory;

namespace Ras.Process.GeometryCommands;

internal sealed class RegionSelectorCollection : ISelectorCollection
{
	private static readonly IReadOnlyList<(string, Type)> DerivedFields = new (string, Type)[4]
	{
		("Index", typeof(int)),
		("ControlPoint", typeof(int)),
		("Area", typeof(double)),
		("InnerRingCount", typeof(int))
	};

	private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>
	{
		{ "type", "PatchType" },
		{ "cellsize", "ConstantSize" }
	};

	private readonly Topology _topo;

	private readonly TopologyFieldSchema _schema;

	public string Name => "region";

	public int Count => _topo.CompletedRegions.Count;

	public IReadOnlyList<string> ProjectionFields => _schema.ProjectionFields;

	public IReadOnlyList<string> KnownFieldNames => _schema.KnownFieldNames;

	public RegionSelectorCollection(Topology topo)
	{
		_topo = topo;
		_schema = new TopologyFieldSchema(DerivedFields, topo.ControlPointMetadata, Aliases);
	}

	public bool TryGetField(string normalizedName, out Type type, out bool settable)
	{
		return _schema.TryGetField(normalizedName, out type, out settable);
	}

	public ISelectorElement GetElement(int index)
	{
		Region region = _topo.CompletedRegions[index];
		int controlPointIndex = region.ControlPointIndex;
		Dictionary<string, object> derived = new Dictionary<string, object>(StringComparer.Ordinal)
		{
			{ "index", index },
			{ "controlpoint", controlPointIndex },
			{
				"area",
				SafeArea(index)
			},
			{ "innerringcount", region.InnerRingCount }
		};
		Table controlPointMetadata = _topo.ControlPointMetadata;
		Table globalMetadata = _topo.GlobalMetadata;
		string writeUnavailableReason = null;
		TableRow readRow;
		TableRow writeRow;
		if (controlPointIndex >= 0 && controlPointIndex < controlPointMetadata.Rows.Count)
		{
			readRow = controlPointMetadata[controlPointIndex];
			writeRow = controlPointMetadata[controlPointIndex];
		}
		else
		{
			readRow = ((globalMetadata.Rows.Count > 0) ? globalMetadata[0] : null);
			writeRow = null;
			writeUnavailableReason = $"region {index} has no control point, so its metadata can't be set individually.";
		}
		return new TopologyElement(_schema, derived, readRow, writeRow, writeUnavailableReason);
	}

	private object SafeArea(int index)
	{
		try
		{
			return Math.Abs(_topo.GetRegionComplexPolygon(index).ComputeArea());
		}
		catch
		{
			return null;
		}
	}
}
