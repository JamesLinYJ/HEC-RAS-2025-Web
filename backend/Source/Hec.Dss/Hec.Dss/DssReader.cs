using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Hec.Dss;

public class DssReader : IDisposable
{
	private const double UNDEFINED_DOUBLE = -3.402823466E+38;

	private const double UNDEFINED_DOUBLE_2 = -3.4028234663852886E+38;

	protected long[] ifltab = new long[250];

	protected string filename;

	protected int versionNumber;

	private DssPathCollection _catalog;

	protected nint dss;

	public int LastTimeSeriesReadStatus { get; private set; }

	public string Filename
	{
		get
		{
			return filename;
		}
		private set
		{
			filename = value;
		}
	}

	protected DssReader()
	{
	}

	public DssReader(string filename, int messageLevel = 3)
	{
		OpenDssFile(filename, messageLevel);
	}

	public static int GetVersion(string fileName)
	{
		if (!File.Exists(fileName))
		{
			return 0;
		}
		return DssNative.hec_dss_getFileVersion(fileName);
	}

	private void OpenDssFile(string filename, int messageLevel)
	{
		DssGlobals.SetMessageLevel(messageLevel);
		int hec_dss_open = DssNative.hec_dss_open(filename, out dss);
		this.filename = filename;
		switch (hec_dss_open)
		{
		case 0:
			versionNumber = GetDSSFileVersion();
			break;
		case -1:
			throw new Exception("Unable to create the DSS file.");
		case -2:
			throw new Exception("Unable to connect to the DSS file.");
		case -3:
			throw new Exception("Incompatible DSS version.");
		case -10:
			throw new Exception("No filename provided.");
		case -700:
			throw new Exception("This library only supports DSS version 7 files");
		default:
			throw new Exception("Error opening DSS file.");
		}
	}

	public DssPathCollection GetCatalog()
	{
		if (_catalog == null)
		{
			_catalog = GetCatalogInternal();
		}
		return _catalog;
	}

	private DssPathCollection GetCatalogInternal()
	{
		List<string> rawCatalog = GetRawCatalog(dss, out var recordTypes);
		RecordType[] convertToRecordType = null;
		convertToRecordType = ConvertToRecordType(recordTypes);
		return new DssPathCollection(filename, rawCatalog.ToArray(), convertToRecordType, condensed: true);
	}

	internal static List<string> GetRawCatalog(nint dss, out int[] recordTypes, string filter = "")
	{
		int hec_dss_record_count = DssNative.hec_dss_record_count(dss);
		int hec_dss_CONSTANT_MAX_PATH_SIZE = DssNative.hec_dss_CONSTANT_MAX_PATH_SIZE();
		byte[] byteValues = new byte[hec_dss_record_count * hec_dss_CONSTANT_MAX_PATH_SIZE];
		recordTypes = new int[hec_dss_record_count];
		List<string> rawCatalog = new List<string>(hec_dss_record_count);
		byte[] pathFilter = new byte[1];
		if (filter != "")
		{
			pathFilter = Encoding.ASCII.GetBytes(filter);
		}
		int hec_dss_catalog = DssNative.hec_dss_catalog(dss, byteValues, recordTypes, pathFilter, hec_dss_record_count, hec_dss_CONSTANT_MAX_PATH_SIZE);
		for (int i = 0; i < hec_dss_catalog; i++)
		{
			int hec_dss_CONSTANT_MAX_PATH_SIZETerm = i * hec_dss_CONSTANT_MAX_PATH_SIZE;
			int count = Math.Min(Array.IndexOf(byteValues, (byte)0, hec_dss_CONSTANT_MAX_PATH_SIZETerm) - hec_dss_CONSTANT_MAX_PATH_SIZETerm, hec_dss_CONSTANT_MAX_PATH_SIZE);
			rawCatalog.Add(Encoding.ASCII.GetString(byteValues, hec_dss_CONSTANT_MAX_PATH_SIZETerm, count));
		}
		return rawCatalog;
	}

	private static RecordType[] ConvertToRecordType(int[] recTypes)
	{
		RecordType[] recordTypes = new RecordType[recTypes.Length];
		for (int i = 0; i < recTypes.Length; i++)
		{
			recordTypes[i] = RecordTypeFromInt(recTypes[i]);
		}
		return recordTypes;
	}

