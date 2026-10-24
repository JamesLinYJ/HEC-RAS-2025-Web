using System;

namespace Geospatial.IO.Network.OSM;

public sealed class OsmPreset
{
	private readonly Func<OverpassQuery> _createQuery;

	public string Name { get; }

	public string Description { get; }

	internal OsmPreset(string name, string description, Func<OverpassQuery> createQuery)
	{
		Name = name;
		Description = description;
		_createQuery = createQuery;
	}

	public OverpassQuery CreateQuery()
	{
		return _createQuery();
	}
}
