using System;
using System.Runtime.CompilerServices;
using H5Assist.Caching.VB;
using Utility;

namespace RasMapperLib.Render;

public class VelocityMC : MCRenderer
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__3_002D0
	{
		public float[] velMin;

		public float[] lambdaVelDelta;

		public float[] velMax;

		public _Closure_0024__3_002D0(_Closure_0024__3_002D0 other)
		{
			if (other != null)
			{
				velMin = other.velMin;
				lambdaVelDelta = other.lambdaVelDelta;
				velMax = other.velMax;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (velMin[i] != -9999f)
				{
					lambdaVelDelta[i] = velMax[i] - velMin[i];
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__4_002D0
	{
		public float[] velocities;

		public float[] lambdaMinVel;

		public float[] lambdaMaxVel;

		public _Closure_0024__4_002D0(_Closure_0024__4_002D0 other)
		{
			if (other != null)
			{
				velocities = other.velocities;
				lambdaMinVel = other.lambdaMinVel;
				lambdaMaxVel = other.lambdaMaxVel;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				float velocity = velocities[i];
				if (velocity != -9999f)
				{
					lambdaMinVel[i] = Math.Min(lambdaMinVel[i], velocity);
					lambdaMaxVel[i] = Math.Max(lambdaMaxVel[i], velocity);
				}
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (lambdaMinVel[i] == float.MaxValue)
				{
					lambdaMinVel[i] = -9999f;
					lambdaMaxVel[i] = -9999f;
				}
			}
		}
	}

	private WaterSurfaceMC _wsMC;

	public static void Compute(RASResults res, int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] velocityDeltas, CacheCollection cache = null)
	{
		new VelocityMC(res, cache).ComputeVelocityRange(profile, mps, terrain, ref velocityDeltas);
	}

	public VelocityMC(RASResults res, CacheCollection cache = null)
		: base(res, cache)
	{
		_wsMC = new WaterSurfaceMC(res, cache);
	}

	public void ComputeVelocityRange(int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] velocityDeltas)
	{
		_Closure_0024__3_002D0 capturedLocals = default;
		_Closure_0024__3_002D0 capturedLocals2 = new _Closure_0024__3_002D0(capturedLocals);
		Buffers.ArraySetAllVB(ref velocityDeltas, -9999f, mps.InputCount);
		capturedLocals2.lambdaVelDelta = velocityDeltas;
		capturedLocals2.velMin = null;
		capturedLocals2.velMax = null;
		ComputeVelocityRange(profile, mps, terrain, ref capturedLocals2.velMin, ref capturedLocals2.velMax);
		Parallel.SmartFor(mps.InputCount, [SpecialName] (int start, int finish) =>
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (capturedLocals2.velMin[i] != -9999f)
				{
					capturedLocals2.lambdaVelDelta[i] = capturedLocals2.velMax[i] - capturedLocals2.velMin[i];
				}
			}
		});
	}

	public void ComputeVelocityRange(int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] maxVelOccurred, ref float[] minVelOccurred)
	{
		_Closure_0024__4_002D0 capturedLocals = default;
		_Closure_0024__4_002D0 capturedLocals2 = new _Closure_0024__4_002D0(capturedLocals);
		capturedLocals2.velocities = null;
		capturedLocals2.lambdaMaxVel = maxVelOccurred;
		capturedLocals2.lambdaMinVel = minVelOccurred;
		Buffers.ArraySetAllVB(ref minVelOccurred, float.MaxValue, mps.InputCount);
		Buffers.ArraySetAllVB(ref maxVelOccurred, float.MinValue, mps.InputCount);
		checked
		{
			foreach (MCRun mcRun in _mcRuns)
			{
				_ = mcRun;
				RASResults result = _result;
				float[] outVelocityX = null;
				float[] outVelocityY = null;
				ref float[] outVelocityMag = ref capturedLocals2.velocities;
				float[] outDepths = null;
				VelocityRenderer.Compute(result, profile, mps, terrain, ref outVelocityX, ref outVelocityY, ref outVelocityMag, null, ref outDepths);
				Parallel.SmartFor(mps.InputCount, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						float velocity = capturedLocals2.velocities[i];
						if (velocity != -9999f)
						{
							capturedLocals2.lambdaMinVel[i] = Math.Min(capturedLocals2.lambdaMinVel[i], velocity);
							capturedLocals2.lambdaMaxVel[i] = Math.Max(capturedLocals2.lambdaMaxVel[i], velocity);
						}
					}
				});
			}
			Parallel.SmartFor(mps.InputCount, [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					if (capturedLocals2.lambdaMinVel[i] == float.MaxValue)
					{
						capturedLocals2.lambdaMinVel[i] = -9999f;
						capturedLocals2.lambdaMaxVel[i] = -9999f;
					}
				}
			});
		}
	}
}
