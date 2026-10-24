using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Geospatial.PairedData;
using Geospatial.Rasters;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Ras.Hydraulics;
using Ras.Hydraulics.Classic;
using Ras.Layers;
using Ras.Layers.Classic;
using Ras.Mapping;
using Utility.DataStructures;

namespace Ras.Rendering;

public static class DefaultRenderLayers
{
	public static List<IRenderLayer> From(Geometry geom, bool createLayerIfEmpty = true)
	{
		List<IRenderLayer> iRenderLayers = new List<IRenderLayer>();
		if (createLayerIfEmpty || geom.FlowAreaLayer.Count > 0)
		{
			iRenderLayers.Add(From(geom.FlowAreaLayer));
		}
		return iRenderLayers;
	}

	public static IRenderLayer From(IGeometryLayer lyr)
	{
		if (lyr == null)
		{
			return null;
		}
		return new PolylineRenderLayer
		{
			VectorSymbology = 
			{
				PenColor = Color.Blue,
				PenWidth = 4.0
			}
		};
	}

	public static IRenderLayer From(RiverLayer riv)
	{
		if (riv == null)
		{
			return null;
		}
		PolylineRenderLayer polylineRenderLayer = new PolylineRenderLayer(new TranslatedList<River, Polyline>(riv, (River r) => r.Polyline));
		polylineRenderLayer.VectorSymbology.PenColor = Color.Blue;
		polylineRenderLayer.VectorSymbology.PenWidth = 4.0;
		polylineRenderLayer.AddHoverHighlight((int fid) => riv[fid].RiverName + " - " + riv[fid].ReachName);
		return polylineRenderLayer;
	}

	public static IRenderLayer From(CrossSectionLayer lyr)
	{
		if (lyr == null)
		{
			return null;
		}
		PolylineRenderLayer polylineRenderLayer = new PolylineRenderLayer(new TranslatedList<CrossSection, Polyline>(lyr, (CrossSection r) => r.Polyline));
		polylineRenderLayer.VectorSymbology.PenColor = Color.LightGreen;
		polylineRenderLayer.VectorSymbology.PenWidth = 2.0;
		polylineRenderLayer.AddHoverHighlight((int fid) => "XS: " + lyr[fid].NodeName);
		return polylineRenderLayer;
	}

	public static IRenderLayer From(StorageAreaLayer lyr)
	{
		if (lyr == null)
		{
			return null;
		}
		PolygonRenderLayer polygonRenderLayer = new PolygonRenderLayer(lyr.Select((StorageArea s) => s.Polygon));
		polygonRenderLayer.VectorSymbology.FillColor = Color.FromArgb(100, 0, 0, 255);
		polygonRenderLayer.AddHoverHighlight((int fid) => lyr[fid].Name);
		return polygonRenderLayer;
	}

	public static IRenderLayer From(FlowAreaLayer lyr)
	{
		if (lyr == null)
		{
			return null;
		}
		MeshRenderLayer meshRenderLayer = new MeshRenderLayer(lyr.Select((FlowArea m) => m.Mesh));
		meshRenderLayer.AddDefaultLabelHoverAction();
		return meshRenderLayer;
	}

	public static IRenderLayer From(BreaklineLayer lyr)
	{
		if (lyr == null)
		{
			return null;
		}
		return new PolylineRenderLayer(new TranslatedList<Breakline, Polyline>(lyr, (Breakline cs) => cs.Polyline))
		{
			VectorSymbology = 
			{
				PenColor = Color.RosyBrown,
				PenWidth = 3.0
			}
		};
	}

	public static IRenderLayer From(BanklineLayer lyr)
	{
		if (lyr == null)
		{
			return null;
		}
		return new PolylineRenderLayer(new TranslatedList<Bankline, Polyline>(lyr, (Bankline cs) => cs.Polyline))
		{
			VectorSymbology = 
			{
				PenColor = Color.Red,
				PenWidth = 3.0
			}
		};
	}

	public static IRenderLayer From(RiverEdgelineLayer lyr)
	{
		if (lyr == null)
		{
			return null;
		}
		return new PolylineRenderLayer(new TranslatedList<Edgeline, Polyline>(lyr, (Edgeline cs) => cs.Polyline))
		{
			VectorSymbology = 
			{
				PenColor = Color.Black,
				PenWidth = 2.0
			}
		};
	}

	public static IRenderLayer From(FlowPathLayer lyr)
	{
		if (lyr == null)
		{
			return null;
		}
		return new PolylineRenderLayer(new TranslatedList<FlowPathLine, Polyline>(lyr, (FlowPathLine cs) => cs.Polyline))
		{
			VectorSymbology = 
			{
				PenColor = Color.Coral,
				PenWidth = 2.0
			}
		};
	}

	public static IRenderLayer From(ClassicStructureLayer lyr)
	{
		if (lyr == null || lyr.Count == 0)
		{
			return null;
		}
		PolylineRenderLayer polylineRenderLayer = new PolylineRenderLayer(new TranslatedList<Structure, Polyline>(lyr, (Structure cs) => cs.Polyline));
		polylineRenderLayer.VectorSymbology.PenColor = Color.Gray;
		polylineRenderLayer.VectorSymbology.PenWidth = 3.0;
		polylineRenderLayer.AddHoverHighlight((int fid) => lyr[fid].ID.ConnectionName);
		return polylineRenderLayer;
	}

