using H5Assist.Caching.VB;

namespace RasMapperLib.Render;

public class DVSqRenderer : DepthAndVelocityRenderer
{
	public static void Compute(RASResults res, int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] outDepthTimesVelocitySquared, CacheCollection cache = null)
	{
		new DVSqRenderer(res, cache).Compute(profile, mps, terrain, ref outDepthTimesVelocitySquared);
	}

	public DVSqRenderer(RASResults res, CacheCollection cache = null)
		: base(res, cache, DepthVelocityMethod.DepthTimesVelocitySquared)
	{
	}
}
