using System.Text;

namespace Hec.Dss;

public class Grid
{
	public static string SHG_SRC_DEFINITION = "PROJCS[\"USA_Contiguous_Albers_Equal_Area_Conic_USGS_version\",GEOGCS[\"GCS_North_American_1983\",DATUM[\"D_North_American_1983\",SPHEROID[\"GRS_1980\",6378137.0,298.257222101]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]],PROJECTION[\"Albers\"],PARAMETER[\"False_Easting\",0.0],PARAMETER[\"False_Northing\",0.0],PARAMETER[\"Central_Meridian\",-96.0],PARAMETER[\"Standard_Parallel_1\",29.5],PARAMETER[\"Standard_Parallel_2\",45.5],PARAMETER[\"Latitude_Of_Origin\",23.0],UNIT[\"Meter\",1.0]]";

	public string PathName { get; set; }

	public GridType GridType { get; set; }

	public string Units { get; set; }

	public string DataSource { get; set; }

	public DssDataType DataType { get; set; }

	public int LowerLeftCellX { get; set; }

	public int LowerLeftCellY { get; set; }

	public int NumberOfCellsX { get; set; }

	public int NumberOfCellsY { get; set; }

	public float CellSize { get; set; }

	public string SRSName { get; set; }

	public int SRSDefinitionType { get; set; }

	public string SRSDefinition { get; set; }

	public float XCoordOfGridCellZero { get; set; }

	public float YCoordOfGridCellZero { get; set; }

	private int StructVersion { get; set; }

	private int Version { get; set; }

	private int CompressionMethod { get; set; }

	private int SizeOfCompressedElements { get; set; }

	public float NullValue { get; set; }

	public float UndefinedValue { get; set; }

	public string TimeZoneID { get; set; }

	public int TimeZoneRawOffset { get; set; }

	public bool IsInterval { get; set; }

	public bool IsTimeStamped { get; set; }

	private int StorageDataType { get; set; }

	public float MinDataValue { get; set; }

	public float MaxDataValue { get; set; }

	public float MeanDataValue { get; set; }

	public float[] RangeLimitTable { get; set; }

	public int[] NumberEqualOrExceedingRangeLimit { get; set; }

	public float[] Data { get; set; }

	public string Info()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("******** Printing GRID STRUCT ********");
		stringBuilder.AppendLine("GridStruct Version :" + StructVersion);
		stringBuilder.AppendLine("Path :" + PathName);
		stringBuilder.AppendLine("GridType :" + GridType);
		stringBuilder.AppendLine("Version : " + Version);
		stringBuilder.AppendLine("Data Units : " + Units);
		stringBuilder.AppendLine("Data Type : " + DataType);
		stringBuilder.AppendLine("LowerLeftCellX : " + LowerLeftCellX);
		stringBuilder.AppendLine("LowerLeftCellY : " + LowerLeftCellY);
		stringBuilder.AppendLine("NumberOfCellsX : " + NumberOfCellsX);
		stringBuilder.AppendLine("NumberOfCellsY : " + NumberOfCellsY);
		stringBuilder.AppendLine("CellSize : " + CellSize.ToString("0.00000"));
		stringBuilder.AppendLine("CompressionMethod : " + CompressionMethod);
		stringBuilder.AppendLine("SizeofCompressedElements : " + SizeOfCompressedElements);
		stringBuilder.AppendLine("NumberOfRanges : " + RangeLimitTable.Length);
		stringBuilder.AppendLine("SrsName : " + SRSName);
		stringBuilder.AppendLine("SrsDefinitionType : " + SRSDefinitionType);
		stringBuilder.AppendLine("_srsDefinition : " + SRSDefinition);
		stringBuilder.AppendLine("XCoordOfGridCellZero : " + XCoordOfGridCellZero.ToString("0.00000"));
		stringBuilder.AppendLine("YCoordOfGridCellZero : " + YCoordOfGridCellZero.ToString("0.00000"));
		stringBuilder.AppendLine("NullValue : " + NullValue.ToString("0.00000"));
		stringBuilder.AppendLine("TimeZoneID : " + TimeZoneID);
		stringBuilder.AppendLine("timeZoneRawOffset : " + TimeZoneRawOffset);
		stringBuilder.AppendLine("IsInterval : " + IsInterval);
		stringBuilder.AppendLine("IsTimeStamped : " + IsTimeStamped);
		stringBuilder.AppendLine("Storage Data Type  :" + StorageDataType);
		if (StorageDataType == 0)
		{
			stringBuilder.AppendLine("Max Data Value : " + MaxDataValue.ToString("0.00000"));
			stringBuilder.AppendLine("Min Data Value : " + MinDataValue.ToString("0.00000"));
			stringBuilder.AppendLine("Mean Data Value : " + MeanDataValue.ToString("0.00000"));
			stringBuilder.AppendLine("======== Range Limit Table ===========");
			if (RangeLimitTable != null && RangeLimitTable.Length != 0)
			{
				stringBuilder.AppendLine("           Range        > or =    Incremental Count");
				int rangeLimitTableLength = RangeLimitTable.Length;
				for (int i = 0; i < rangeLimitTableLength - 1; i++)
				{
					float rangeLimitTable = RangeLimitTable[i];
					string rangeLimitTable2 = rangeLimitTable.ToString("0.00000");
					if (rangeLimitTable == UndefinedValue)
					{
						rangeLimitTable2 = "undefined".PadRight(16);
					}
					stringBuilder.AppendLine(rangeLimitTable2 + " " + NumberEqualOrExceedingRangeLimit[i].ToString("0.00000"));
				}
				stringBuilder.AppendLine("====================================================");
			}
		}
		return stringBuilder.ToString();
	}

	private string ConvertMilitaryTimeTo12Hour(string timeString)
	{
		string s = timeString.Substring(0, 2);
		string textValue = "AM";
		int.TryParse(s, out var result);
		if (result > 12)
		{
			textValue = "PM";
			result -= 12;
		}
		string substring = timeString.Substring(2);
		return result + ":" + substring + textValue;
	}
}
