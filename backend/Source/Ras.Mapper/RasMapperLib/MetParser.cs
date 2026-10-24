using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Geospatial.GDALAssist;
using H5Assist;
using H5Assist.VB;
using Hec.Dss;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using Utility.Extensions;
using Utility.Progress;
using Utility.Units;

namespace RasMapperLib;

public class MetParser
{
	[Serializable]
	[CompilerGenerated]
	internal sealed class _Closure_0024__
	{
		public static readonly _Closure_0024__ _0024I;

		public static Func<double, float> _0024I7_002D1;

		static _Closure_0024__()
		{
			_0024I = new _Closure_0024__();
		}

		[SpecialName]
		internal float _Lambda_0024__7_002D0(double x)
		{
			return (float)x;
		}

		[SpecialName]
		internal float _Lambda_0024__7_002D1(double v)
		{
			return (float)v;
		}

		[SpecialName]
		internal bool _Lambda_0024__7_002D2(float v)
		{
			return v != -9999f;
		}

		[SpecialName]
		internal float _Lambda_0024__8_002D0(double d)
		{
			return (float)d;
		}

		[SpecialName]
		internal float _Lambda_0024__8_002D1(double d)
		{
			return (float)d;
		}
	}

	public const string DataTypeAttribute = "Time Series Data Type";

	public const string DataUnitsAttribute = "Time Series Data Units";

