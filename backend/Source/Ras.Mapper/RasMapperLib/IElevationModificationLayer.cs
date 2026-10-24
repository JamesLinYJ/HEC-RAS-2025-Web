using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RasMapperLib;

[ComVisible(false)]
public interface IElevationModificationLayer : IFeatureLayer
{
	void ObstructRaster(RasterM raster, float[] values);

	void ObstructProfiles(IList<Polyline> polylines, IList<Profile> profiles);

	void ObstructPoints(IPoints points, float[] valuesPerPoint, SpatialIndex<int> pointSpIdx);
}
