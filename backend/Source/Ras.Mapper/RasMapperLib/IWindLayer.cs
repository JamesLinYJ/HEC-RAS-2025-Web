using System;

namespace RasMapperLib;

public interface IWindLayer : IMeteorologyLayer
{
	bool GetWindTimeSeries(PointM pt, ref float[] magnitudes, ref float[] directions, ref DateTime[] times);
}
