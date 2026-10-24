using System;
using System.Collections.Generic;
using System.Linq;

namespace Geospatial.Rasters;

public class MergedTimeSeriesRasterizer<T> : MergedTimeSeriesResampler<T>, ITimeSeriesRaster<T>, ITimeSeriesResampler<T>, IMultiBandResampler<T>, ISurface<T>, IDiscreteTimesteps, IMultiBandRaster<T>, IRasterDefinition where T : unmanaged, IEquatable<T>
{
	public RasterDefinition RasterDefinition { get; }

	public MergedTimeSeriesRasterizer(IList<ITimeSeriesRaster<T>> inputs)
		: base((IList<ITimeSeriesResampler<T>>)((IEnumerable<ITimeSeriesRaster<T>>)inputs).Select((Func<ITimeSeriesRaster<T>, ITimeSeriesResampler<T>>)((ITimeSeriesRaster<T> input) => input)).ToList())
	{
		if (inputs.Count != 0)
		{
			RasterDefinition = inputs.First().RasterDefinition;
		}
	}

	public void GetRange(PixelRange range, int bandIndex, Memory<T> result)
	{
		Tuple<ITimeSeriesResampler<T>, DateTime, int> tuple = _timestepMap[bandIndex];
		(tuple.Item1 as ITimeSeriesRaster<T>).GetRange(range, tuple.Item3, result);
	}
}
