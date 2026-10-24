using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RasMapperLib;

[ComVisible(false)]
public interface IContainsControlPointsLayer
{
	int FeatureCount();

	PointOnPolyline NearestPointOnPolyline(int idx, PointM pt);

	float GetElevPtTolerance(int fid);

	float GetLayerValueAtPoint(PointM pt, bool useObstructions);

	Dictionary<int, PointM> GetElevationChangingPointOnAllPerimeters(int crtlPtFID);

	void ControlPointsChanged(object sender, FeatureChangedEventArgs args);
}
