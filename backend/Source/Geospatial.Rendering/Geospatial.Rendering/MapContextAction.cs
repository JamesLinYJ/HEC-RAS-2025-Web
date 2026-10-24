using System;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Utility.ContextActions;

namespace Geospatial.Rendering;

public class MapContextAction : ContextBase
{
	public Action<Point, RasterDefinition> MapAction { get; }

	public MapContextAction(string name, Action<Point, RasterDefinition> mapAction)
		: this(name, enabled: true, mapAction)
	{
	}

	public MapContextAction(string name, bool enabled, Action<Point, RasterDefinition> mapAction)
		: base(name, enabled)
	{
		MapAction = mapAction;
	}
}
