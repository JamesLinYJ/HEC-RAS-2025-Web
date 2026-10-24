using System;

namespace Geospatial.Vectors.Topology;

public record TableDefault(string Name, Type Type, object DefaultValue)
{
	public static implicit operator string(TableDefault tableDefault)
	{
		return tableDefault.Name;
	}
}