	public static List<MetTimeSeries> TryParsePointTimeSeries(H5Assist.VB.H5Reader hr, List<string> datasets, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		List<MetTimeSeries> metTimeSeries = new List<MetTimeSeries>();
		Dictionary<string, DssReader> dssReaders = new Dictionary<string, DssReader>();
		Dictionary<string, DssPathCollection> dssCats = new Dictionary<string, DssPathCollection>();
		checked
		{
			int count = datasets.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				try
				{
					string dataset = datasets[i];
					if (!hr.ExistsAsDataset(dataset))
					{
						metTimeSeries.Add(null);
						continue;
					}
					string tryReadStringAttributeScalar = hr.TryReadStringAttributeScalar(dataset, "Source");
					if (string.IsNullOrEmpty(tryReadStringAttributeScalar))
					{
						prog.ReportError("No valid source found for " + dataset);
						metTimeSeries.Add(null);
						continue;
					}
					switch (tryReadStringAttributeScalar)
					{
					case "Table":
						metTimeSeries.Add(PointLoadTimeSeriesFromTable(hr, dataset, prog));
						break;
					case "DSS":
						metTimeSeries.Add(PointLoadTimeSeriesFromDss(hr, dataset, ref dssReaders, ref dssCats, prog));
						break;
					case "Constant":
					{
						float tryReadSingleAttributeScalar = hr.TryReadSingleAttributeScalar(dataset, "Constant Value", -9999f);
						if (float.IsNaN(tryReadSingleAttributeScalar))
						{
							tryReadSingleAttributeScalar = -9999f;
						}
						string dataType = "per-aver";
						string units = hr.TryReadStringAttributeScalar(dataset, "Constant Units");
						metTimeSeries.Add(MetTimeSeries.ConstantValue(tryReadSingleAttributeScalar, units, dataType));
						break;
					}
					default:
						prog.ReportError("Unknown Data Source: " + tryReadStringAttributeScalar.InDoubleQuotes());
						metTimeSeries.Add(null);
						break;
					}
				}
				catch (Exception ex)
				{
					ProjectData.SetProjectError(ex);
					Exception ex2 = ex;
					prog.ReportError(ex2.Message);
					ProjectData.ClearProjectError();
				}
			}
			foreach (KeyValuePair<string, DssReader> dssReader in dssReaders)
			{
				dssReader.Value?.Dispose();
			}
			return metTimeSeries;
		}
	}

	public static List<MetPairedData> TryParsePointPairedData(H5Assist.VB.H5Reader hr, List<string> datasets, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		List<MetPairedData> metPairedDatas = new List<MetPairedData>();
		Dictionary<string, DssReader> dssReaders = new Dictionary<string, DssReader>();
		Dictionary<string, DssPathCollection> dssCats = new Dictionary<string, DssPathCollection>();
		checked
		{
			int count = datasets.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				try
				{
					string dataset = datasets[i];
					if (!hr.ExistsAsDataset(dataset))
					{
						metPairedDatas.Add(null);
						continue;
					}
					string tryReadStringAttributeScalar = hr.TryReadStringAttributeScalar(dataset, "Source");
					if (string.IsNullOrEmpty(tryReadStringAttributeScalar))
					{
						prog.ReportError("No valid source found for " + dataset);
						metPairedDatas.Add(null);
						continue;
					}
					switch (tryReadStringAttributeScalar)
					{
					case "Table":
						metPairedDatas.Add(PointLoadPairedDataFromTable(hr, dataset, prog));
						break;
					case "DSS":
						metPairedDatas.Add(PointLoadPairedDataFromDss(hr, dataset, ref dssReaders, ref dssCats, prog));
						break;
					case "Constant":
						prog.ReportError("Error, Data Source 'Constant' invalid for Paired Data.");
						metPairedDatas.Add(null);
						break;
					default:
						prog.ReportError("Unknown Data Source: " + tryReadStringAttributeScalar.InDoubleQuotes());
						metPairedDatas.Add(null);
						break;
					}
				}
				catch (Exception ex)
				{
					ProjectData.SetProjectError(ex);
					Exception ex2 = ex;
					prog.ReportError(ex2.Message);
					ProjectData.ClearProjectError();
				}
			}
			foreach (KeyValuePair<string, DssReader> dssReader in dssReaders)
			{
				dssReader.Value?.Dispose();
			}
			return metPairedDatas;
		}
	}

	private static MetTimeSeries PointLoadTimeSeriesFromTable(H5Assist.VB.H5Reader hr, string ds, ProgressReporter prog)
	{
		CompoundDataset compoundDataset = hr.ReadCompoundDataset(ds);
		string inDoubleQuotesDsTerm = " from " + ds.InDoubleQuotes();
		string[] column = compoundDataset.GetColumn<string>("Date");
		double[] column2 = compoundDataset.GetColumn<double>("Simulation Time");
		float[] column3 = compoundDataset.GetColumn<float>("Value");
		if (column == null || column2 == null || column3 == null)
		{
			prog.ReportError("Invalid columns in " + ds.InDoubleQuotes());
			return null;
		}
		int column3Term = column3.Count();
		if (column3Term == 0)
		{
			prog.ReportError("No values provided for " + ds.InDoubleQuotes());
			return null;
		}
		string tryReadStringAttributeScalar = hr.TryReadStringAttributeScalar(ds, "Time Series Data Units");
		string tryReadStringAttributeScalar2 = hr.TryReadStringAttributeScalar(ds, "Time Series Data Type");
		if (string.IsNullOrEmpty(tryReadStringAttributeScalar))
		{
			prog.ReportError("No unit tag found" + inDoubleQuotesDsTerm);
			return null;
		}
		if (string.IsNullOrEmpty(tryReadStringAttributeScalar2))
		{
			prog.ReportError("No data-type tag found" + inDoubleQuotesDsTerm);
			return null;
		}
		string tryReadStringAttributeScalar3 = hr.TryReadStringAttributeScalar(ds, "Time Series Mode");
		checked
		{
			switch (tryReadStringAttributeScalar3)
			{
			case "Regular Interval":
			{
				string tryReadStringAttributeScalar4 = hr.TryReadStringAttributeScalar(ds, "Time Series Time Interval");
				Utility.Units.Time time2 = Utility.Units.Time.TryParse(tryReadStringAttributeScalar4);
				if (time2 == null)
				{
					prog.ReportError("Could not parse RTS units: " + tryReadStringAttributeScalar4.InDoubleQuotes() + inDoubleQuotesDsTerm);
					return null;
				}
				int ordinal = default;
				if (!hr.TryReadAttributeScalar(ds, "Time Series Time Increment", ref ordinal))
				{
					prog.ReportError("Error finding RTS increment " + inDoubleQuotesDsTerm);
					return null;
				}
				TimeSpan dt = time2.ToTimespan(ordinal);
				bool condition = default;
				if (!hr.TryReadAttributeScalar(ds, "Time Series Use Fixed Start", ref condition))
				{
					prog.ReportError("Missing required attribute 'Time Series Use Fixed Start'");
					return null;
				}
				if (!condition)
				{
					return MetTimeSeries.FromRTS(column3, dt, tryReadStringAttributeScalar, tryReadStringAttributeScalar2);
				}
				string tryReadStringAttributeScalar5 = hr.TryReadStringAttributeScalar(ds, "Time Series Fixed Start");
				if (!Hec.Dss.Time.TryConvertFromHecDateTime(tryReadStringAttributeScalar5, out var date2))
				{
					prog.ReportError("Error parsing time " + tryReadStringAttributeScalar5.InDoubleQuotes() + inDoubleQuotesDsTerm);
					return null;
				}
				return MetTimeSeries.FromRTS(column3, date2, dt, tryReadStringAttributeScalar, tryReadStringAttributeScalar2);
			}
			case "Irregular Fixed":
			{
				DateTime[] dateTimes = new DateTime[column3Term - 1 + 1];
				int count = column3Term - 1;
				for (int j = 0; j <= count; j++)
				{
					if (Hec.Dss.Time.TryConvertFromHecDateTime(column[j], out var date))
					{
						dateTimes[j] = date;
						continue;
					}
					prog.ReportError("Illegal date string: " + column[j].InDoubleQuotes() + inDoubleQuotesDsTerm);
					return null;
				}
				return new MetTimeSeries(column3, dateTimes, tryReadStringAttributeScalar, tryReadStringAttributeScalar2);
			}
			case "Irregular Floating":
			{
				string tryReadStringAttributeScalar6 = hr.TryReadStringAttributeScalar(ds, "Simulation Time Units");
				Utility.Units.Time time = Utility.Units.Time.TryParse(tryReadStringAttributeScalar6);
				if (time == null)
				{
					prog.ReportError("Could not parse simulation time units: " + tryReadStringAttributeScalar6.InDoubleQuotes() + inDoubleQuotesDsTerm);
					return null;
				}
				if (time.Equals(Utility.Units.Time.Days))
				{
					return new MetTimeSeries(column3, column2, tryReadStringAttributeScalar, tryReadStringAttributeScalar2);
				}
				double convertTo = time.ConvertTo(Utility.Units.Time.Days, 1.0);
				double[] doubleValues = new double[column3Term - 1 + 1];
				int count2 = column3Term - 1;
				for (int i = 0; i <= count2; i++)
				{
					double column22 = column2[i];
					if (double.IsNaN(column22))
					{
						prog.ReportError("Illegal date offset NaN " + inDoubleQuotesDsTerm);
						return null;
					}
					doubleValues[i] = column22 * convertTo;
				}
				return new MetTimeSeries(column3, doubleValues, tryReadStringAttributeScalar, tryReadStringAttributeScalar2);
			}
			default:
				prog.ReportError("Error parsing table mode " + tryReadStringAttributeScalar3.InDoubleQuotes() + " from " + ds.InDoubleQuotes());
				return null;
			}
		}
	}

	private static MetPairedData PointLoadPairedDataFromTable(H5Assist.VB.H5Reader hr, string ds, ProgressReporter prog)
	{
		CompoundDataset compoundDataset = hr.ReadCompoundDataset(ds);
		_ = " from " + ds.InDoubleQuotes();
		if (!hr.ExistsAsAttribute(ds, "Column Names"))
		{
			prog.ReportError("Error, missing required attribute 'Column Names'");
			return null;
		}
		if (!hr.ExistsAsAttribute(ds, "Column Data Types"))
		{
			prog.ReportError("Error, missing required attribute 'Column Data Types'");
			return null;
		}
		string[] values = null;
		string[] values2 = null;
		string[] values3 = null;
		hr.ReadAttribute(ds, "Column Names", ref values);
		hr.ReadAttribute(ds, "Column Data Types", ref values2);
		if (hr.ExistsAsAttribute(ds, "Column Data Units"))
		{
			hr.ReadAttribute(ds, "Column Data Units", ref values3);
		}
		else
		{
			values3 = new string[checked(values.Count() + 1)];
		}
		if (values.Count() != 2)
		{
			prog.ReportError("Error: Only 2 columns allowed for the PairedData object.");
			return null;
		}
		float[] column = compoundDataset.GetColumn<float>(values[0]);
		float[] column2 = compoundDataset.GetColumn<float>(values[1]);
		if (column == null || column2 == null)
		{
			prog.ReportError("Invalid columns in " + ds.InDoubleQuotes());
			return null;
		}
		string xUnits = "";
		string yUnits = "";
		if (values3 != null)
		{
			xUnits = values3[0];
			yUnits = values3[1];
		}
		string xDataType = "";
		string yDataType = "";
		if (values2 != null)
		{
			xDataType = values2[0];
			yDataType = values2[1];
		}
		return new MetPairedData(column, column2, xUnits, xDataType, yUnits, yDataType);
	}

	private static MetTimeSeries PointLoadTimeSeriesFromDss(H5Assist.VB.H5Reader hr, string ds, ref Dictionary<string, DssReader> dssReaders, ref Dictionary<string, DssPathCollection> dssCats, ProgressReporter prog)
	{
		string tryReadStringAttributeScalar = hr.TryReadStringAttributeScalar(ds, "DSS Filename");
		string tryReadStringAttributeScalar2 = hr.TryReadStringAttributeScalar(ds, "DSS Pathname");
		if (string.IsNullOrEmpty(tryReadStringAttributeScalar) || string.IsNullOrEmpty(tryReadStringAttributeScalar2))
		{
			prog.ReportError("Invalid attributes on " + ds.InDoubleQuotes() + ", Filename/Path not found.");
			return null;
		}
		if (!File.Exists(tryReadStringAttributeScalar))
		{
			prog.ReportError(tryReadStringAttributeScalar.InDoubleQuotes() + " does not exist on the file system.");
			return null;
		}
		DssReader dssReaders2 = null;
		DssPathCollection dssPathCollection = null;
		if (dssReaders.TryGetValue(tryReadStringAttributeScalar, out dssReaders2))
		{
			dssPathCollection = dssCats[tryReadStringAttributeScalar];
		}
		else
		{
			dssReaders2 = new DssReader(tryReadStringAttributeScalar);
			dssReaders[tryReadStringAttributeScalar] = dssReaders2;
			dssPathCollection = dssReaders2.GetCatalog();
			dssCats[tryReadStringAttributeScalar] = dssPathCollection;
		}
		DssPath dssPath = new DssPath(tryReadStringAttributeScalar2);
		if (!dssReaders2.PathExists(dssPath, DateTime.MinValue, DateTime.MinValue))
		{
			prog.ReportError(tryReadStringAttributeScalar2.InDoubleQuotes() + " does not exist in " + tryReadStringAttributeScalar.InDoubleQuotes());
			return null;
		}
		DssPathCondensed dssPathCondensed = null;
		foreach (DssPath dssPathCollection2 in dssPathCollection)
		{
			if (dssPathCollection2.PathWithoutDate.RoughlyEquals(dssPath.PathWithoutDate))
			{
				dssPathCondensed = dssPathCollection2 as DssPathCondensed;
			}
		}
		float[] floatValues = null;
		DateTime[] dateTimes = null;
		string units = "";
		string dataType = "";
		if (dssPathCondensed == null)
		{
			TimeSeries timeSeries = dssReaders2.GetTimeSeries(dssPath, DateTime.MinValue, DateTime.MinValue);
			floatValues = Array.ConvertAll(timeSeries.Values, [SpecialName] (double x) => (float)x);
			dateTimes = timeSeries.Times;
			dataType = timeSeries.DataType;
			units = timeSeries.Units;
		}
		else
		{
			List<float> floatList = new List<float>();
			List<DateTime> dateTimesList = new List<DateTime>();
			foreach (DssPath comprisingDSSPath in dssPathCondensed.GetComprisingDSSPaths())
			{
				TimeSeries timeSeries2 = dssReaders2.GetTimeSeries(comprisingDSSPath, DateTime.MinValue, DateTime.MinValue);
				floatList.AddRange(timeSeries2.Values.Select((_Closure_0024__._0024I7_002D1 == null) ? (_Closure_0024__._0024I7_002D1 = [SpecialName] (double v) => (float)v) : _Closure_0024__._0024I7_002D1));
				dateTimesList.AddRange(timeSeries2.Times);
				dataType = timeSeries2.DataType;
				units = timeSeries2.Units;
			}
			floatValues = floatList.ToArray();
			dateTimes = dateTimesList.ToArray();
		}
		int count = floatValues.Length;
		for (int intervalIndex = 0; intervalIndex <= count; intervalIndex = checked(intervalIndex + 1))
		{
			if (floatValues[intervalIndex] == -901f)
			{
				floatValues[intervalIndex] = -9999f;
			}
		}
		floatValues.TrimIndexOf([SpecialName] (float v) => v != -9999f, out var firstKeptIndex, out var keepCount);
		if (keepCount <= 0)
		{
			prog.ReportError("No valid values found in " + tryReadStringAttributeScalar.InDoubleQuotes() + ": " + tryReadStringAttributeScalar2.InDoubleQuotes());
			return null;
		}
		if (firstKeptIndex > 0 || keepCount < floatValues.Count())
		{
			floatValues = floatValues.ToList().GetRange(firstKeptIndex, keepCount).ToArray();
			dateTimes = dateTimes.ToList().GetRange(firstKeptIndex, keepCount).ToArray();
		}
		return new MetTimeSeries(floatValues, dateTimes, units, dataType);
	}

	private static MetPairedData PointLoadPairedDataFromDss(H5Assist.VB.H5Reader hr, string ds, ref Dictionary<string, DssReader> dssReaders, ref Dictionary<string, DssPathCollection> dssCats, ProgressReporter prog)
	{
		string tryReadStringAttributeScalar = hr.TryReadStringAttributeScalar(ds, "DSS Filename");
		string tryReadStringAttributeScalar2 = hr.TryReadStringAttributeScalar(ds, "DSS Pathname");
		if (string.IsNullOrEmpty(tryReadStringAttributeScalar) || string.IsNullOrEmpty(tryReadStringAttributeScalar2))
		{
			prog.ReportError("Invalid attributes on " + ds.InDoubleQuotes() + ", Filename/Path not found.");
			return null;
		}
		if (!File.Exists(tryReadStringAttributeScalar))
		{
			prog.ReportError(tryReadStringAttributeScalar.InDoubleQuotes() + " does not exist on the file system.");
			return null;
		}
		DssReader dssReaders2 = null;
		DssPathCollection dssPathCollection = null;
		if (dssReaders.TryGetValue(tryReadStringAttributeScalar, out dssReaders2))
		{
			dssPathCollection = dssCats[tryReadStringAttributeScalar];
		}
		else
		{
			dssReaders2 = new DssReader(tryReadStringAttributeScalar);
			dssReaders[tryReadStringAttributeScalar] = dssReaders2;
			dssPathCollection = dssReaders2.GetCatalog();
			dssCats[tryReadStringAttributeScalar] = dssPathCollection;
		}
		DssPath path = new DssPath(tryReadStringAttributeScalar2);
		if (!dssReaders2.PathExists(path, DateTime.MinValue, DateTime.MinValue))
		{
			prog.ReportError(tryReadStringAttributeScalar2.InDoubleQuotes() + " does not exist in " + tryReadStringAttributeScalar.InDoubleQuotes());
			return null;
		}
		PairedData pairedData = dssReaders2.GetPairedData(tryReadStringAttributeScalar2);
		if (pairedData == null)
		{
			prog.ReportError("No Paired Data found for " + tryReadStringAttributeScalar2);
			return null;
		}
		float[] floatValues = pairedData.Ordinates.Select([SpecialName] (double d) => (float)d).ToArray();
		float[] floatValuesArray = pairedData.Values?.FirstOrDefault()?.Select([SpecialName] (double d) => (float)d).ToArray();
		if (floatValuesArray == null)
		{
			prog.ReportError("No Paired Data found for " + tryReadStringAttributeScalar2);
			return null;
		}
		int count = floatValues.Length;
		checked
		{
			for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
			{
				if (floatValues[intervalIndex] == -901f)
				{
					floatValues[intervalIndex] = -9999f;
				}
			}
			int count2 = floatValuesArray.Length;
			for (int intervalIndex2 = 0; intervalIndex2 <= count2; intervalIndex2++)
			{
				if (floatValuesArray[intervalIndex2] == -901f)
				{
					floatValuesArray[intervalIndex2] = -9999f;
				}
			}
			if (!hr.ExistsAsAttribute(ds, "Column Names"))
			{
				prog.ReportError("Error, missing required attribute 'Column Names'");
				return null;
			}
			if (!hr.ExistsAsAttribute(ds, "Column Data Types"))
			{
				prog.ReportError("Error, missing required attribute 'Column Data Types'");
				return null;
			}
			string[] values = null;
			string[] values2 = null;
			hr.ReadAttribute(ds, "Column Names", ref values);
			hr.ReadAttribute(ds, "Column Data Types", ref values2);
			BaseUnitType baseUnitType = BaseUnitType.TryParseUnitType(values2[0], pairedData.UnitsIndependent);
			BaseUnitType baseUnitType2 = BaseUnitType.TryParseUnitType(values2[1], pairedData.UnitsDependent);
			if (baseUnitType == null || baseUnitType2 == null)
			{
				BaseUnitType baseUnitType3 = BaseUnitType.TryParseUnitType(values2[0], pairedData.UnitsDependent);
				baseUnitType2 = BaseUnitType.TryParseUnitType(values2[1], pairedData.UnitsIndependent);
				if (baseUnitType3 == null)
				{
					prog.ReportError(tryReadStringAttributeScalar2 + ": Could not parse a value of type " + values2[0].InSingleQuotes() + " " + values[0].InParentheses() + " from " + pairedData.UnitsDependent.InSingleQuotes() + " or " + pairedData.UnitsIndependent.InSingleQuotes());
					return null;
				}
				if (baseUnitType2 == null)
				{
					prog.ReportError(tryReadStringAttributeScalar2 + ": Could not parse a value of type " + values2[1].InSingleQuotes() + " " + values[1].InParentheses() + " from " + pairedData.UnitsDependent.InSingleQuotes() + " or " + pairedData.UnitsIndependent.InSingleQuotes());
					return null;
				}
				return new MetPairedData(floatValues, floatValuesArray, pairedData.UnitsDependent, pairedData.TypeDependent, pairedData.UnitsIndependent, pairedData.TypeIndependent);
			}
			return new MetPairedData(floatValues, floatValuesArray, pairedData.UnitsIndependent, pairedData.TypeIndependent, pairedData.UnitsDependent, pairedData.TypeDependent);
		}
	}

	public static MetTimeSeries LoadProcessedTimeSeriesData(H5Assist.VB.H5Reader hr, string ds, bool errorIfMissingMetadata = true, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		CompoundDataset compoundDataset = hr.ReadCompoundDataset(ds);
		string inDoubleQuotesDsTerm = " from " + ds.InDoubleQuotes();
		string[] column = compoundDataset.GetColumn<string>("Date");
		double[] column2 = compoundDataset.GetColumn<double>("Simulation Time");
		float[] column3 = compoundDataset.GetColumn<float>("Value");
		if (column == null || column2 == null || column3 == null)
		{
			prog.ReportError("Invalid columns" + inDoubleQuotesDsTerm);
			return null;
		}
		int column3Term = column3.Count();
		if (column3Term == 0)
		{
			prog.ReportError("No values provided for " + ds.InDoubleQuotes());
			return null;
		}
		string tryReadStringAttributeScalar = hr.TryReadStringAttributeScalar(ds, "Time Series Data Units");
		string dataType = hr.TryReadStringAttributeScalar(ds, "Time Series Data Type");
		if (string.IsNullOrEmpty(tryReadStringAttributeScalar) && errorIfMissingMetadata)
		{
			prog.ReportError("No unit tag found" + inDoubleQuotesDsTerm);
			return null;
		}
		checked
		{
			DateTime[] dateTimes = new DateTime[column3Term - 1 + 1];
			int count = column3Term - 1;
			for (int i = 0; i <= count; i++)
			{
				if (Hec.Dss.Time.TryConvertFromHecDateTime(column[i], out var date))
				{
					dateTimes[i] = date;
					continue;
				}
				prog.ReportError("Illegal date string: " + column[i].InDoubleQuotes() + inDoubleQuotesDsTerm);
				return null;
			}
			return new MetTimeSeries(column3, dateTimes, column2, tryReadStringAttributeScalar, dataType);
		}
	}

	public static MetPairedData LoadProcessedPairedData(H5Assist.VB.H5Reader hr, string ds, bool errorIfMissingMetadata = true, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		return PointLoadPairedDataFromTable(hr, ds, prog);
	}

	public static MetRasterTimeSeries TryParseRasterTimeSeries(H5Assist.VB.H5Reader h5r, string folder, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		string tryReadStringAttributeScalar = h5r.TryReadStringAttributeScalar(folder, "Source");
		switch (ParseMeteorologyGriddedSource(tryReadStringAttributeScalar))
		{
		case MeteorologyGriddedSource.Unknown:
			prog.ReportError("Gridded Source attribute missing, or could not be parsed: " + tryReadStringAttributeScalar.InDoubleQuotes());
			return null;
		case MeteorologyGriddedSource.DSS:
			return RasterLoadFromDss(h5r, folder, prog);
		case MeteorologyGriddedSource.SingleRaster:
			return RasterLoadFromSingleFile(h5r, folder, prog);
		case MeteorologyGriddedSource.MultipleRaster:
			return RasterLoadFromMultipleFiles(h5r, folder, prog);
		default:
			prog.ReportError("Gridded source type " + tryReadStringAttributeScalar.InDoubleQuotes() + " not supported yet.");
			return null;
		}
	}

	private static MetRasterTimeSeries RasterLoadFromDss(H5Assist.VB.H5Reader h5r, string folder, ProgressReporter prog)
	{
		string filename = h5r.TryReadStringAttributeScalar(folder, "DSS Filename");
		filename = RasMapperLib.Utilities.Path.MakeAbsolute(filename, SharedData.RasMapFilename);
		MetRasterTimeSeries result;
		if (!File.Exists(filename))
		{
			prog.ReportError("DSS filename " + filename.InDoubleQuotes() + " not found.");
			result = null;
		}
		else if (string.IsNullOrEmpty(h5r.TryReadStringAttributeScalar(folder, "DSS Pathname")))
		{
			prog.ReportError("DSS pathname attribute is missing for " + filename.InDoubleQuotes());
			result = null;
		}
		else
		{
			try
			{
				throw new NotImplementedException();
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				prog.ReportError("Could not parse Precipitation dataset: " + ex2.Message);
				result = null;
				ProjectData.ClearProjectError();
			}
		}
		return result;
	}

	private static MetRasterTimeSeries RasterLoadFromSingleFile(H5Assist.VB.H5Reader h5r, string folder, ProgressReporter prog)
	{
		string textValue = "";
		string textValue2 = "";
		if (!h5r.TryReadAttributeScalar(folder, "GDAL Filename", ref textValue))
		{
			prog.ReportError(folder.InDoubleQuotes() + ": Layer with 'Single Raster' mode requires the attribute " + "GDAL Filename".InDoubleQuotes() + ", which is missing.");
			return null;
		}
		if (!h5r.TryReadAttributeScalar(folder, "GDAL Datasetname", ref textValue2))
		{
			prog.ReportMessage(folder.InDoubleQuotes() + " has no sub-dataset selected (OK)");
		}
		throw new NotImplementedException();
	}

	private static MetRasterTimeSeries RasterLoadFromMultipleFiles(H5Assist.VB.H5Reader h5r, string folder, ProgressReporter prog)
	{
		string tryReadStringAttributeScalar = h5r.TryReadStringAttributeScalar(folder, "GDAL Folder");
		if (string.IsNullOrEmpty(tryReadStringAttributeScalar))
		{
			prog.ReportError(folder + ": Layer with 'Multi Raster' mode requires a valid folder.");
			return null;
		}
		tryReadStringAttributeScalar = RasMapperLib.Utilities.Path.MakeAbsolute(tryReadStringAttributeScalar, SharedData.RasMapFilename);
		if (!Directory.Exists(tryReadStringAttributeScalar))
		{
			prog.ReportError(folder + ": " + tryReadStringAttributeScalar.InDoubleQuotes() + " does not exist.");
			return null;
		}
		string tryReadStringAttributeScalar2 = h5r.TryReadStringAttributeScalar(folder, "GDAL Filter");
		if (string.IsNullOrEmpty(tryReadStringAttributeScalar2))
		{
			prog.ReportMessage(folder + ": Layer with 'Multi Raster' mode does not have a file filter, we will use every file in the directory.");
			tryReadStringAttributeScalar2 = "*";
		}
		if (string.IsNullOrEmpty(h5r.TryReadStringAttributeScalar(folder, "GDAL Datasetname")))
		{
			prog.ReportMessage(folder.InDoubleQuotes() + " has no sub-dataset selected (OK)");
		}
		string[] files = Directory.GetFiles(tryReadStringAttributeScalar, tryReadStringAttributeScalar2);
		if (files.IsNullOrEmpty())
		{
			prog.ReportError("No files found with the given folder/filter: " + tryReadStringAttributeScalar.InDoubleQuotes() + ", " + tryReadStringAttributeScalar2.InDoubleQuotes());
			return null;
		}
		string splitFolderTerm = folder.Split(new char[2] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).Last();
		string changeExtension = System.IO.Path.ChangeExtension(h5r.Filename, splitFolderTerm + ".vrt");
		bool condition = true;
		if (File.Exists(changeExtension))
		{
			condition = false;
		}
		if (condition)
		{
			List<string> matchingCandidates = null;
			GDALRaster.FindMatchingFiles(files.First(), files, out matchingCandidates, null, prog);
			if (File.Exists(changeExtension))
			{
				File.Delete(changeExtension);
			}
			GDALRaster.BuildMultibandVRT(matchingCandidates, changeExtension);
		}
		return null;
	}

	public static bool TryParseGenericMetLayer(H5Assist.VB.H5Reader h5r, RASResults res, RASEventConditions ec, string metTypeName, ref IMeteorologyLayer lyr, ProgressReporter prog)
	{
		string metFolderName = RASEventConditions.GetMetFolderName(metTypeName);
		bool condition = true;
		if (h5r.TryReadAttributeScalar(metFolderName, "Enabled", ref condition))
		{
			if (!condition)
			{
				return true;
			}
		}
		else if (h5r.TryReadStringAttributeScalar(metFolderName, "Enabled").RoughlyEquals("False"))
		{
			return true;
		}
		string modeStr = h5r.TryReadStringAttributeScalar(metFolderName, "Mode");
		float ratio = h5r.TryReadSingleAttributeScalar(metFolderName, "Ratio");
		if (float.IsNaN(ratio))
		{
			ratio = 1f;
		}
		switch (ParseMeteorologyMode(modeStr))
		{
		case MeteorologyMode.Constant:
		{
			string tryReadStringAttributeScalar = h5r.TryReadStringAttributeScalar(metFolderName, "Constant Units");
			float tryReadSingleAttributeScalar = h5r.TryReadSingleAttributeScalar(metFolderName, "Constant Value");
			if (string.IsNullOrEmpty(tryReadStringAttributeScalar))
			{
				prog.ReportMessage("No Units provided for " + metFolderName);
				return false;
			}
			if (float.IsNaN(tryReadSingleAttributeScalar))
			{
				prog.ReportMessage("No Value provided for " + metFolderName);
				return false;
			}
			lyr = new ConstantMetLayer(metTypeName, tryReadSingleAttributeScalar);
			lyr.Ratio = ratio;
			return true;
		}
		case MeteorologyMode.Point:
		{
			PointMetTimeSeriesLayer lyr2 = null;
			bool result = PointMetTimeSeriesLayer.TryParse(h5r, res, ec.MetStations, metTypeName, ratio, ref lyr2, prog);
			lyr = lyr2;
			return result;
		}
		default:
			return false;
		}
	}

	public static MeteorologyMode ParseMeteorologyMode(string modeStr)
	{
		if (modeStr.RoughlyEquals("Point"))
		{
			return MeteorologyMode.Point;
		}
		if (modeStr.RoughlyEquals("Gridded"))
		{
			return MeteorologyMode.Gridded;
		}
		if (modeStr.RoughlyEquals("Constant"))
		{
			return MeteorologyMode.Constant;
		}
		return MeteorologyMode.Unknown;
	}

	public static MeteorologyGriddedSource ParseMeteorologyGriddedSource(string sourceStr)
	{
		if (sourceStr.RoughlyEquals("DSS"))
		{
			return MeteorologyGriddedSource.DSS;
		}
		if (sourceStr.RoughlyEquals("Single Raster File"))
		{
			return MeteorologyGriddedSource.SingleRaster;
		}
		if (sourceStr.RoughlyEquals("Multiple Raster Files"))
		{
			return MeteorologyGriddedSource.MultipleRaster;
		}
		return MeteorologyGriddedSource.Unknown;
	}
}
