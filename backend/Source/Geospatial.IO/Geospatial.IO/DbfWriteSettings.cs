namespace Geospatial.IO;

public class DbfWriteSettings
{
	public bool AutoTrimFieldNames;

	public bool OverwriteFileIfExists;

	public bool IgnoreBadColumnTypes;

	public bool UseShapefileFieldNameLimit;

	public bool UseShapefileStringLimit;

	public int FloatCharLength;

	public int FloatNumDecimals;

	public static DbfWriteSettings Default()
	{
		return new DbfWriteSettings
		{
			AutoTrimFieldNames = true,
			OverwriteFileIfExists = false,
			IgnoreBadColumnTypes = false,
			UseShapefileFieldNameLimit = true,
			UseShapefileStringLimit = true,
			FloatCharLength = 19,
			FloatNumDecimals = 8
		};
	}
}
