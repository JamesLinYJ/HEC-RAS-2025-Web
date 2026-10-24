using System;
using System.Collections.Generic;
using Geospatial.Vectors.Topology;
using Utility.Filtering;
using Utility.Memory;

namespace Ras.Process.GeometryCommands;

internal sealed class ArcSelectorCollection : ISelectorCollection
{
	private static readonly IReadOnlyList<(string, Type)> DerivedFields = new (string, Type)[4]
	{
		("Index", typeof(int)),
		("StartNode", typeof(int)),
		("EndNode", typeof(int)),
		("Length", typeof(double))
	};

	private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string> { { "cellsize", "ConstantSize" } };

	private readonly Topology _topo;

	private readonly TopologyFieldSchema _schema;

	public string Name => "arc";

	public int Count => _topo.Arcs.Count;

	public IReadOnlyList<string> ProjectionFields => _schema.ProjectionFields;

	public IReadOnlyList<string> KnownFieldNames => _schema.KnownFieldNames;

	public ArcSelectorCollection(Topology topo)
	{
		_topo = topo;
		_schema = new TopologyFieldSchema(DerivedFields, topo.ArcMetadata, Aliases);
	}

	public bool TryGetField(string normalizedName, out Type type, out bool settable)
	{
		return _schema.TryGetField(normalizedName, out type, out settable);
	}

	public ISelectorElement GetElement(int index)
	{
		Arc arc = _topo.Arcs[index];
		Dictionary<string, object> derived = new Dictionary<string, object>(StringComparer.Ordinal)
		{
			{ "index", index },
			{ "startnode", arc.StartNode },
			{ "endnode", arc.EndNode },
			{
				"length",
				SafeLength(index)
			}
		};
		Table arcMetadata = _topo.ArcMetadata;
		TableRow tableRow = ((index < arcMetadata.Rows.Count) ? arcMetadata[index] : null);
		string writeUnavailableReason = ((tableRow == null) ? $"arc {index} has no metadata row." : null);
		return new TopologyElement(_schema, derived, tableRow, tableRow, writeUnavailableReason);
	}

	private object SafeLength(int index)
	{
		try
		{
			return _topo.GetArcPolylineLength(index);
		}
		catch
		{
			return null;
		}
	}
}
