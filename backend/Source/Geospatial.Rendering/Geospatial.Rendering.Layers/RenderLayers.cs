using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Geospatial.Rasters;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Layers;

public static class RenderLayers
{
	public static IRenderLayer From(Mesh mesh)
	{
		if (mesh == null)
		{
			return null;
		}
		PolygonRenderLayer polygonRenderLayer = new PolygonRenderLayer(new Polygon[1] { mesh.Perimeter });
		polygonRenderLayer.VectorSymbology.FillColor = Color.FromArgb(75, 0, 255, 0);
		PolylineRenderLayer polylineRenderLayer = new PolylineRenderLayer(from idx in Enumerable.Range(0, mesh.FaceCount)
			select mesh.FacePolyline(idx));
		polylineRenderLayer.VectorSymbology.PenWidth = 1.0;
		polylineRenderLayer.VectorSymbology.PenColor = Color.Black;
		return new RenderLayerCollection(polygonRenderLayer, polylineRenderLayer);
	}

	public static PolygonRenderLayer From(Polygon pgon, bool fill)
	{
		if (pgon == null)
		{
			return null;
		}
		return new PolygonRenderLayer(new Polygon[1] { pgon })
		{
			RenderFill = fill,
			RenderPerimeter = true,
			VectorSymbology = 
			{
				FillColor = Color.FromArgb(75, 50, 255, 0),
				PenWidth = 1.0,
				PenColor = Color.Black
			}
		};
	}

	public static PointRenderLayer From(IList<Geospatial.Vectors.Point> points)
	{
		if (points == null)
		{
			return null;
		}
		return new PointRenderLayer(points)
		{
			VectorSymbology = 
			{
				PointSize = 5,
				FillColor = Color.Black
			}
		};
	}

	public static TinRenderLayer From(Tin tin)
	{
		if (tin == null)
		{
			return null;
		}
		return new TinRenderLayer(tin)
		{
			VectorSymbology = 
			{
				PenColor = Color.Red,
				PointSize = 5,
				FillColor = Color.Black
			}
		};
	}

	public static RenderLayer From(IResample<float> resampler, InterpolatedSurfaceFill sfIll)
	{
		if (resampler == null)
		{
			return null;
		}
		return new RasterRenderLayer(resampler, new RasterSymbology(sfIll));
	}

	public static TimeSeriesRenderLayer From(ITimeSeriesResampler<float> resampler, InterpolatedSurfaceFill sFill)
	{
		if (resampler == null)
		{
			return null;
		}
		return new TimeSeriesRenderLayer(resampler, new RasterSymbology(sFill));
	}
}