	private static RecordType RecordTypeFromInt(int recType)
	{
		RecordType result = RecordType.Unknown;
		if (recType >= 100 && recType < 110)
		{
			result = ((recType == 102 || recType == 107) ? RecordType.RegularTimeSeriesProfile : RecordType.RegularTimeSeries);
		}
		else if (recType >= 110 && recType < 200)
		{
			result = RecordType.IrregularTimeSeries;
		}
		else if (recType >= 200 && recType < 300)
		{
			result = RecordType.PairedData;
		}
		else if (recType >= 300 && recType < 400)
		{
			result = RecordType.Text;
		}
		else if (recType >= 400 && recType < 450)
		{
			result = RecordType.Grid;
		}
		else
		{
			switch (recType)
			{
			case 450:
				result = RecordType.Tin;
				break;
			case 20:
				result = RecordType.LocationInfo;
				break;
			}
		}
		return result;
	}

	public static bool IsValid(double doubleValue)
	{
		if (doubleValue != -3.402823466E+38 && doubleValue != -3.4028234663852886E+38)
		{
			return !IsMissing(doubleValue);
		}
		return false;
	}

	public static bool IsMissing(double doubleValue)
	{
		if (doubleValue != -901.0)
		{
			return doubleValue == -902.0;
		}
		return true;
	}

	public RecordType GetRecordType(DssPath path)
	{
		string pathname = path.FullPath;
		if (path is DssPathCondensed)
		{
			pathname = ((DssPathCondensed)path).GetComprisingDSSPaths()[0].FullPath;
		}
		int hec_dss_dataType = DssNative.hec_dss_dataType(dss, pathname);
		if (hec_dss_dataType <= 0)
		{
			DssPathCollection catalog = GetCatalog();
			DssPath.DatelessComparer cmp = new DssPath.DatelessComparer();
			pathname = catalog.Where((DssPath p) => cmp.Equals(p, path)).First().PathWithoutRange;
			hec_dss_dataType = DssNative.hec_dss_dataType(dss, pathname);
		}
		return RecordTypeFromInt(hec_dss_dataType);
	}

	private TimeSeries GetTimeSeries(DssPath dssPath, TimeWindow.ConsecutiveValueCompression compression, DateTime startDateTime = default(DateTime), DateTime endDateTime = default(DateTime), bool PreReadCatalog = true)
	{
		ValidateTsPathExists(dssPath, PreReadCatalog);
		bool startDateTimeDateTimeTerm = startDateTime != default(DateTime) && endDateTime != default(DateTime);
		Time.DateTimeToHecDateTime(startDateTime, out var date, out var time);
		Time.DateTimeToHecDateTime(endDateTime, out var date2, out var time2);
		if (!startDateTimeDateTimeTerm)
		{
			int firstValidJulian = 0;
			int firstSeconds = 0;
			int lastValidJulian = 0;
			int lastSeconds = 0;
			int rangeStatus = DssNative.hec_dss_tsGetDateTimeRange(dss, dssPath.FullPath, 1, ref firstValidJulian, ref firstSeconds, ref lastValidJulian, ref lastSeconds);
			LastTimeSeriesReadStatus = rangeStatus;
			if (rangeStatus != 0 || Time.IsUndefinedTime(firstValidJulian) || Time.IsUndefinedTime(lastValidJulian))
			{
				throw new InvalidDataException("DSS time-series date-range lookup failed or returned undefined dates (status " + rangeStatus + ").");
			}
			Time.JulianToHecDateTime(firstValidJulian, firstSeconds, out date, out time);
			Time.JulianToHecDateTime(lastValidJulian, lastSeconds, out date2, out time2);
		}
		int numberValues = 0;
		int qualityElementSize = 0;
		int sizeStatus = DssNative.hec_dss_tsGetSizes(dss, dssPath.PathWithoutDate, date, time, date2, time2, ref numberValues, ref qualityElementSize);
		LastTimeSeriesReadStatus = sizeStatus;
		if (sizeStatus != 0 || numberValues < 0 || qualityElementSize < 0)
		{
			throw new InvalidDataException("DSS time-series size lookup failed or returned invalid array sizes (status " + sizeStatus + ").");
		}
		int[] intValues = new int[numberValues];
		double[] doubleValues = new double[numberValues];
		int count = checked(numberValues * qualityElementSize);
		int[] qualities = new int[count];
		int numberValuesRead = 0;
		int julianBaseDate = 0;
		int timeGranularitySeconds = 60;
		ByteString byteString = new ByteString(64);
		ByteString byteString2 = new ByteString(64);
		int hec_dss_tsRetrieve = DssNative.hec_dss_tsRetrieve(dss, dssPath.PathWithoutRange, date, time, date2, time2, intValues, doubleValues, numberValues, ref numberValuesRead, qualities, count, ref julianBaseDate, ref timeGranularitySeconds, byteString.Data, byteString.Length, byteString2.Data, byteString2.Length);
		LastTimeSeriesReadStatus = hec_dss_tsRetrieve;
		TimeSeries timeSeries = new TimeSeries();
		if (hec_dss_tsRetrieve == 0)
		{
			Array.Resize(ref intValues, numberValuesRead);
			timeSeries.Times = Time.DateTimesFromJulianArray(intValues, timeGranularitySeconds, julianBaseDate);
			Array.Resize(ref doubleValues, numberValuesRead);
			if (count > 0)
			{
				Array.Resize(ref qualities, checked(numberValuesRead * qualityElementSize));
				timeSeries.Qualities = qualities;
			}
			timeSeries.Values = doubleValues;
			timeSeries.DataType = byteString2.ToString();
			timeSeries.Units = byteString.ToString();
			timeSeries.Path = new DssPath(dssPath.FullPath);
			timeSeries.LocationInformation = GetLocationInfo(dssPath.FullPath);
			if (compression != TimeWindow.ConsecutiveValueCompression.None)
			{
				timeSeries = timeSeries.Compress(compression);
			}
			if (dssPath.IsRegular)
			{
				timeSeries.Trim();
			}
			return timeSeries;
		}
		return GetEmptyTimeSeries(dssPath);
	}

