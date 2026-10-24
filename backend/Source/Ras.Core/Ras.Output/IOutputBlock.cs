using System.Collections.Generic;
using Geospatial;
using Ras.Layers;

namespace Ras.Output;

public interface IOutputBlock : IDiscreteTimestepsWithEvent, IDiscreteTimesteps
{
	string Name { get; }

	IList<string> ProfileNames { get; }

	int ProfileCount => ProfileNames.Count;

	Geometry Geometry { get; }
}