	public static List<IRenderLayer> From(Result res, IResample<float> terrRenderer, bool withGeometry = true)
	{
		List<IRenderLayer> iRenderLayers = From(res.Geometry);
		if (!withGeometry)
		{
			iRenderLayers.Clear();
		}
		iRenderLayers.Add(MakeMap(res, MapTypes.WaterSurfaceElevation, terrRenderer));
		return iRenderLayers;
	}

	public static ResultMapRenderLayer MakeMap(Result res, MapTypes mt, IResample<float> terrain)
	{
		ResultMapRenderLayer resultMapRenderLayer = new ResultMapRenderLayer(res.BaseOutputBlock, mt, terrain, res.Geometry.FlowAreaLayer.AllPropertyTables());
		resultMapRenderLayer.SetDefaultSymbology();
		return resultMapRenderLayer;
	}

	public static IRenderLayer FromTerrainXSIS(ClassicGeometry geom, RasterSymbology terrainSymb)
	{
		InterpolationSurfaceLayer interpLayer = geom.InterpolationSurfaceLayer;
		return new MultiTinRenderLayer(interpLayer.Select((InterpolationSurface t) => t.Tin).ToList())
		{
			RenderEdges = false,
			RenderPoints = false,
			SurfaceFillMode = TinSurfaceFillMode.RenderCustomZM,
			CustomTinRenderFunction = (int tinIdx, float z, float m) =>
			{
				if (m < -1f || m > 1f)
				{
					return -9999f;
				}
				InterpolationSurface interpolationSurface = interpLayer[tinIdx];
				CrossSection crossSection = geom.CrossSectionLayer[interpolationSurface.USXS];
				CrossSection crossSection2 = geom.CrossSectionLayer[interpolationSurface.DSXS];
				StationElevationProfile overrideStationElevation = crossSection.OverrideStationElevation;
				StationElevationProfile overrideStationElevation2 = crossSection2.OverrideStationElevation;
				if (overrideStationElevation == null || overrideStationElevation2 == null)
				{
					return -9999f;
				}
				double mValueToStation = crossSection.MValueToStation(m);
				double mValueToStation2 = crossSection2.MValueToStation(m);
				double sample = overrideStationElevation.Sample(mValueToStation);
				double sample2 = overrideStationElevation2.Sample(mValueToStation2);
				if (mValueToStation > overrideStationElevation.Stations.Last())
				{
					sample = overrideStationElevation.Values.Last();
				}
				if (mValueToStation2 > overrideStationElevation2.Stations.Last())
				{
					sample2 = overrideStationElevation2.Values.Last();
				}
				return (float)(sample * (double)z + sample2 * (1.0 - (double)z));
			},
			RasterSymbology = terrainSymb
		};
	}

	public static IRenderLayer From(InterpolationSurfaceLayer isl)
	{
		MultiTinRenderLayer multiTinRenderLayer = new MultiTinRenderLayer((from xsis in isl
			select xsis.Tin into t
			where t != null
			select t).ToList());
		multiTinRenderLayer.RenderEdges = true;
		multiTinRenderLayer.RenderPoints = false;
		multiTinRenderLayer.SurfaceFillMode = TinSurfaceFillMode.None;
		Color[] colors = new Color[3]
		{
			Color.Blue,
			Color.White,
			Color.Red
		};
		double[] values = new double[3] { -2.0, 0.0, 2.0 };
		multiTinRenderLayer.RasterSymbology = new RasterSymbology(new InterpolatedSurfaceFill(colors, values));
		multiTinRenderLayer.VectorSymbology = new VectorSymbology(Color.Black, 1.0, Color.Black);
		multiTinRenderLayer.VectorSymbology.PointSize = 2;
		return multiTinRenderLayer;
	}

	public static IRenderLayer Merge(params IRenderLayer[] layers)
	{
		return new RenderLayerCollection(layers);
	}

	public static RasterRenderLayer From(Terrain terrain, bool dbgBaseTileOutlines = false, bool dbgBaseCellOutlines = false)
	{
		Histogram histogram = terrain.ComputeHistogram(1048576, 256);
		RasterSymbology rasterSymbology = new RasterSymbology(new InterpolatedSurfaceFill(ColorPalettes.Terrain, histogram));
		rasterSymbology.HillshadeProperties.IsEnabled = true;
		rasterSymbology.HillshadeProperties.ZFactor = 3f;
		return new RasterRenderLayer(terrain, rasterSymbology);
	}

	public static RasterRenderLayer From(TerrainV6 terrain, bool dbgBaseTileOutlines = false, bool dbgBaseCellOutlines = false)
	{
		Histogram histogram = terrain.ComputeHistogram(1048576, 256);
		RasterSymbology rasterSymbology = new RasterSymbology(new InterpolatedSurfaceFill(ColorPalettes.Terrain, histogram));
		rasterSymbology.HillshadeProperties.IsEnabled = true;
		rasterSymbology.HillshadeProperties.ZFactor = 3f;
		return new RasterRenderLayer(terrain, rasterSymbology);
	}
}