	private void ValidateTsPathExists(DssPath dssPath, bool PreReadCatalog)
	{
		if (PreReadCatalog)
		{
			if (!PathExists(dssPath))
			{
				throw new Exception("Error: Path does not exist '" + dssPath.FullPath + "'");
			}
			RecordType recordType = GetRecordType(dssPath);
			if (recordType != RecordType.RegularTimeSeries && recordType != RecordType.IrregularTimeSeries)
			{
				throw new Exception("Error reading path " + dssPath.FullPath + ".  This path is not a time series");
			}
		}
	}

	public TimeSeries GetTimeSeries(DssPath dssPath, DateTime startDateTime = default(DateTime), DateTime endDateTime = default(DateTime), TimeWindow.TimeWindowBehavior behavior = TimeWindow.TimeWindowBehavior.StrictlyInclusive, TimeWindow.ConsecutiveValueCompression compression = TimeWindow.ConsecutiveValueCompression.None)
	{
		if (startDateTime > endDateTime)
		{
			return GetEmptyTimeSeries(dssPath);
		}
		switch (behavior)
		{
		case TimeWindow.TimeWindowBehavior.StrictlyInclusive:
			return GetTimeSeries(dssPath, compression, startDateTime, endDateTime);
		case TimeWindow.TimeWindowBehavior.Cover:
		{
			if (startDateTime == default(DateTime) && endDateTime == default(DateTime))
			{
				return GetTimeSeries(dssPath, compression, startDateTime, endDateTime);
			}
			if (dssPath.IsRegular)
			{
				DateTime startDateTime2 = startDateTime.AddSeconds(-TimeWindow.SecondsInInterval(dssPath));
				DateTime endDateTime2 = endDateTime.AddSeconds(TimeWindow.SecondsInInterval(dssPath));
				return TimeWindow.Trim(GetTimeSeries(dssPath, compression, startDateTime2, endDateTime2), startDateTime, endDateTime);
			}
			TimeSpan timeSpan = endDateTime - startDateTime;
			DateTime startDateTime3 = startDateTime.AddHours((0.0 - timeSpan.TotalHours) * 0.1);
			DateTime endDateTime3 = endDateTime.AddHours(timeSpan.TotalMinutes * 0.1);
			return GetTimeSeries(dssPath, compression, startDateTime3, endDateTime3);
		}
		default:
			return GetEmptyTimeSeries(dssPath);
		}
	}

