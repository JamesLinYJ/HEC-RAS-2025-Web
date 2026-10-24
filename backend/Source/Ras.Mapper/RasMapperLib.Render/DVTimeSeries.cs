using System;
using H5Assist.Caching.VB;

namespace RasMapperLib.Render;

public class DVTimeSeries : Renderer
{

	public DVTimeSeries(RASResults res, CacheCollection cache = null)
		: base(res, cache, needsVelocityFlowData: true, needsPostProcessFile: true)
	{
		throw new NotImplementedException();
	}
}
