using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using H5Assist.Caching.VB;
using Utility;

namespace RasMapperLib.Render;

public class ArrivalTimeMC : MCRenderer
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__2_002D0
	{
		public float[] localAT;

		public float[] lambdaMaxAT;

		public float[] lambdaMinAT;

		public float[] maxAT;

		public float[] terrain;

		public float[] lambdaAT;

		public float[] minAT;

		public _Closure_0024__2_002D0(_Closure_0024__2_002D0 other)
		{
			if (other != null)
			{
				localAT = other.localAT;
				lambdaMaxAT = other.lambdaMaxAT;
				lambdaMinAT = other.lambdaMinAT;
				maxAT = other.maxAT;
				terrain = other.terrain;
				lambdaAT = other.lambdaAT;
				minAT = other.minAT;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int n)
		{
			float localAT = this.localAT[n];
			if (localAT != -9999f)
			{
				lambdaMaxAT[n] = Math.Max(lambdaMaxAT[n], localAT);
				lambdaMinAT[n] = Math.Min(lambdaMinAT[n], localAT);
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int n)
		{
			if (maxAT[n] != float.MinValue && terrain[n] != -9999f)
			{
				lambdaAT[n] = maxAT[n] - minAT[n];
			}
		}
	}

	public static void ComputeArrivalTimeRange(RASResults res, RASGeometryMapPoints mps, float[] terrain, float depthThreshold, bool useHours, string startProfile, ref float[] arrivalTimeDeltas, CacheCollection cache = null)
	{
		new ArrivalTimeMC(res, cache).ComputeArrivalTimeRange(mps, terrain, depthThreshold, useHours, startProfile, ref arrivalTimeDeltas);
	}

	public ArrivalTimeMC(RASResults res, CacheCollection cache = null)
		: base(res, cache)
	{
	}

	public void ComputeArrivalTimeRange(RASGeometryMapPoints mps, float[] terrain, float depthThreshold, bool useHours, string startProfile, ref float[] arrivalTimeDeltas)
	{
		_Closure_0024__2_002D0 capturedLocals = default;
		_Closure_0024__2_002D0 capturedLocals2 = new _Closure_0024__2_002D0(capturedLocals);
		capturedLocals2.terrain = terrain;
		capturedLocals2.localAT = null;
		capturedLocals2.maxAT = null;
		capturedLocals2.minAT = null;
		Buffers.ArraySetAllVB(ref arrivalTimeDeltas, -9999f, mps.InputCount);
		Buffers.ArraySetAllVB(ref capturedLocals2.maxAT, float.MinValue, mps.InputCount);
		Buffers.ArraySetAllVB(ref capturedLocals2.minAT, float.MaxValue, mps.InputCount);
		capturedLocals2.lambdaMaxAT = capturedLocals2.maxAT;
		capturedLocals2.lambdaMinAT = capturedLocals2.minAT;
		foreach (MCRun mcRun in _mcRuns)
		{
			_postProcessor.EnsureChildPostProcess(mcRun.OutputFile);
			new CacheCollection();
			System.Threading.Tasks.Parallel.For(0, capturedLocals2.localAT.Count(), [SpecialName] (int n) =>
			{
				float localAT = capturedLocals2.localAT[n];
				if (localAT != -9999f)
				{
					capturedLocals2.lambdaMaxAT[n] = Math.Max(capturedLocals2.lambdaMaxAT[n], localAT);
					capturedLocals2.lambdaMinAT[n] = Math.Min(capturedLocals2.lambdaMinAT[n], localAT);
				}
			});
		}
		capturedLocals2.lambdaAT = arrivalTimeDeltas;
		System.Threading.Tasks.Parallel.For(0, capturedLocals2.lambdaAT.Count(), [SpecialName] (int n) =>
		{
			if (capturedLocals2.maxAT[n] != float.MinValue && capturedLocals2.terrain[n] != -9999f)
			{
				capturedLocals2.lambdaAT[n] = capturedLocals2.maxAT[n] - capturedLocals2.minAT[n];
			}
		});
		throw new NotImplementedException();
	}
}
