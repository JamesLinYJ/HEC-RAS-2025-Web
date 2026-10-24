using System.Collections.Generic;
using System.Drawing;
using Geospatial.DataStructures;
using Geospatial.PairedData;
using Geospatial.Rasters;
using Geospatial.Rasters.Modifiers;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Layers.Attributes;
using Geospatial.Rendering.Systems;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;
using Ras.Layers.Terrains;

namespace Ras.Rendering;

public class LeveeModRenderLayer : PolylineRenderLayer
{
	private readonly LeveeModTerrainInput _levees;

	private bool _showProfileStations;

	[PlotOption("Show Profile Stations")]
	public bool ShowProfileStations
	{
		get
		{
			return _showProfileStations;
		}
		set
		{
			if (_showProfileStations != value)
			{
				_showProfileStations = value;
				IncrementRenderState();
			}
		}
	}

	public VectorSymbology StationSymbology { get; set; } = new VectorSymbology(Color.OrangeRed, 1.0, Color.OrangeRed)
	{
		PointSize = 5
	};

	private int MarkerRadiusCells => (int)((double)StationSymbology.PointSize * VectorManagerOptions.RenderScaling) + (int)(StationSymbology.PenWidth * VectorManagerOptions.RenderScaling) / 2;

	public LeveeModRenderLayer(IPolylineCollection polylines, LeveeModTerrainInput levees)
		: base(polylines)
	{
		_levees = levees;
	}

	protected override bool RenderInternal(PlotRequest request, ref object cache)
	{
		bool renderInternal = base.RenderInternal(request, ref cache);
		if (ShowProfileStations)
		{
			renderInternal |= RenderProfileStations(request);
		}
		return renderInternal;
	}

	private bool RenderProfileStations(PlotRequest request)
	{
		if (_levees == null || _levees.Count == 0)
		{
			return false;
		}
		SpatialIndex<int> spatialIndex = EnsureGetSpatialIndex();
		if (spatialIndex == null)
		{
			return false;
		}
		RasterDefinition raster = request.RasterQuery.ExtendRaster(MarkerRadiusCells);
		List<int> elements = spatialIndex.GetElements(raster, dedupe: false);
		if (elements.Count == 0)
		{
			return false;
		}
		List<Geospatial.Vectors.Point> points = new List<Geospatial.Vectors.Point>();
		HashSet<int> intSet = new HashSet<int>();
		foreach (int element in elements)
		{
			if (!intSet.Add(element) || element < 0 || element >= _levees.Count)
			{
				continue;
			}
			LeveeMod leveeMod = _levees[element];
			Polyline polyline = leveeMod?.Line;
			StationElevationProfile stationElevationProfile = leveeMod?.Profile;
			if (!Polyline.IsValidPolyline(polyline) || stationElevationProfile == null || stationElevationProfile.Count == 0 || !raster.Extent.Overlaps(polyline.Extent))
			{
				continue;
			}
			double[] cumulativeSegmentLengths = polyline.CumulativeSegmentLengths();
			for (int i = 0; i < stationElevationProfile.Count; i++)
			{
				PointOnPolyline pointOnPolyline = polyline.PointAtDistanceFast(stationElevationProfile.Stations[i], cumulativeSegmentLengths);
				if (!pointOnPolyline.IsNull())
				{
					Geospatial.Vectors.Point point = pointOnPolyline.Point;
					points.Add(point);
					if (request.RasterQuery.Contains(point))
					{
						request.AddLabel(new LazyLabelDefinition<double>(PolylineRenderLayer.FormatVertexLabel, stationElevationProfile.Elevations[i], point));
					}
				}
			}
		}
		if (points.Count == 0)
		{
			return false;
		}
		return PointRenderLayer.Paint(points, StationSymbology, request);
	}

	protected override long GetInternalRenderState()
	{
		return base.GetInternalRenderState() + (_levees?.VersionXY ?? 0) + StationSymbology.VersionXY;
	}
}
