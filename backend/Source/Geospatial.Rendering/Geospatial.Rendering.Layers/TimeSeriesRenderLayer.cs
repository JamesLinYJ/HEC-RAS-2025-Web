using System;
using System.Linq;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Utility.Extensions;
using Utility.Interfaces;

namespace Geospatial.Rendering.Layers;

public class TimeSeriesRenderLayer : RenderLayer, ITimeSeriesRenderLayer, IRenderLayer, IDisplayName, IReadOnlyDisplayName, IRasterRenderLayer
{
	public static DateTime NullTime = DateTime.MinValue;

	private ITimeSeriesResampler<float> _ts;

	private DateTime _currentTime = NullTime;

	public DateTime StartTime => _ts.Times.FirstOrDefault();

	public DateTime EndTime => _ts.Times.LastOrDefault();

	public DateTime CurrentTime
	{
		get
		{
			return _currentTime;
		}
		set
		{
			_currentTime = value;
		}
	}

	public override Extent ValidExtent => _ts.ValidExtent;

	public RasterSymbology RasterSymbology { get; set; } = RasterSymbology.DefaultRasterSymbology();

	public bool CurrentTimeIsValid
	{
		get
		{
			if (_currentTime != NullTime && _currentTime >= StartTime)
			{
				return _currentTime <= EndTime;
			}
			return false;
		}
	}

	public TimeSeriesRenderLayer(ITimeSeriesResampler<float> ts)
	{
		if (ts == null)
		{
			throw new ArgumentNullException("ts");
		}
		_ts = ts;
	}

	public TimeSeriesRenderLayer(ITimeSeriesResampler<float> ts, RasterSymbology symb)
	{
		if (ts == null)
		{
			throw new ArgumentNullException("ts");
		}
		_ts = ts;
		RasterSymbology = symb;
	}

	private IResample<float> GetCurrentPinnedTimestep()
	{
		if (!CurrentTimeIsValid)
		{
			return null;
		}
		return _ts.AsSingleProfile(_ts.Times.IndexOf(CurrentTime));
	}

	protected override bool RenderInternal(PlotRequest plot, ref object cache)
	{
		throw new NotImplementedException("Was previously using RasterRenderLayer code with some weird static thing");
	}

	public bool TrySetTime(DateTime dt)
	{
		if (dt < StartTime || dt > EndTime || dt == NullTime)
		{
			CurrentTime = NullTime;
			return false;
		}
		CurrentTime = dt;
		return true;
	}
}
