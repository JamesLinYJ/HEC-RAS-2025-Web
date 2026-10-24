using System;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using H5Assist;

namespace Geospatial.IO.MetImport;

public interface IGriddedDataSelection : IDisposable
{
	ISurface<float> Surface { get; }

	Projection Projection { get; }

	bool SupportsPrecipConversion => false;

	GriddedWriteInfo WriteToH5(H5Writer writer, string dataset);
}
