using System.Runtime.CompilerServices;
using H5Assist.Caching.VB;
using Utility;

namespace RasMapperLib.Render;

public class DepthTimeSeries : Renderer
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__3_002D0
	{
		public float[][] waterSurfaces;

		public float[][] lambdaDepth;

		public int nTimesteps;

		public float[] terrain;

		public DepthTimeSeries depthTimeSeries;

		public _Closure_0024__3_002D0(_Closure_0024__3_002D0 other)
		{
			if (other != null)
			{
				waterSurfaces = other.waterSurfaces;
				lambdaDepth = other.lambdaDepth;
				nTimesteps = other.nTimesteps;
				terrain = other.terrain;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			checked
			{
				for (int i = start; i <= finish; i++)
				{
					float[] waterSurface = waterSurfaces[i];
					float[] lambdaDepth = this.lambdaDepth[i];
					int count = nTimesteps - 1;
					for (int j = 0; j <= count; j++)
					{
						float waterSurface2 = waterSurface[j];
						float terrain = this.terrain[i];
						if (waterSurface2 != -9999f && terrain != -9999f && !(waterSurface2 < terrain + depthTimeSeries.MinWSPlotTolerance))
						{
							lambdaDepth[j] = waterSurface2 - terrain;
						}
					}
				}
			}
		}
	}

	private WaterSurfaceTimeSeries _wsTS;

	public static void Compute(RASResults res, RASGeometryMapPoints mps, float[] terrain, ref float[][] depths, CacheCollection cache = null)
	{
		new DepthTimeSeries(res, cache).Compute(mps, terrain, ref depths);
	}

	public DepthTimeSeries(RASResults res, CacheCollection cache = null)
		: base(res, cache, needsVelocityFlowData: false, needsPostProcessFile: true)
	{
		_wsTS = new WaterSurfaceTimeSeries(res, cache);
	}

	public void Compute(RASGeometryMapPoints mps, float[] terrain, ref float[][] depths)
	{
		_Closure_0024__3_002D0 capturedLocals = default;
		_Closure_0024__3_002D0 capturedLocals2 = new _Closure_0024__3_002D0(capturedLocals);
		capturedLocals2.depthTimeSeries = this;
		capturedLocals2.terrain = terrain;
		TimeSeriesInitialize(mps, -9999f, ref depths);
		capturedLocals2.waterSurfaces = null;
		_wsTS.Compute(mps, ref capturedLocals2.waterSurfaces);
		capturedLocals2.lambdaDepth = depths;
		capturedLocals2.nTimesteps = depths[0].Length;
		Parallel.SmartFor(depths.Length, checked([SpecialName] (int start, int finish) =>
		{
			for (int i = start; i <= finish; i++)
			{
				float[] waterSurface = capturedLocals2.waterSurfaces[i];
				float[] lambdaDepth = capturedLocals2.lambdaDepth[i];
				int count = capturedLocals2.nTimesteps - 1;
				for (int j = 0; j <= count; j++)
				{
					float computed = waterSurface[j];
					float terrain2 = capturedLocals2.terrain[i];
					if (computed != -9999f && terrain2 != -9999f && !(computed < terrain2 + capturedLocals2.depthTimeSeries.MinWSPlotTolerance))
					{
						lambdaDepth[j] = computed - terrain2;
					}
				}
			}
		}));
	}
}
