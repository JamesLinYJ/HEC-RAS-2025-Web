using System.Data;
using System.Runtime.CompilerServices;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

[StandardModule]
internal static class DataTableExtensions
{
	public static void Write(this DataTable dt, int rowIdx, int colIdx, object localValue)
	{
		if (dt.Columns[colIdx].ReadOnly)
		{
			dt.Columns[colIdx].ReadOnly = false;
			dt.Rows[rowIdx][colIdx] = RuntimeHelpers.GetObjectValue(localValue);
			dt.Columns[colIdx].ReadOnly = true;
		}
		else
		{
			dt.Rows[rowIdx][colIdx] = RuntimeHelpers.GetObjectValue(localValue);
		}
	}

	public static string GetString(this DataTable dt, int fid, string colName, string fallback = "")
	{
		return GetString(dt, fid, dt.Columns.IndexOf(colName), fallback);
	}

	public static string GetString(this DataTable dt, int fid, int colIndex, string fallback = "")
	{
		if (colIndex == -1)
		{
			return fallback;
		}
		object objectValue = RuntimeHelpers.GetObjectValue(dt.Rows[fid][colIndex]);
		if (objectValue == null || Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			return fallback;
		}
		return objectValue as string;
	}

	public static bool GetBoolean(this DataTable dt, int fid, string colName)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(dt.Rows[fid][colName]);
		if (Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)) || objectValue == null)
		{
			return false;
		}
		return Conversions.ToBoolean(objectValue);
	}

	public static float GetSingle(this DataTable dt, int fid, string colName, float fallback = float.NaN)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(dt.Rows[fid][colName]);
		if (objectValue == null || Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			return fallback;
		}
		return Conversions.ToSingle(objectValue);
	}

	public static float GetSingle(this DataTable dt, int fid, int colIdx, float fallback = float.NaN)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(dt.Rows[fid][colIdx]);
		if (objectValue == null || Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			return fallback;
		}
		return Conversions.ToSingle(objectValue);
	}

	public static Point2D GetPoint2D(this DataTable dt, int fid, string colName)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(dt.Rows[fid][colName]);
		if (objectValue == null || Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			return new Point2D(float.NaN, float.NaN);
		}
		return (objectValue != null) ? ((Point2D)objectValue) : default(Point2D);
	}

	public static int GetInteger(this DataTable dt, int fid, string colName, int fallback = 0)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(dt.Rows[fid][colName]);
		if (objectValue == null || Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			return fallback;
		}
		return Conversions.ToInteger(objectValue);
	}

	public static double GetDouble(this DataTable dt, int fid, string colName, double fallback = double.NaN)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(dt.Rows[fid][colName]);
		if (objectValue == null || Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			return fallback;
		}
		return Conversions.ToDouble(objectValue);
	}

	public static T GetObject<T>(this DataTable dt, int fid, string colName) where T : class
	{
		object objectValue = RuntimeHelpers.GetObjectValue(dt.Rows[fid][colName]);
		if (Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)) || objectValue == null)
		{
			return null;
		}
		return objectValue as T;
	}
}
