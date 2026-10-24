using System.Linq;
using System.Runtime.Versioning;
using Geospatial.Rasters;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Layers;

public static class TinRenderExtensions
{
	[SupportedOSPlatform("windows")]
	public static void DebugView(this Tin tin, int nPx = 1048576, Extent? constrainedExt = null)
	{
		Extent targetExtent = tin.Extent;
		if (constrainedExt.HasValue)
		{
			targetExtent = constrainedExt.Value;
		}
		RasterDefinition query = RasterDefinition.ComputeBoundingRaster(targetExtent, nPx);
		TinRenderLayer irl = new TinRenderLayer(tin);
		using PlotRequest plotRequest = new PlotRequest(query);
		irl.Render(plotRequest);
		plotRequest.DebugView();
	}

	[SupportedOSPlatform("windows")]
	public static void DebugViewPoint(this Tin tin, int pointIndex, int nPx = 1048576)
	{
		Point point = tin.Points[pointIndex];
		int t = 0;
		tin.FindTriangleAndNearestPointIndex(point, ref t, out var _);
		Extent union = Extent.Union((from tt in tin.TrianglesAroundPointCCW(pointIndex, t)
			select tin.Triangles[tt]).ToList());
		tin.DebugView(nPx, union);
	}
}
