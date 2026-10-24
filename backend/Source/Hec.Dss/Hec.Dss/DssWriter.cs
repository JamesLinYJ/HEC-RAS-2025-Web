using System;

namespace Hec.Dss;

public class DssWriter : DssReader
{
	public DssWriter(string filename, int messageLevel = 3)
		: base(filename, messageLevel)
	{
	}

	public static int ConvertFromDSS6ToDSS7(string fileName6, string fileName7)
	{
		return DssNative.hec_dss_convertToVersion7(fileName6, fileName7);
	}

	public int Write(TimeSeriesProfile profile, bool saveAsFloat = false)
	{
		throw new NotImplementedException("TimeSeriesProfile profile");
	}

	private static float[] MatrixToFloatArray(double[,] values, int numCols, int numRows)
	{
		float[] floatValues = new float[numCols * numRows];
		int ordinal = 0;
		for (int i = 0; i < numRows; i++)
		{
			for (int j = 0; j < numCols; j++)
			{
				if (ordinal < floatValues.Length)
				{
					floatValues[ordinal] = (float)values[i, j];
				}
				ordinal++;
			}
		}
		return floatValues;
	}

	private static double[] MatrixToDoubleArray(double[,] values, int numCols, int numRows)
	{
		double[] doubleValues = new double[numCols * numRows];
		int ordinal = 0;
		for (int i = 0; i < numRows; i++)
		{
			for (int j = 0; j < numCols; j++)
			{
				if (ordinal < doubleValues.Length)
				{
					doubleValues[ordinal] = values[i, j];
				}
				ordinal++;
			}
		}
		return doubleValues;
	}

	public int Write(TimeSeries ts, bool saveAsFloat = false)
	{
		if (ts.Count == 0)
		{
			return -1;
		}
		if (ts.Path == null)
		{
			throw new Exception("Path was not specified");
		}
		int unity = -1;
		if (ts.IsRegular())
		{
			Time.DateTimeToHecDateTime(ts.StartDateTime, out var date, out var time);
			int qualityArraySize = 0;
			if (ts.Qualities != null)
			{
				qualityArraySize = ts.Qualities.Length;
			}
			unity = DssNative.hec_dss_tsStoreRegular(dss, ts.Path.FullPath, date, time, ts.Values, ts.Values.Length, ts.Qualities, qualityArraySize, saveAsFloat ? 1 : 0, ts.Units, ts.DataType);
		}
		else
		{
			int irregularTimeGranularity = GetIrregularTimeGranularity(ts);
			int irregularJulianBaseDate = GetIrregularJulianBaseDate(ts, irregularTimeGranularity);
			string startDateBase = Time.JulianToHecDate(irregularJulianBaseDate);
			int[] times = Time.ConvertDatesToHecInteger(ts.Times, irregularJulianBaseDate, irregularTimeGranularity);
			unity = DssNative.hec_dss_tsStoreIregular(dss, ts.Path.FullPath, startDateBase, times, irregularTimeGranularity, ts.Values, ts.Values.Length, ts.Qualities, ts.Qualities.Length, saveAsFloat ? 1 : 0, ts.Units, ts.DataType);
		}
		if (ts.LocationInformation != null)
		{
			StoreLocation(ts.Path.FullPath, ts.LocationInformation);
		}
		return unity;
	}

	private int GetIrregularJulianBaseDate(TimeSeries ts, int timeGranularitySeconds)
	{
		if (timeGranularitySeconds == 1)
		{
			return Time.DateToJulian(ts.StartDateTime);
		}
		return 0;
	}

	private int GetIrregularTimeGranularity(TimeSeries ts)
	{
		int result = 60;
		if (ts.Count < 2)
		{
			return result;
		}
		for (int i = 1; i < ts.Count; i++)
		{
			DateTime dateTime = ts[i - 1].DateTime;
			if (ts[i].DateTime.Subtract(dateTime).TotalSeconds < 60.0)
			{
				return 1;
			}
		}
		DateTime dateTime2 = ts[0].DateTime;
		if (ts[ts.Count - 1].DateTime.Subtract(dateTime2).TotalDays / 365.25 > 100.0 && ts.Path.Epart.ToLower() == "ir-century")
		{
			result = 86400;
		}
		return result;
	}

	public int StoreLocation(string dssPath, LocationInformation loc, bool overwrite = false)
	{
		return DssNative.hec_dss_locationStore(dss, dssPath, loc.XOrdinate, loc.YOrdinate, loc.ZOrdiante, (int)loc.CoordinateSystem, loc.CoordinateID, loc.HorizontalUnits, loc.HorizontalDatum, loc.VerticalUnits, loc.VerticalDatum, loc.TimeZoneName, loc.Supplemental, overwrite ? 1 : 0);
	}

	public void Write(Grid grid)
	{
		Console.WriteLine(DssNative.hec_dss_gridStore(dss, grid.PathName, (int)grid.GridType, (int)grid.DataType, grid.LowerLeftCellX, grid.LowerLeftCellY, grid.NumberOfCellsX, grid.NumberOfCellsY, grid.RangeLimitTable.Length, grid.SRSDefinitionType, grid.TimeZoneRawOffset, grid.IsInterval ? 1 : 0, grid.IsTimeStamped ? 1 : 0, grid.Units, grid.DataSource, grid.SRSName, grid.SRSDefinition, grid.TimeZoneID, grid.CellSize, grid.XCoordOfGridCellZero, grid.YCoordOfGridCellZero, grid.NullValue, grid.MaxDataValue, grid.MinDataValue, grid.MeanDataValue, grid.RangeLimitTable, grid.NumberEqualOrExceedingRangeLimit, grid.Data));
	}

	public static bool Squeeze(string filename)
	{
		return DssNative.hec_dss_squeeze(filename) == 0;
	}

	public void DeleteRecord(string path)
	{
		DssNative.hec_dss_delete(dss, path);
	}

	public void DeleteRecord(DssPath path)
	{
		DeleteRecord(path.FullPath);
	}

	public void Write(PairedData pd)
	{
		double[] flattenedValues = pd.FlattenedValues;
		ByteString byteString = new ByteString(pd.Labels);
		DssNative.hec_dss_pdStore(dss, pd.Path.FullPath, pd.Ordinates, pd.Ordinates.Length, flattenedValues, flattenedValues.Length, pd.Ordinates.Length, pd.CurveCount, pd.UnitsIndependent, pd.TypeIndependent, pd.UnitsDependent, pd.TypeDependent, byteString.Data, byteString.Data.Length);
	}
}
