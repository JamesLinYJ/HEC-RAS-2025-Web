using System;
using Geospatial.Rasters;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Utility.Interfaces;

namespace Ras.Rendering;

public class ResultMapFunctionalLayer : RenderLayer, IBandedRenderLayer, IRenderLayer, IDisplayName, IReadOnlyDisplayName, IProfileStateProvider, IRasterRenderLayer
{
	private readonly ResultMapRenderLayer _mapRenderLayer;

	private readonly ITimeSeriesResampler<float> _analyticSurface;

	private readonly ConverterDelegates.Convert<float> _composeInPlace;

	private RasterSymbology _symb = RasterSymbology.DefaultRasterSymbology();

	public override Extent ValidExtent => _mapRenderLayer.ValidExtent;

	public IProfileState ProfileState => ((IProfileStateProvider)_mapRenderLayer).ProfileState;

	public RasterSymbology RasterSymbology
	{
		get
		{
			return _symb;
		}
		set
		{
			_symb = value;
			IncrementRenderState();
		}
	}

	public ResultMapFunctionalLayer(ResultMapRenderLayer mapLayer, ITimeSeriesResampler<float> analyticSurface, ConverterDelegates.Convert<float> compose)
	{
		_mapRenderLayer = mapLayer;
		_analyticSurface = analyticSurface;
		_composeInPlace = compose;
	}

	protected override bool RenderInternal(PlotRequest request, ref object cache)
	{
		throw new NotImplementedException();
	}
}
