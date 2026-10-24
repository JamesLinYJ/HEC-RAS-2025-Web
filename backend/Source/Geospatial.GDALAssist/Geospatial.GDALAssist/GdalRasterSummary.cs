using System;
using System.Collections.Generic;
using System.Diagnostics;
using Geospatial.Rasters;

namespace Geospatial.GDALAssist;

[DebuggerDisplay("{DebuggerDisplay}")]
public class GdalRasterSummary
{
	public string Path { get; init; }

	public int BandCount { get; init; }

	public Projection Projection { get; init; }

	public RasterDefinition NativeRaster { get; init; }

	public string ConnectionString { get; private set; }

	public string Driver { get; private set; }

	public string Subdataset { get; private set; }

	public Dictionary<string, string> FileMetadata { get; private set; }

	public DateTime[] BandTimestamps { get; }

	public string Units { get; set; }

	public DateTime[] ForecastIssueTimes { get; set; }

	public string DataType { get; set; }

	private GdalRasterSummary()
	{
	}

	public GdalRasterSummary(GDALRaster gdr)
	{
		Path = gdr.TryGetPath();
		BandCount = gdr.GetBandCount();
		ConnectionString = gdr.Filename;
		Driver = gdr.DriverShortName;
		Subdataset = gdr.TryGetSubdatasetName();
		Projection = gdr.GetProjection();
		NativeRaster = gdr.GetNativeRaster();
		FileMetadata = gdr.GetMetadataDictionary();
		BandTimestamps = gdr.TryParseDatetimesPerBand();
		ForecastIssueTimes = gdr.TryParseForecastIssueTimesPerBand();
		Units = gdr.TryParseUnits();
		DataType = gdr.TryParseDataType();
	}

	public static GdalRasterSummary LoadBasics(GDALRaster gdr)
	{
		return new GdalRasterSummary
		{
			Path = gdr.Filename,
			BandCount = gdr.GetBandCount(),
			ConnectionString = gdr.Filename,
			Projection = gdr.GetProjection(),
			NativeRaster = gdr.GetNativeRaster()
		};
	}

	public static GdalRasterSummary LoadFull(GDALRaster gdr)
	{
		return new GdalRasterSummary(gdr);
	}

	public static GdalRasterSummary LoadFull(string rasterFile)
	{
		using GDALRaster gdr = new GDALRaster(rasterFile);
		return new GdalRasterSummary(gdr);
	}

	public static List<GdalRasterSummary> ParseCollection(IList<string> files)
	{
		List<GdalRasterSummary> gdalRasterSummaries = new List<GdalRasterSummary>();
		foreach (string file in files)
		{
			try
			{
				using GDALRaster gdr = new GDALRaster(file);
				gdalRasterSummaries.Add(new GdalRasterSummary(gdr));
			}
			catch
			{
				gdalRasterSummaries.Add(null);
			}
		}
		return gdalRasterSummaries;
	}

	public static List<GdalRasterSummary> ParseCollection(GdalRasterCollection gdrc)
	{
		List<GdalRasterSummary> gdalRasterSummaries = new List<GdalRasterSummary>();
		foreach (KeyValuePair<string, GDALRaster> raster in gdrc.Rasters)
		{
			gdalRasterSummaries.Add(new GdalRasterSummary(raster.Value));
		}
		return gdalRasterSummaries;
	}
}
