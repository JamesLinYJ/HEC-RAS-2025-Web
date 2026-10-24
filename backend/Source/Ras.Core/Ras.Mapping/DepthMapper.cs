using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Geospatial.Vectors.Rasterizers;
using Ras.Layers.Results;
using Ras.Output;
using Utility.Memory;

namespace Ras.Mapping;

public class DepthMapper : Mapper
{
	private readonly WaterSurfaceMapper _wsr;

	public override MapTypes MapType { get; }

	public DepthMapper(IHorizontalOutputBlock block, MeshPropertyTables[] tables, MapperCache cache = null)
		: base(block, tables, cache)
	{
		_wsr = new WaterSurfaceMapper(block, tables, _mapperCache);
	}

	public override void Compute(int profile, GeometryMapPoints mps, Memory<float> clipTerrain, Memory<float> resultDepths)
	{
		if (mps.InputCount < 0)
		{
			throw new ArgumentException("mps");
		}
		if (resultDepths.Length != mps.InputCount)
		{
			throw new ArgumentException("resultDepths");
		}
		Memory<float> mem;
		using (SharedMemoryPool<float>.RentNoFill(mps.InputCount, out mem))
		{
			_wsr.Compute(profile, mps, RenderOptions.AllowNegativeDepths ? Memory<float>.Empty : clipTerrain, mem);
			SubtractTerrainAVX(mem, clipTerrain, resultDepths);
		}
	}

	public unsafe static void SubtractTerrainAVX(Memory<float> waterSurfaces, Memory<float> clipTerrain, Memory<float> depthResults)
	{
		int length = waterSurfaces.Length;
		if (clipTerrain.Length != length || depthResults.Length != length)
			throw new ArgumentException("Water-surface, terrain and depth buffers must have matching lengths.");
		if (!Avx.IsSupported)
		{
			ReadOnlySpan<float> surfaces = waterSurfaces.Span;
			ReadOnlySpan<float> terrain = clipTerrain.Span;
			Span<float> depths = depthResults.Span;
			for (int index = 0; index < length; index++)
				depths[index] = surfaces[index] == -9999f || terrain[index] == -9999f
					? -9999f : surfaces[index] - terrain[index];
			return;
		}
		fixed (float* waterSurfaces2 = waterSurfaces.Span)
		{
			fixed (float* clipTerrain2 = clipTerrain.Span)
			{
				fixed (float* depthResults2 = depthResults.Span)
				{
					float computed = -9999f;
					Vector256<float> vector = Avx.BroadcastScalarToVector256(&computed);
					int count = length / 8;
					int ordinal = 0;
					for (int i = 0; i < count; i++)
					{
						Vector256<float> left = Avx.LoadVector256(waterSurfaces2 + ordinal);
						Vector256<float> vector2 = Avx.LoadVector256(clipTerrain2 + ordinal);
						Vector256<float> mask = Avx.Or(right: Avx.CompareEqual(vector2, vector), left: Avx.CompareEqual(left, vector));
						Vector256<float> source = Avx.Subtract(left, vector2);
						Avx.Store(depthResults2 + ordinal, source);
						Avx.MaskStore(depthResults2 + ordinal, mask, vector);
						ordinal += 8;
					}
					for (int j = count * 8; j < length; j++)
					{
						if (clipTerrain2[j] == -9999f || waterSurfaces2[j] == -9999f)
						{
							depthResults2[j] = -9999f;
						}
						else
						{
							depthResults2[j] = waterSurfaces2[j] - clipTerrain2[j];
						}
					}
				}
			}
		}
	}

	public static float[] ComputeTimeSeries(PostProcessor pp, Point pt, float terrainElevation, RenderOptions renderOptions = null)
	{
		float[] timeSeries = WaterSurfaceMapper.ComputeTimeSeries(pp, pt, terrainElevation, renderOptions);
		SubtractToDepths(timeSeries, terrainElevation);
		return timeSeries;
	}

	public static void ComputeTimeSeries(PostProcessor pp, GeometryMapPoints mapPoints, Memory<float> terrain, int bandStart, int bandCount, Memory<float>[] output, RenderOptions renderOptions = null)
	{
		WaterSurfaceMapper.ComputeTimeSeries(pp, mapPoints, terrain, bandStart, bandCount, output, renderOptions);
		Span<float> terrainSpan = terrain.Span;
		for (int i = 0; i < output.Length; i++)
		{
			float terrain2 = terrainSpan[i];
			SubtractToDepths(output[i], terrain2);
		}
	}

	private static void SubtractToDepths(Memory<float> wsels, float terrain)
	{
		Span<float> wselsSpan = wsels.Span;
		for (int i = 0; i < wselsSpan.Length; i++)
		{
			float wsel = wselsSpan[i];
			if (wsel == -9999f || wsel == 0f)
			{
				wselsSpan[i] = 0f;
			}
			else
			{
				wselsSpan[i] = wsel - terrain;
			}
		}
	}
}
