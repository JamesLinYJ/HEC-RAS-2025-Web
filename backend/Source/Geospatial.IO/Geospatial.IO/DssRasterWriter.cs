using System;
using System.IO;
using Geospatial.PairedData;
using Geospatial.Rasters;
using Hec.Dss;
using Utility.Progress;
using Utility.Units;

namespace Geospatial.IO;

public static class DssRasterWriter
{
	public static void WriteTimeSeriesRaster(string filename, ITimeSeriesRaster<float> raster, string pathname, DateTime firstGridStartTime, ProgressReporter pr = null)
	{
		using (new DssWriter(filename))
		{
			DssPath dssPath = new DssPath(pathname);
			for (int i = 0; i < raster.ProfileCount; i++)
			{
				DateTime dt = ((i != 0) ? raster.Times[i - 1] : firstGridStartTime);
				DateTime dt2 = raster.Times[i];
				dssPath.Dpart = GetDtString(dt);
				dssPath.Epart = GetDtString(dt2);
			}
			throw new NotImplementedException();
		}
	}

	public static DssTimeSeriesRasterDataSource TryReadTimeSeriesRaster(DssReader dssr, string pathname, ProgressReporter errMessages = null)
	{
		if (errMessages == null)
		{
			errMessages = ProgressReporter.None();
		}
		try
		{
			return new DssTimeSeriesRasterDataSource(dssr, pathname);
		}
		catch (Exception ex)
		{
			errMessages.ReportMessage(ex.Message);
			return null;
		}
	}

	public static DssTimeSeriesRasterDataSource TryReadTimeSeriesRaster(string filename, string pathname, bool keepFileHandleOpen, ProgressReporter errMessages = null)
	{
		if (errMessages == null)
		{
			errMessages = ProgressReporter.None();
		}
		if (!File.Exists(filename))
		{
			errMessages.ReportMessage("File does not exist: " + filename);
			return null;
		}
		try
		{
			return new DssTimeSeriesRasterDataSource(filename, pathname, keepFileHandleOpen);
		}
		catch (Exception ex)
		{
			errMessages.ReportMessage(ex.Message);
			return null;
		}
	}

	public static IAccumulatedRasterizer<float> TryReadAccumulatedRaster(DssTimeSeriesRasterDataSource datasource, ProgressReporter errMessages = null)
	{
		if (errMessages == null)
		{
			errMessages = ProgressReporter.None();
		}
		try
		{
			if (datasource.ProfileCount == 0)
			{
				errMessages.ReportMessage("No profiles available.");
				return null;
			}
			Grid grd = null;
			datasource.UseReader((DssReader dssr) =>
			{
				grd = dssr.GetGrid(datasource.Pathnames[0], retrieveData: false);
			});
			TimeSeriesDataType dataType;
			Utility.Units.Time timeUnits;
			if (grd.DataType == DssDataType.PER_AVER || grd.DataType == DssDataType.INST_VAL)
			{
				dataType = TimeSeriesDataType.Rate;
				PeriodAveragePrecipitation periodAveragePrecipitation = PeriodAveragePrecipitation.TryParse(grd.Units);
				if (periodAveragePrecipitation == null)
				{
					errMessages.ReportMessage("Could not parse data units: " + grd.Units);
					return null;
				}
				_ = periodAveragePrecipitation.VerticalUnits;
				timeUnits = periodAveragePrecipitation.TimeUnits;
			}
			else if (grd.DataType == DssDataType.PER_CUM)
			{
				dataType = TimeSeriesDataType.Delta;
				PeriodCumulativePrecipitation periodCumulativePrecipitation = PeriodCumulativePrecipitation.TryParse(grd.Units);
				if (periodCumulativePrecipitation == null)
				{
					errMessages.ReportMessage("Could not parse data units: " + grd.Units);
					return null;
				}
				_ = periodCumulativePrecipitation.VerticalUnits;
				timeUnits = Utility.Units.Time.Hours;
			}
			else
			{
				if (grd.DataType != DssDataType.INST_CUM)
				{
					errMessages.ReportMessage("Could not parse data type: " + grd.DataType);
					return null;
				}
				dataType = TimeSeriesDataType.Amount;
				CumulativePrecipitation cumulativePrecipitation = CumulativePrecipitation.TryParse(grd.Units);
				if (cumulativePrecipitation == null)
				{
					errMessages.ReportMessage("Could not parse data units: " + grd.Units);
					return null;
				}
				_ = cumulativePrecipitation.VerticalUnits;
				timeUnits = Utility.Units.Time.Hours;
			}
			return datasource.AsTimeSeriesRasterizer().AsAccumulatedRasterizer(dataType, timeUnits);
		}
		catch (Exception ex)
		{
			errMessages.ReportMessage(ex.Message);
			return null;
		}
	}

	private static string GetDtString(DateTime dt)
	{
		Hec.Dss.Time.DateTimeToHecDateTime(dt, out var date, out var time);
		return date + " " + time;
	}

	private static void PopulateGrid(Grid grd, RasterDefinition rdef)
	{
		grd.CellSize = (float)rdef.CellSize;
	}
}
