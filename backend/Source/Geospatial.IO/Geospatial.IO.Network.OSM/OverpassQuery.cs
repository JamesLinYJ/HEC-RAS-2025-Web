using System.Collections.Generic;
using System.Text;
using Geospatial.Vectors;

namespace Geospatial.IO.Network.OSM;

public class OverpassQuery
{
	public const int DefaultTimeoutSeconds = 180;

	public const string DefaultLabel = "Query";

	public List<OverpassClause> Clauses { get; } = new List<OverpassClause>();

	public string Label { get; set; } = "Query";

	public int TimeoutSeconds { get; set; } = 180;

	public bool TryBuildQl(Extent bboxWgs84, out string ql, out string error)
	{
		ql = null;
		error = null;
		if (!bboxWgs84.IsFiniteAndValid())
		{
			error = "The map extent is not a valid area to query.";
			return false;
		}
		return TryBuildQl(OverpassQl.FormatBoundingBox(bboxWgs84), out ql, out error);
	}

	public bool TryBuildTemplateQl(out string ql, out string error)
	{
		return TryBuildQl("{{bbox}}", out ql, out error);
	}

	private bool TryBuildQl(string bboxToken, out string ql, out string error)
	{
		ql = null;
		error = null;
		if (Clauses.Count == 0)
		{
			error = "Add at least one search clause before querying.";
			return false;
		}
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(24, 1, stringBuilder2);
		handler.AppendLiteral("[out:json][timeout:");
		handler.AppendFormatted(TimeoutSeconds);
		handler.AppendLiteral("];\n(\n");
		stringBuilder3.Append(ref handler);
		foreach (OverpassClause clause in Clauses)
		{
			if (clause.Kinds == OsmElementKinds.None)
			{
				error = "A search clause has no element types selected (node, way, or relation).";
				return false;
			}
			foreach (string kindToken in OverpassQl.GetKindTokens(clause.Kinds))
			{
				stringBuilder.Append("  ").Append(kindToken);
				foreach (OsmTagFilter filter in clause.Filters)
				{
					if (!filter.TryAppendQl(stringBuilder, out error))
					{
						return false;
					}
				}
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder4 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder2);
				handler.AppendLiteral("(");
				handler.AppendFormatted(bboxToken);
				handler.AppendLiteral(");\n");
				stringBuilder4.Append(ref handler);
			}
		}
		stringBuilder.Append(");\nout geom;");
		ql = stringBuilder.ToString();
		return true;
	}
}
