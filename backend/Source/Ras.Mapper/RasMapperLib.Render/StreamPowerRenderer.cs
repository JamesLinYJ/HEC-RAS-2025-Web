using System.Linq;
using System.Runtime.CompilerServices;
using H5Assist.Caching.VB;
using Utility;

namespace RasMapperLib.Render;

public class StreamPowerRenderer : Renderer
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__4_002D0
	{
		public float[] vMag;

		public float[] shear;

		public float[] lOut;

		public _Closure_0024__4_002D0(_Closure_0024__4_002D0 other)
		{
			if (other != null)
			{
				vMag = other.vMag;
				shear = other.shear;
				lOut = other.lOut;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (vMag[i] != -9999f && shear[i] != -9999f)
				{
					lOut[i] = vMag[i] * shear[i];
				}
			}
		}
	}

	private VelocityRenderer _velR;

	private ShearRenderer _shearR;

	public static void Compute(RASResults res, int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] outStreamPower, CacheCollection cache = null)
	{
		new StreamPowerRenderer(res, cache).Compute(profile, mps, terrain, ref outStreamPower);
	}

	public StreamPowerRenderer(RASResults res, CacheCollection cache = null)
		: base(res, cache, needsVelocityFlowData: true, needsPostProcessFile: false)
	{
		_velR = new VelocityRenderer(res, cache);
		_shearR = new ShearRenderer(res, cache);
	}

	public void Compute(int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] outStreamPower)
	{
		_Closure_0024__4_002D0 capturedLocals = default;
		_Closure_0024__4_002D0 capturedLocals2 = new _Closure_0024__4_002D0(capturedLocals);
		Buffers.ArraySetAllVB(ref outStreamPower, -9999f, mps.InputCount);
		capturedLocals2.vMag = null;
		capturedLocals2.shear = null;
		VelocityRenderer velR = _velR;
		float[] outVelocityX = null;
		float[] outVelocityY = null;
		ref float[] outVelocityMag = ref capturedLocals2.vMag;
		float[] outDepths = null;
		velR.Compute(profile, mps, terrain, ref outVelocityX, ref outVelocityY, ref outVelocityMag, ref outDepths);
		_shearR.Compute(profile, mps, terrain, ref capturedLocals2.shear);
		capturedLocals2.lOut = outStreamPower;
		Parallel.SmartFor(capturedLocals2.vMag.Count(), [SpecialName] (int start, int finish) =>
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (capturedLocals2.vMag[i] != -9999f && capturedLocals2.shear[i] != -9999f)
				{
					capturedLocals2.lOut[i] = capturedLocals2.vMag[i] * capturedLocals2.shear[i];
				}
			}
		});
	}
}
