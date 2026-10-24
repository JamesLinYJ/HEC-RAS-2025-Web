using H5Assist.Caching.VB;

namespace RasMapperLib.Render;

public class DVRenderer : DepthAndVelocityRenderer
{
	public static void Compute(RASResults res, int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] outDepthTimesVelocity, CacheCollection cache = null)
	{
		new DVRenderer(res, cache).Compute(profile, mps, terrain, ref outDepthTimesVelocity);
	}

	public DVRenderer(RASResults res, CacheCollection cache = null)
		: base(res, cache, DepthVelocityMethod.DepthTimesVelocity)
	{
	}
}
