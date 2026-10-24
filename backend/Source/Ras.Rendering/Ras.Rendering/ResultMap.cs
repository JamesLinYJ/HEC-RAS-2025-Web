using Geospatial.Rasters;
using Geospatial.Vectors.Rasterizers;
using Ras.Layers;
using Ras.Mapping;

namespace Ras.Rendering;

public class ResultMap
{
	public Result Result { get; }

	public MapTypes Type { get; }

	public IResample<float> Terrain { get; }

	public GenericMapType GenericMapType { get; }

	public ResultMap(Result result, MapTypes type, IResample<float> terrain)
	{
		Result = result;
		Type = type;
		Terrain = terrain;
	}

	public ResultMap(Result result, GenericMapType mt, IResample<float> terrain)
	{
		Result = result;
		Type = MapTypes.Generic;
		GenericMapType = mt;
		Terrain = terrain;
	}
}
