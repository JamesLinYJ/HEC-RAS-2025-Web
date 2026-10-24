using System.Collections.Generic;

namespace Geospatial.IO.Network.OSM;

public class OverpassClause
{
	public OsmElementKinds Kinds { get; set; } = OsmElementKinds.All;

	public List<OsmTagFilter> Filters { get; } = new List<OsmTagFilter>();

	public OverpassClause()
	{
	}

	public OverpassClause(OsmElementKinds kinds, params OsmTagFilter[] filters)
	{
		Kinds = kinds;
		if (filters != null)
		{
			Filters.AddRange(filters);
		}
	}
}
