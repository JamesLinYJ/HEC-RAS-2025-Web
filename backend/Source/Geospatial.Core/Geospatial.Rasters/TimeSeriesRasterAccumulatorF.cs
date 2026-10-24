using Geospatial.PairedData;
using Utility.Units;

namespace Geospatial.Rasters;

internal class TimeSeriesRasterAccumulatorF : TimeSeriesAccumulatorF, IAccumulatedRasterizer<float>, IAccumulatedResampler<float>, ISurface<float>, IDiscreteTimesteps, IRasterDefinition
{
	private readonly ITimeSeriesRaster<float> _ds;

	public RasterDefinition RasterDefinition => _ds.RasterDefinition;

	public TimeSeriesRasterAccumulatorF(ITimeSeriesRaster<float> ds, TimeSeriesDataType dataType, Time timeUnits)
		: base(ds, dataType, timeUnits)
	{
		_ds = ds;
	}

	public TimeSeriesRasterAccumulatorF(ITimeSeriesRaster<float> ds, TimeSeriesDataType dataType, Time timeUnits, ProfileInterpolationMethod interp)
		: base(ds, dataType, timeUnits, interp)
	{
		_ds = ds;
	}
}
