using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Geospatial.Rasters;
using Geospatial.Rendering.Layers.Attributes;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Interfaces;
using Utility.Logging;

namespace Geospatial.Rendering.Layers;

public class RasterRenderLayer : RenderLayer, IRasterRenderLayer, IRenderLayer, IDisplayName, IReadOnlyDisplayName
{
	private static readonly ILogger _logger = LogHelper.GetLogger("RasterRenderLayer");

	private IResample<float> _raster;

	private readonly Versioned<ContourLayerStats> _contourStatsCache = new Versioned<ContourLayerStats>();

	public bool RegisterDataOnPlotRequest = true;

	private bool _plotRasterTileOutlines;

	private VersionTracker _symbologyVersionTracker;

	private RasterSymbology _rasterSymb = RasterSymbology.DefaultRasterSymbology();

	public IResample<float> Raster => _raster;

	public ContourLayerStats ContourStats => _contourStatsCache.Get((_raster as ISpatialVersion)?.VersionXY ?? 0, _raster, (IResample<float> r) => ContourLayerStatistics.Compute(r));

	[PlotOption("ResampleMode", "Resample Mode")]
	public InterpolationMode ResampleMode
	{
		get
		{
			return RasterSymbology.InterpolationMode;
		}
		set
		{
			if (RasterSymbology.InterpolationMode != value)
			{
				RasterSymbology.InterpolationMode = value;
				IncrementRenderState();
			}
		}
	}

	[PlotOption("ShowTiling", "Plot Raster Tile Outlines")]
	public bool PlotRasterTileOutlines
	{
		get
		{
			return _plotRasterTileOutlines;
		}
		set
		{
			if (_plotRasterTileOutlines != value)
			{
				_plotRasterTileOutlines = value;
				IncrementRenderState();
			}
		}
	}

	public override Extent ValidExtent => _raster.ValidExtent;

	public RasterSymbology RasterSymbology
	{
		get
		{
			return _rasterSymb;
		}
		set
		{
			_symbologyVersionTracker.Assign(ref _rasterSymb, value);
		}
	}

	public RasterRenderLayer(IResample<float> raster)
	{
		if (raster == null)
		{
			throw new ArgumentNullException("raster");
		}
		_raster = raster;
	}

	public RasterRenderLayer(IResample<float> raster, RasterSymbology symbology)
	{
		if (raster == null)
		{
			throw new ArgumentNullException("raster");
		}
		_raster = raster;
		RasterSymbology = symbology;
	}

	public RasterSymbology GetDefaultTerrainHistoSymbology(RasterLayerSymbologyOptions? opts = null)
	{
		if (!opts.HasValue)
		{
			opts = RasterLayerSymbologyOptions.Default();
		}
		Histogram histogram = Histogram.ComputeTiled(_raster, 1048576, 256);
		if (histogram.IsNull())
		{
			_logger.LogWarning("SetDefaultTerrainHistoSymbology {LayerName} Null histogram - possibly bad resample data (NaN/Inf/NoData)", "Layer Name Redacted");
			return new RasterSymbology();
		}
		if (histogram.IsSingleValue())
		{
			return new RasterSymbology();
		}
		return new RasterSymbology(new InterpolatedSurfaceFill(ColorPalettes.Terrain, histogram))
		{
			HillshadeProperties = 
			{
				IsEnabled = true,
				ZFactor = 3f
			}
		};
	}

	public void SetDefaultTerrainHistoSymbology(RasterLayerSymbologyOptions? opts = null)
	{
		RasterSymbology = GetDefaultTerrainHistoSymbology(opts);
	}

	public void SetGrayscaleLinearSymbology(double min, double max)
	{
		RasterSymbology rasterSymbology = new RasterSymbology(new InterpolatedSurfaceFill(ColorPalettes.Grayscale, new double[2] { min, max }));
		rasterSymbology.HillshadeProperties.IsEnabled = true;
		rasterSymbology.HillshadeProperties.ZFactor = 3f;
		RasterSymbology = rasterSymbology;
	}

	public override List<MapHover> MapHover(HitTestArgs args, ref object hitTestCache)
	{
		return null;
	}

	public void ReplaceResampler(IResample<float> raster)
	{
		if (raster == null)
		{
			throw new ArgumentNullException("raster");
		}
		_raster = raster;
		IncrementRenderState();
	}

	protected override bool RenderInternal(PlotRequest plot, ref object cache)
	{
		ContourProperties contourProperties = RasterSymbology.ContourProperties;
		ContourLayerStats contourStats = ((contourProperties != null && contourProperties.IsEnabled) ? ContourStats : ContourLayerStats.None);
		bool defaultRenderImplementation = RasterRenderHelper.DefaultRenderImplementation(this, plot, _raster, RasterSymbology, contourStats);
		if (defaultRenderImplementation && RegisterDataOnPlotRequest)
		{
			lock (plot.RasterDataLock)
			{
				IMemoryOwner<float> memoryOwner = plot.RasterData[this];
				try
				{
					plot.CommonData?.RegisterCommonData(_raster, memoryOwner.Memory.Slice(0, plot.RasterQuery.CellCount));
				}
				catch (Exception exception)
				{
					_logger.LogWarning(exception, "We might have a subtle race condition with registering a disposed memOwner");
				}
			}
			if (PlotRasterTileOutlines)
			{
				int unity = 1;
				List<Extent> extents = new List<Extent>(unity);
				CollectionsMarshal.SetCount(extents, unity);
				Span<Extent> extentsSpan = CollectionsMarshal.AsSpan(extents);
				int index = 0;
				extentsSpan[index] = _raster.ValidExtent;
				new ExtentRenderLayer(extents).Render(plot);
				ITiledRasterDefinition tiledRasterDefinition = _raster as ITiledRasterDefinition;
				if (tiledRasterDefinition == null && _raster is RasterPyramid<float> rasterPyramid)
				{
					ResampleArgs args = RasterSymbology.AsResampleArgs(plot.RasterQuery);
					int bestPyramidLevel = rasterPyramid.GetBestPyramidLevel(args);
					tiledRasterDefinition = rasterPyramid.GetPyramidLevel(bestPyramidLevel) as ITiledRasterDefinition;
				}
				if (tiledRasterDefinition != null)
				{
					new RasterDefinitionRenderLayer(tiledRasterDefinition.TiledRasterDefinition).Render(plot);
				}
			}
		}
		return defaultRenderImplementation;
	}

	protected override long GetInternalRenderState()
	{
		return base.GetInternalRenderState() + _symbologyVersionTracker.GetVersion(RasterSymbology) + ((_raster as ISpatialVersion)?.VersionXY ?? 0);
	}
}
