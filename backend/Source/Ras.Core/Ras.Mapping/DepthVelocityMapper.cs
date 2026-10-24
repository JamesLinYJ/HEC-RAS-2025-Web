using System;
using Geospatial.Vectors.Meshes;
using Ras.Output;
using Utility.Memory;

namespace Ras.Mapping;

public class DepthVelocityMapper : Mapper
{
	private readonly VelocityMapper _velocities;

	public override MapTypes MapType => MapTypes.DepthTimesVelocity;

	public DepthVelocityMapper(IHorizontalOutputBlock block, MeshPropertyTables[] tables, MapperCache cache = null)
		: base(block, tables, cache)
	{
		_velocities = new VelocityMapper(block, tables, cache);
	}

	public override void Compute(int pfIdx, GeometryMapPoints mapPts, Memory<float> terrain, Memory<float> output)
	{
		if (mapPts.InputCount < 0)
		{
			throw new ArgumentException("Input points <= 0");
		}
		if (output.Length != mapPts.InputCount)
		{
			throw new ArgumentException("Output buffer size does not match the number of input points.");
		}
		Memory<float> mem;
		using (SharedMemoryPool<float>.RentNoFill(mapPts.InputCount, out mem))
		{
			_velocities.Compute(pfIdx, mapPts, terrain, mem, output);
			Span<float> outputSpan = output.Span;
			Span<float> memSpan = mem.Span;
			int inputCount = mapPts.InputCount;
			for (int i = 0; i < inputCount; i++)
			{
				float output2 = outputSpan[i];
				float mem2 = memSpan[i];
				if (output2 != -9999f && mem2 != -9999f)
				{
					outputSpan[i] = output2 * mem2;
				}
			}
		}
	}
}
