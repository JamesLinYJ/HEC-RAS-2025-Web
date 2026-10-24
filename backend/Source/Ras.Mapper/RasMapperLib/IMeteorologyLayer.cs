using System;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using Utility.Progress;

namespace RasMapperLib;

public interface IMeteorologyLayer
{
	Projection OverrideProjection { get; set; }

	InterpolationMode RasterInterpMode { get; set; }

	float Ratio { get; set; }

	bool RasterizeSaveData(string hdfFilename, string folder, RASResults res, ProgressReporter prog = null);

	bool GetTimeSeries(PointM pt, ref float[] values, ref DateTime[] times);

	void AlertTimeWindowChanged(DateTime startDt, DateTime endDt);
}
