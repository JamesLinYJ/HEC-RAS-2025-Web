using System.Runtime.InteropServices;

internal class DssNative
{
	[DllImport("hecdss")]
	public static extern int hec_dss_CONSTANT_MAX_PATH_SIZE();

	[DllImport("hecdss")]
	public static extern int hec_dss_open(string fileName, out nint dss);

	[DllImport("hecdss")]
	public static extern int hec_dss_convertToVersion7(string fileName6, string fileName7);

	[DllImport("hecdss")]
	public static extern int hec_dss_getFileVersion(string fileName);

	[DllImport("hecdss")]
	public static extern int hec_dss_close(nint dss);

	[DllImport("hecdss")]
	internal static extern int hec_dss_getVersion(nint dss);

	[DllImport("hecdss")]
	internal static extern int hec_dss_set_value(string name, int value);

	[DllImport("hecdss")]
	internal static extern int hec_dss_set_string(string name, string value);

	[DllImport("hecdss")]
	internal static extern int hec_dss_squeeze(string fileName);

	[DllImport("hecdss")]
	internal static extern int hec_dss_delete(nint dss, string path);

	[DllImport("hecdss")]
	public static extern int hec_dss_record_count(nint dss);

	[DllImport("hecdss")]
	public static extern nint hec_dss_deprecated_ifltab(nint dss);

	[DllImport("hecdss")]
	public static extern void hec_dss_deprecated_ifltab_print(nint ifltab);

	[DllImport("hecdss")]
	public static extern int hec_dss_dataType(nint dss, string pathname);

	[DllImport("hecdss")]
	public static extern int hec_dss_tsStoreRegular(nint dss, string pathname, string startDate, string startTime, double[] valueArray, int arraySize, int[] qualityArray, int qualityArraySize, int saveAsFloat, string units, string type);

	[DllImport("hecdss")]
	public static extern int hec_dss_tsStoreIregular(nint dss, string pathname, string startDateBase, int[] times, int timeGranularitySeconds, double[] valueArray, int valueArraySize, int[] qualityArray, int qualityArraySize, int saveAsFloat, string units, string type);

	[DllImport("hecdss", CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int hec_dss_tsRetrieve(nint dss, string pathname, string startDate, string startTime, string endDate, string endTime, int[] timeArray, double[] valueArray, int arraySize, ref int numberValuesRead, int[] quality, int qualityLength, ref int julianBaseDate, ref int timeGranularitySeconds, byte[] units, int unitsLength, byte[] type, int typeLength);

	[DllImport("hecdss")]
	internal static extern int hec_dss_tsGetDateTimeRange(nint dss, string pathname, int boolFullSet, ref int firstValidJulian, ref int firstSeconds, ref int lastValidJulian, ref int lastSeconds);

	[DllImport("hecdss", CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int hec_dss_tsGetSizes(nint dss, string pathname, string startDate, string startTime, string endDate, string endTime, ref int numberValues, ref int qualityElementSize);

	[DllImport("hecdss")]
	public static extern int hec_dss_tsRetrieveInfo(nint dss, string pathname, byte[] units, int unitsLength, byte[] type, int typeLength);

	[DllImport("hecdss")]
	public static extern int hec_dss_catalog(nint dss, byte[] pathBuffer, int[] recordTypes, [In] byte[] pathFilter, int count, int pathBufferItemSize);

	[DllImport("hecdss")]
	public static extern int hec_dss_dateToYearMonthDay(string date, ref int year, ref int month, ref int day);

	[DllImport("hecdss")]
	public static extern int hec_dss_dateToJulian(string date);

	[DllImport("hecdss")]
	public static extern void hec_dss_julianToYearMonthDay(int julian, ref int year, ref int month, ref int day);

	[DllImport("hecdss")]
	public static extern int hec_dss_locationRetrieve(nint dss, string fullPath, ref double x, ref double y, ref double z, ref int coordinateSystem, ref int coordinateID, ref int horizontalUnits, ref int horizontalDatum, ref int verticalUnits, ref int verticalDatum, byte[] timeZoneName, int timeZoneNameLength, byte[] supplemental, int supplementalLength);

	[DllImport("hecdss")]
	public static extern int hec_dss_locationStore(nint dss, string fullPath, double x, double y, double z, int coordinateSystem, int coordinateID, int horizontalUnits, int horizontalDatum, int verticalUnits, int verticalDatum, string timeZoneName, string supplemental, int replace);

	[DllImport("hecdss")]
	public static extern int hec_dss_pdRetrieveInfo(nint dss, string pathname, ref int numberOrdinates, ref int numberCurves, byte[] unitsIndependent, int unitsIndependentLength, byte[] unitsDependent, int unitsDependentLength, byte[] typeIndependent, int typeIndependentLength, byte[] typeDependent, int typeDependentLength, ref int labelsLength);

	[DllImport("hecdss")]
	public static extern int hec_dss_pdRetrieve(nint dss, string pathname, double[] Ordinates, int OrdinatesLength, double[] Values, int ValuesLength, ref int numberOrdinates, ref int numberCurves, byte[] unitsIndependent, int unitsIndependentLength, byte[] typeIndependent, int typeIndependentLength, byte[] unitsDependent, int unitsDependentLength, byte[] typeDependent, int typeDependentLength, byte[] labels, int labelsLength);

	[DllImport("hecdss")]
	public static extern int hec_dss_pdStore(nint dss, string pathname, double[] Ordinates, int OrdinatesLength, double[] Values, int ValuesLength, int numberOrdinates, int numberCurves, string unitsIndependent, string typeIndependent, string unitsDependent, string typeDependent, byte[] labels, int labelsLength);

	[DllImport("hecdss")]
	public static extern int hec_dss_gridRetrieve(nint dss, string pathname, int boolRetrieveData, ref int type, ref int dataType, ref int lowerLeftCellX, ref int lowerLeftCellY, ref int numberOfCellsX, ref int numberOfCellsY, ref int numberOfRanges, ref int srsDefinitionType, ref int timeZoneRawOffset, ref int isInterval, ref int isTimeStamped, byte[] dataUnits, int dataUnitsLength, byte[] dataSource, int dataSourceLength, byte[] srsName, int srsNameLength, byte[] srsDefinition, int srsDefinitionLength, byte[] timeZoneID, int timeZoneIDLength, ref float cellSize, ref float xCoordOfGridCellZero, ref float yCoordOfGridCellZero, ref float nullValue, ref float maxDataValue, ref float minDataValue, ref float meanDataValue, float[] rangeLimitTable, int rangeTablesLength, int[] numberEqualOrExceedingRangeLimit, float[] data, int dataLength);

	[DllImport("hecdss")]
	public static extern int hec_dss_gridStore(nint dss, string pathname, int gridType, int dataType, int lowerLeftCellX, int lowerLeftCellY, int numberOfCellsX, int numberOfCellsY, int numberOfRanges, int srsDefinitionType, int timeZoneRawOffset, int isInterval, int isTimeStamped, string dataUnits, string dataSource, string srsName, string srsDefinition, string timeZoneID, float cellSize, float xCoordOfGridCellZero, float yCoordOfGridCellZero, float nullValue, float maxDataValue, float minDataValue, float meanDataValue, float[] rangeLimitTable, int[] numberEqualOrExceedingRangeLimit, float[] data);
}
