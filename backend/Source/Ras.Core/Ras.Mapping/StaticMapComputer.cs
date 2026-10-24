using System;
using Geospatial.Rasters;
using Ras.Layers;
using Utility.Memory;

namespace Ras.Mapping;

public class StaticMapComputer : IStaticMapComputer
{
	private readonly Func<float, float, float> _accumulator;

	private readonly Mapper _mapper;

	public Geometry Geometry { get; }

	public IResample<float> Terrain { get; }

	public int ProfileCount => _mapper.OutputBlock.ProfileCount;

	public StaticMapComputer(Geometry geometry, IResample<float> terrain, Mapper mapper, Func<float, float, float> accumulator)
	{
		Geometry = geometry;
		Terrain = terrain;
		_mapper = mapper;
		_accumulator = accumulator;
	}

	public void AccumulateStaticMap(int pfIdx, StaticCache staticCache, Memory<float> output)
	{
		Span<float> outputSpan = output.Span;
		GeometryMapPoints geometry = staticCache.Geometry;
		Memory<float> mem;
		using (SharedMemoryPool<float>.Rent(geometry.InputCount, -9999f, out mem))
		{
			_mapper.Compute(pfIdx, geometry, staticCache.Terrain, mem);
			Span<float> memSpan = mem.Span;
			for (int i = 0; i < geometry.InputCount; i++)
			{
				float mem2 = memSpan[i];
				float output2 = outputSpan[i];
				outputSpan[i] = _accumulator(mem2, output2);
			}
		}
	}

	public StaticCache GetStaticCache(RasterDefinition rasterDefinition)
	{
		return new StaticCache(Geometry, Terrain, rasterDefinition);
	}
}