	public TimeSeries GetEmptyTimeSeries(DssPath path)
	{
		if (path is DssPathCondensed)
		{
			path = ((DssPathCondensed)path).GetComprisingDSSPaths()[0];
		}
		int ordinal = 64;
		ByteString byteString = new ByteString(ordinal);
		ByteString byteString2 = new ByteString(ordinal);
		int hec_dss_tsRetrieveInfo = DssNative.hec_dss_tsRetrieveInfo(dss, path.FullPath, byteString.Data, ordinal, byteString2.Data, ordinal);
		TimeSeries timeSeries = new TimeSeries();
		if (hec_dss_tsRetrieveInfo != 0)
		{
			return timeSeries;
		}
		timeSeries.Path = path;
		timeSeries.Units = byteString.ToString();
		timeSeries.DataType = byteString2.ToString();
		return timeSeries;
	}

	public PairedData GetPairedData(string pathname, bool metaDataOnly = false)
	{
		int numberOrdinates = 0;
		int numberCurves = 0;
		int labelsLength = 0;
		ByteString byteString = new ByteString(128);
		ByteString byteString2 = new ByteString(128);
		ByteString byteString3 = new ByteString(128);
		ByteString byteString4 = new ByteString(128);
		PairedData pairedData = new PairedData();
		if (DssNative.hec_dss_pdRetrieveInfo(dss, pathname, ref numberOrdinates, ref numberCurves, byteString.Data, byteString.Data.Length, byteString2.Data, byteString2.Data.Length, byteString3.Data, byteString3.Data.Length, byteString4.Data, byteString4.Data.Length, ref labelsLength) != 0 || numberOrdinates <= 0 || numberCurves <= 0)
		{
			return pairedData;
		}
		pairedData.UnitsIndependent = byteString.ToString();
		pairedData.UnitsDependent = byteString2.ToString();
		pairedData.TypeDependent = byteString4.ToString();
		pairedData.TypeIndependent = byteString3.ToString();
		if (metaDataOnly)
		{
			return pairedData;
		}
		double[] ordinates = new double[numberOrdinates];
		double[] doubleValues = new double[numberOrdinates * numberCurves];
		ByteString byteString5 = new ByteString(labelsLength);
		if (DssNative.hec_dss_pdRetrieve(dss, pathname, ordinates, ordinates.Length, doubleValues, doubleValues.Length, ref numberOrdinates, ref numberCurves, byteString.Data, byteString.Data.Length, byteString3.Data, byteString3.Data.Length, byteString2.Data, byteString2.Data.Length, byteString4.Data, byteString4.Data.Length, byteString5.Data, byteString5.Data.Length) != 0)
		{
			return pairedData;
		}
		pairedData.Path = new DssPath(pathname);
		pairedData.Ordinates = ordinates;
		int ordinal = 0;
		pairedData.Values = new List<double[]>();
		for (int i = 0; i < numberCurves; i++)
		{
			double[] doubleValuesArray = new double[numberOrdinates];
			for (int j = 0; j < doubleValuesArray.Length; j++)
			{
				doubleValuesArray[j] = doubleValues[ordinal++];
			}
			pairedData.Values.Add(doubleValuesArray);
		}
		List<string> labels = new List<string>();
		labels.AddRange(byteString5.ToStringArray());
		pairedData.Labels = labels;
		pairedData.LocationInformation = GetLocationInfo(pathname);
		return pairedData;
	}

	public TimeSeriesProfile GetTimeSeriesProfile(string pathname)
	{
		return GetTimeSeriesProfile(new DssPath(pathname));
	}

	public TimeSeriesProfile GetTimeSeriesProfile(DssPath pathname, DateTime startDateTime = default(DateTime), DateTime endDateTime = default(DateTime))
	{
		throw new NotImplementedException("");
	}

