using System.Linq;
using System.Runtime.CompilerServices;
using H5Assist.Caching.VB;
using Utility;

namespace RasMapperLib.Render;

public class DepthExtentRenderer
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__5_002D0
	{
		public float[] lambdaWS;

		public float[] terrain;

		public float[] lambdaDepth;

		public DepthExtentRenderer depthExtentRenderer;

		public _Closure_0024__5_002D0(_Closure_0024__5_002D0 other)
		{
			if (other != null)
			{
				lambdaWS = other.lambdaWS;
				terrain = other.terrain;
				lambdaDepth = other.lambdaDepth;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (lambdaWS[i] != -9999f && terrain[i] != -9999f)
				{
					lambdaDepth[i] = lambdaWS[i] - terrain[i];
				}
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				float lambdaWS = this.lambdaWS[i];
				float terrain = this.terrain[i];
				if (lambdaWS != -9999f && !(lambdaWS < terrain + depthExtentRenderer.MinWSPlotTolerance) && terrain != -9999f)
				{
					lambdaDepth[i] = lambdaWS - terrain;
				}
			}
		}
	}

	private WaterSurfaceRenderer _wsr;

	private float MinWSPlotTolerance;

	public static void Compute(RASResults res, int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] depths, CacheCollection cache = null, bool allowNegativeDepths = false)
	{
		new DepthExtentRenderer(res, cache).Compute(profile, mps, terrain, ref depths, allowNegativeDepths);
	}

	public DepthExtentRenderer(RASResults res, CacheCollection cache = null)
	{
		MinWSPlotTolerance = RASResults.MinWSPlotTolerance;
		_wsr = new WaterSurfaceRenderer(res, cache);
	}

	public float[] Compute(int profile, RASGeometryMapPoints mps, float[] terrain, bool allowNegativeDepths = false)
	{
		float[] depths = null;
		Compute(profile, mps, terrain, ref depths, allowNegativeDepths);
		return depths;
	}

	public void Compute(int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] depths, bool allowNegativeDepths = false)
	{
		_Closure_0024__5_002D0 capturedLocals = default;
		_Closure_0024__5_002D0 capturedLocals2 = new _Closure_0024__5_002D0(capturedLocals);
		capturedLocals2.depthExtentRenderer = this;
		capturedLocals2.terrain = terrain;
		float[] waterSurfaces = null;
		WaterSurfaceRenderer wsr = _wsr;
		float[] outputTimes = null;
		wsr.Compute(profile, mps, ref waterSurfaces, null, ref outputTimes);
		Buffers.ArraySetAllVB(ref depths, -9999f, mps.InputCount);
		capturedLocals2.lambdaWS = waterSurfaces;
		capturedLocals2.lambdaDepth = depths;
		checked
		{
			if (allowNegativeDepths)
			{
				Parallel.SmartFor(waterSurfaces.Count(), [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						if (capturedLocals2.lambdaWS[i] != -9999f && capturedLocals2.terrain[i] != -9999f)
						{
							capturedLocals2.lambdaDepth[i] = capturedLocals2.lambdaWS[i] - capturedLocals2.terrain[i];
						}
					}
				});
				return;
			}
			Parallel.SmartFor(waterSurfaces.Count(), [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					float lambdaWS = capturedLocals2.lambdaWS[i];
					float terrain2 = capturedLocals2.terrain[i];
					if (lambdaWS != -9999f && !(lambdaWS < terrain2 + capturedLocals2.depthExtentRenderer.MinWSPlotTolerance) && terrain2 != -9999f)
					{
						capturedLocals2.lambdaDepth[i] = lambdaWS - terrain2;
					}
				}
			});
		}
	}
}
