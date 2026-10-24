using H5Assist.Caching.VB;

namespace RasMapperLib.Render;

public class DepthMC : MCRenderer
{
	private WaterSurfaceMC _wsMC;

	public static void ComputeDepthRange(RASResults res, int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] depthDeltas, CacheCollection cache = null)
	{
		new DepthMC(res, cache).ComputeDepthRange(profile, mps, terrain, ref depthDeltas);
	}

	public DepthMC(RASResults res, CacheCollection cache = null)
		: base(res, cache)
	{
		_wsMC = new WaterSurfaceMC(res, cache);
	}

	public void ComputeDepthRange(int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] depthDeltas)
	{
		_wsMC.ComputeWaterSurfaceRange(profile, mps, terrain, ref depthDeltas);
	}
}