	private static double[,] DoubleArrayToMatrix(double[] values, int numCols, int numRows)
	{
		double[,] doubleValues = new double[numRows, numCols];
		int ordinal = 0;
		for (int i = 0; i < doubleValues.GetLength(0); i++)
		{
			for (int j = 0; j < numCols; j++)
			{
				doubleValues[i, j] = values[ordinal++];
			}
		}
		return doubleValues;
	}

	public bool PathExists(string pathname, DateTime startDateTime = default(DateTime), DateTime endDateTime = default(DateTime))
	{
		return PathExists(new DssPath(pathname), startDateTime, endDateTime);
	}

	public bool PathExists(DssPath path, DateTime startDateTime = default(DateTime), DateTime endDateTime = default(DateTime))
	{
		bool startDateTimeDateTimeTerm = startDateTime != default(DateTime) && endDateTime != default(DateTime);
		int catalogDssPathTerm = 0;
		DssPathCollection catalog = GetCatalog();
		DssPath.DatelessComparer cmp = new DssPath.DatelessComparer();
		if (!startDateTimeDateTimeTerm)
		{
			catalogDssPathTerm = catalog.Count((DssPath p) => cmp.Equals(p, path));
		}
		else
		{
			catalog.Count((DssPath p) => cmp.Equals(p, path));
			catalogDssPathTerm = catalog.Count((DssPath p) => cmp.Equals(p, path) && p is DssPathCondensed && Time.ConvertFromHecDateTime((p as DssPathCondensed).DPartStart) >= startDateTime && Time.ConvertFromHecDateTime((p as DssPathCondensed).DPartEnd) <= endDateTime);
		}
		return catalogDssPathTerm > 0;
	}

	public bool ExactPathExists(DssPath path)
	{
		if (GetCatalog().FindExactPath(path.FullPath) == DssPath.NotFound)
		{
			return false;
		}
		return true;
	}

	public float CheckMissingFlag()
	{
		return float.MinValue;
	}

	public int GetDSSFileVersion()
	{
		return DssNative.hec_dss_getVersion(dss);
	}

	public LocationInformation GetLocationInfo(string pathname)
	{
		return GetLocationInfo(pathname, out _);
	}

	// Preserve the existing API's default-location behavior while allowing
	// readers to distinguish a missing location from actual zero coordinates.
	public LocationInformation GetLocationInfo(string pathname, out int status)
	{
		double x = 0.0;
		double y = 0.0;
		double z = 0.0;
		int coordinateSystem = 0;
		int coordinateID = 0;
		int horizontalUnits = 0;
		int horizontalDatum = 0;
		int verticalUnits = 0;
		int verticalDatum = 0;
		ByteString byteString = new ByteString(128);
		ByteString byteString2 = new ByteString(5000);
		int hec_dss_locationRetrieve = DssNative.hec_dss_locationRetrieve(dss, pathname, ref x, ref y, ref z, ref coordinateSystem, ref coordinateID, ref horizontalUnits, ref horizontalDatum, ref verticalUnits, ref verticalDatum, byteString.Data, byteString.Data.Length, byteString2.Data, byteString2.Data.Length);
		status = hec_dss_locationRetrieve;
		LocationInformation locationInformation = new LocationInformation();
		if (hec_dss_locationRetrieve != 0)
		{
			return locationInformation;
		}
		locationInformation.XOrdinate = x;
		locationInformation.YOrdinate = y;
		locationInformation.ZOrdiante = z;
		locationInformation.CoordinateSystem = (CoordinateSystem)coordinateSystem;
		locationInformation.CoordinateID = coordinateID;
		locationInformation.HorizontalUnits = horizontalUnits;
		locationInformation.HorizontalDatum = horizontalDatum;
		locationInformation.VerticalUnits = verticalUnits;
		locationInformation.VerticalDatum = verticalDatum;
		locationInformation.TimeZoneName = byteString.ToString();
		locationInformation.Supplemental = byteString2.ToString();
		return locationInformation;
	}

