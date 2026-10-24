using System;
using System.Runtime.CompilerServices;
using H5Assist.Caching.VB;
using Utility;

namespace RasMapperLib.Render;

public class WaterSurfaceMC : MCRenderer
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__1_002D0
	{
		public float[] waterSurfaces;

		public float[] lambdaMaxWS;

		public float[] lambdaMinWS;

		public _Closure_0024__1_002D0(_Closure_0024__1_002D0 other)
		{
			if (other != null)
			{
				waterSurfaces = other.waterSurfaces;
				lambdaMaxWS = other.lambdaMaxWS;
				lambdaMinWS = other.lambdaMinWS;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				float waterSurface = waterSurfaces[i];
				if (waterSurface != -9999f)
				{
					lambdaMaxWS[i] = Math.Max(lambdaMaxWS[i], waterSurface);
					lambdaMinWS[i] = Math.Min(lambdaMinWS[i], waterSurface);
				}
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (lambdaMinWS[i] == float.MaxValue)
				{
					lambdaMinWS[i] = -9999f;
					lambdaMaxWS[i] = -9999f;
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__2_002D0
	{
		public float[] wsMin;

		public float[] lambdaWSDelta;

		public float[] wsMax;

		public _Closure_0024__2_002D0(_Closure_0024__2_002D0 other)
		{
			if (other != null)
			{
				wsMin = other.wsMin;
				lambdaWSDelta = other.lambdaWSDelta;
				wsMax = other.wsMax;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (wsMin[i] != -9999f)
				{
					lambdaWSDelta[i] = wsMax[i] - wsMin[i];
				}
			}
		}
	}

	public WaterSurfaceMC(RASResults res, CacheCollection cache = null)
		: base(res, cache)
	{
	}

	public void ComputeWaterSurfaceRange(int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] minWSOccurred, ref float[] maxWSOccurred)
	{
		_Closure_0024__1_002D0 capturedLocals = default;
		_Closure_0024__1_002D0 capturedLocals2 = new _Closure_0024__1_002D0(capturedLocals);
		capturedLocals2.waterSurfaces = null;
		capturedLocals2.lambdaMinWS = minWSOccurred;
		capturedLocals2.lambdaMaxWS = maxWSOccurred;
		Buffers.ArraySetAllVB(ref minWSOccurred, float.MaxValue, mps.InputCount);
		Buffers.ArraySetAllVB(ref maxWSOccurred, float.MinValue, mps.InputCount);
		checked
		{
			foreach (MCRun mcRun in _mcRuns)
			{
				WaterSurfaceRenderer.Compute(mcRun.Result, profile, mps, ref capturedLocals2.waterSurfaces, terrain, _cache);
				Parallel.SmartFor(mps.InputCount, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						float waterSurface = capturedLocals2.waterSurfaces[i];
						if (waterSurface != -9999f)
						{
							capturedLocals2.lambdaMaxWS[i] = Math.Max(capturedLocals2.lambdaMaxWS[i], waterSurface);
							capturedLocals2.lambdaMinWS[i] = Math.Min(capturedLocals2.lambdaMinWS[i], waterSurface);
						}
					}
				});
			}
			Parallel.SmartFor(mps.InputCount, [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					if (capturedLocals2.lambdaMinWS[i] == float.MaxValue)
					{
						capturedLocals2.lambdaMinWS[i] = -9999f;
						capturedLocals2.lambdaMaxWS[i] = -9999f;
					}
				}
			});
		}
	}

	public void ComputeWaterSurfaceRange(int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] wsDelta)
	{
		_Closure_0024__2_002D0 capturedLocals = default;
		_Closure_0024__2_002D0 capturedLocals2 = new _Closure_0024__2_002D0(capturedLocals);
		Buffers.ArraySetAllVB(ref wsDelta, -9999f, mps.InputCount);
		capturedLocals2.lambdaWSDelta = wsDelta;
		capturedLocals2.wsMin = null;
		capturedLocals2.wsMax = null;
		ComputeWaterSurfaceRange(profile, mps, terrain, ref capturedLocals2.wsMin, ref capturedLocals2.wsMax);
		Parallel.SmartFor(mps.InputCount, [SpecialName] (int start, int finish) =>
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (capturedLocals2.wsMin[i] != -9999f)
				{
					capturedLocals2.lambdaWSDelta[i] = capturedLocals2.wsMax[i] - capturedLocals2.wsMin[i];
				}
			}
		});
	}
}
