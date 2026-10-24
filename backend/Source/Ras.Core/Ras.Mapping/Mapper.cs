using System;
using Geospatial.Vectors.Meshes;
using Geospatial.Vectors.Rasterizers;
using Ras.Layers;
using Ras.Output;

namespace Ras.Mapping;

public abstract class Mapper
{
	public const string OUTPUT_SIZE_ERROR = "Output buffer size does not match the number of input points.";

	public const string INPUT_SIZE_ERROR = "Input points <= 0";

	protected IHorizontalOutputBlock _block;

	protected Geometry _geometry;

	protected MapperCache _mapperCache;

	public MapperCache MapperCache => _mapperCache;

	public RenderOptions RenderOptions { get; set; } = new RenderOptions();

	public abstract MapTypes MapType { get; }

	public IHorizontalOutputBlock OutputBlock => _block;

	public static Mapper CreateMapper(MapTypes mt, IHorizontalOutputBlock ob, MeshPropertyTables[] tables, MapperCache cache = null)
	{
		return mt switch
		{
			MapTypes.WaterSurfaceElevation => (Mapper)new WaterSurfaceMapper(ob, tables, cache), 
			MapTypes.Depth => new DepthMapper(ob, tables, cache), 
			MapTypes.Velocity => new VelocityMapper(ob, tables, cache), 
			MapTypes.DepthTimesVelocity => new DepthVelocityMapper(ob, tables, cache), 
			MapTypes.DepthTimesVelocitySquared => new DepthVelocitySquaredMapper(ob, tables, cache),
			_ => throw new NotImplementedException(), 
		};
	}

	protected Mapper(IHorizontalOutputBlock block, MeshPropertyTables[] tables, MapperCache cache = null)
	{
		_block = block;
		_geometry = _block.Geometry;
		_mapperCache = cache ?? new MapperCache(block, tables);
	}

	public abstract void Compute(int pfIdx, GeometryMapPoints mapPts, Memory<float> terrain, Memory<float> output);

	protected void ValidateProfileIndex(int pfIndex)
	{
		if (pfIndex == int.MaxValue || pfIndex == int.MinValue || (pfIndex >= 0 && pfIndex < _block.ProfileCount))
		{
			return;
		}
		throw new Exception("Invalid profile index: " + pfIndex);
	}
}