	public Grid GetGrid(string pathname, bool retrieveData)
	{
		int type = 0;
		int dataType = 0;
		int lowerLeftCellX = 0;
		int lowerLeftCellY = 0;
		int numberOfCellsX = 0;
		int numberOfCellsY = 0;
		int numberOfRanges = 0;
		int srsDefinitionType = 0;
		int timeZoneRawOffset = 0;
		int isInterval = 0;
		int isTimeStamped = 0;
		ByteString byteString = new ByteString(64);
		ByteString byteString2 = new ByteString(128);
		ByteString byteString3 = new ByteString(64);
		ByteString byteString4 = new ByteString(8192);
		ByteString byteString5 = new ByteString(64);
		float cellSize = 0f;
		float xCoordOfGridCellZero = 0f;
		float yCoordOfGridCellZero = 0f;
		float nullValue = 0f;
		float maxDataValue = 0f;
		float minDataValue = 0f;
		float meanDataValue = 0f;
		float[] rangeLimitTable = new float[0];
		int[] numberEqualOrExceedingRangeLimit = new int[0];
		float[] floatValues = new float[0];
		int boolRetrieveData = 0;
		DssNative.hec_dss_gridRetrieve(dss, pathname, boolRetrieveData, ref type, ref dataType, ref lowerLeftCellX, ref lowerLeftCellY, ref numberOfCellsX, ref numberOfCellsY, ref numberOfRanges, ref srsDefinitionType, ref timeZoneRawOffset, ref isInterval, ref isTimeStamped, byteString.Data, byteString.Length, byteString2.Data, byteString2.Length, byteString3.Data, byteString3.Length, byteString4.Data, byteString4.Length, byteString5.Data, byteString5.Length, ref cellSize, ref xCoordOfGridCellZero, ref yCoordOfGridCellZero, ref nullValue, ref maxDataValue, ref minDataValue, ref meanDataValue, rangeLimitTable, rangeLimitTable.Length, numberEqualOrExceedingRangeLimit, floatValues, floatValues.Length);
		int boolRetrieveData2 = 1;
		rangeLimitTable = new float[numberOfRanges];
		numberEqualOrExceedingRangeLimit = new int[numberOfRanges];
		floatValues = new float[numberOfCellsX * numberOfCellsY];
		DssNative.hec_dss_gridRetrieve(dss, pathname, boolRetrieveData2, ref type, ref dataType, ref lowerLeftCellX, ref lowerLeftCellY, ref numberOfCellsX, ref numberOfCellsY, ref numberOfRanges, ref srsDefinitionType, ref timeZoneRawOffset, ref isInterval, ref isTimeStamped, byteString.Data, byteString.Length, byteString2.Data, byteString2.Length, byteString3.Data, byteString3.Length, byteString4.Data, byteString4.Length, byteString5.Data, byteString5.Length, ref cellSize, ref xCoordOfGridCellZero, ref yCoordOfGridCellZero, ref nullValue, ref maxDataValue, ref minDataValue, ref meanDataValue, rangeLimitTable, rangeLimitTable.Length, numberEqualOrExceedingRangeLimit, floatValues, floatValues.Length);
		return new Grid
		{
			PathName = pathname,
			DataType = (DssDataType)dataType,
			GridType = (GridType)type,
			LowerLeftCellX = lowerLeftCellX,
			LowerLeftCellY = lowerLeftCellY,
			NumberOfCellsX = numberOfCellsX,
			NumberOfCellsY = numberOfCellsY,
			SRSDefinitionType = srsDefinitionType,
			TimeZoneRawOffset = timeZoneRawOffset,
			IsInterval = (isInterval == 1),
			IsTimeStamped = (isTimeStamped == 1),
			Units = byteString.ToString(),
			DataSource = byteString2.ToString(),
			SRSName = byteString3.ToString(),
			SRSDefinition = byteString4.ToString(),
			TimeZoneID = byteString5.ToString(),
			CellSize = cellSize,
			XCoordOfGridCellZero = xCoordOfGridCellZero,
			YCoordOfGridCellZero = yCoordOfGridCellZero,
			NullValue = nullValue,
			MaxDataValue = maxDataValue,
			MinDataValue = minDataValue,
			MeanDataValue = meanDataValue,
			RangeLimitTable = rangeLimitTable,
			NumberEqualOrExceedingRangeLimit = numberEqualOrExceedingRangeLimit,
			Data = floatValues
		};
	}

	public Grid GetGrid(DssPath path, bool retrieveData)
	{
		return GetGrid(path.FullPath, retrieveData);
	}

	public void Dispose()
	{
		DssNative.hec_dss_close(dss);
	}
}
