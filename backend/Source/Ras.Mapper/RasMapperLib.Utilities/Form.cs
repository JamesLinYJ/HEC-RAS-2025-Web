using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib.Utilities;

public class Form
{
	public static string MillisecondsToString(int milliseconds)
	{
		double scaledMilliseconds = (double)milliseconds / 1000.0;
		if (scaledMilliseconds < 1.0)
		{
			return Strings.Format(scaledMilliseconds, "0.00") + " sec";
		}
		if (scaledMilliseconds < 10.0)
		{
			return Strings.Format(scaledMilliseconds, "0.0") + " sec";
		}
		if (scaledMilliseconds < 60.0)
		{
			return Strings.Format(scaledMilliseconds, "0") + " sec";
		}
		return Strings.Format(scaledMilliseconds / 60.0, "0.0") + " min";
	}

	public static DateTime StringToDateTime(string sDate)
	{
		CultureInfo provider = new CultureInfo("en-US");
		DateTimeStyles style = DateTimeStyles.None;
		if (sDate.EndsWith("2400"))
		{
			return StringToDateTime(sDate.Substring(0, checked(sDate.Length - 4)) + "0000").AddDays(1.0);
		}
		if (DateTime.TryParseExact(sDate, "ddMMMyyyy HHmm", provider, style, out var result))
		{
			return result;
		}
		return DateTime.MinValue;
	}

	public static string ElapsedTimeToString(Stopwatch stopwatch)
	{
		return ElapsedTimeToString(stopwatch.Elapsed);
	}

	public static string ElapsedTimeToString(TimeSpan ts)
	{
		return string.Format("{0:00}:{1:00}:{2:00}.{3:000}", new object[4]
		{
			System.Math.Floor(ts.TotalHours),
			ts.Minutes,
			ts.Seconds,
			ts.Milliseconds
		});
	}

	public static List<string> DataTableColumns(DataTable dt)
	{
		List<string> stringList = new List<string>();
		foreach (DataColumn column in dt.Columns)
		{
			stringList.Add(column.ColumnName);
		}
		return stringList;
	}

	public static bool DataRowItemExists(DataRow row, int c)
	{
		if (c == -1 || row[c] is DBNull)
		{
			return false;
		}
		return true;
	}

	public static string DataRowItemToString(DataRow row, int c)
	{
		if (c == -1 || row[c] is DBNull)
		{
			return "";
		}
		return RuntimeHelpers.GetObjectValue(row[c]).ToString();
	}

	public static int DataRowItemToInteger(DataRow row, int c, int defaultValue = int.MinValue)
	{
		if (c == -1 || row[c] is DBNull)
		{
			return defaultValue;
		}
		return Conversions.ToInteger(row[c]);
	}

	public static double DataRowItemToDouble(DataRow row, int c, double fallbackValue = double.NaN)
	{
		if (c == -1 || row[c] is DBNull)
		{
			return fallbackValue;
		}
		return Conversions.ToDouble(row[c]);
	}

	public static float DataRowItemToSingle(DataRow row, int c, float fallbackValue = float.NaN)
	{
		if (c == -1 || row[c] is DBNull)
		{
			return fallbackValue;
		}
		return Conversions.ToSingle(row[c]);
	}

	public static string ListToCSS(List<string> stringList)
	{
		if (stringList == null)
		{
			return "";
		}
		StringBuilder stringBuilder = new StringBuilder();
		string expression = "";
		foreach (string textValue in stringList)
		{
			if (Strings.Len(expression) > 0)
			{
				stringBuilder.Append(",");
			}
			stringBuilder.Append(textValue);
		}
		return stringBuilder.ToString();
	}

	public static string ListToCrSS(List<string> stringList)
	{
		if (stringList == null)
		{
			return "";
		}
		StringBuilder stringBuilder = new StringBuilder();
		foreach (string textValue in stringList)
		{
			stringBuilder.AppendLine(textValue);
		}
		return stringBuilder.ToString();
	}

	public static List<string> CSSToStringList(string csbuffer)
	{
		string[] parts = csbuffer.Split(',');
		List<string> stringList = new List<string>();
		string[] partsArray = parts;
		foreach (string part in partsArray)
		{
			stringList.Add(part);
		}
		return stringList;
	}

	public static double[] CSSToDoubleArray(string csbuffer)
	{
		string[] parts = csbuffer.Split(',');
		if (parts.Count() == 0)
		{
			double[] doubleValues = new double[1];
			double.TryParse(csbuffer, out doubleValues[0]);
			return doubleValues;
		}
		checked
		{
			double[] doubleValuesArray = new double[parts.Count() - 1 + 1];
			int count = parts.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				double.TryParse(parts[i], out doubleValuesArray[i]);
			}
			return doubleValuesArray;
		}
	}

	public static byte[] CSSToByteArray(string csbuffer)
	{
		string[] parts = csbuffer.Split(',');
		if (parts.Count() == 0)
		{
			byte[] byteValues = new byte[1];
			byte.TryParse(csbuffer, out byteValues[0]);
			return byteValues;
		}
		checked
		{
			byte[] byteValuesArray = new byte[parts.Count() - 1 + 1];
			int count = parts.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				byte.TryParse(parts[i], out byteValuesArray[i]);
			}
			return byteValuesArray;
		}
	}

	public static int[] CSSToIntArray(string csbuffer)
	{
		string[] parts = csbuffer.Split(',');
		if (parts.Count() == 0)
		{
			int[] intValues = new int[1];
			int.TryParse(csbuffer, out intValues[0]);
			return intValues;
		}
		checked
		{
			int[] intValuesArray = new int[parts.Count() - 1 + 1];
			int count = parts.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				int.TryParse(parts[i], out intValuesArray[i]);
			}
			return intValuesArray;
		}
	}

	public static Color[] CSSToColorArray(string csbuffer)
	{
		if (string.IsNullOrEmpty(csbuffer))
		{
			return null;
		}
		string[] parts = csbuffer.Split(',');
		checked
		{
			Color[] colors = new Color[parts.Count() - 1 + 1];
			int count = parts.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				int.TryParse(parts[i], out var result);
				colors[i] = Color.FromArgb(result);
			}
			return colors;
		}
	}

	public static string ArrayToCSS<T>(T[] a)
	{
		checked
		{
			if (a.Count() > 0)
			{
				StringBuilder stringBuilder = new StringBuilder();
				int count = a.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					if (i > 0)
					{
						stringBuilder.Append(",");
					}
					stringBuilder.Append(a[i].ToString());
				}
				return stringBuilder.ToString();
			}
			return "";
		}
	}

	public static string ArrayToCSS(Color[] a)
	{
		checked
		{
			if (a.Count() > 0)
			{
				StringBuilder stringBuilder = new StringBuilder();
				int count = a.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					if (i > 0)
					{
						stringBuilder.Append(",");
					}
					stringBuilder.Append(a[i].ToArgb().ToString());
				}
				return stringBuilder.ToString();
			}
			return "";
		}
	}

	public static string ArrayToCSS(ICollection<Color> a)
	{
		checked
		{
			if (a.Count > 0)
			{
				StringBuilder stringBuilder = new StringBuilder();
				int count = a.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					if (i > 0)
					{
						stringBuilder.Append(",");
					}
					stringBuilder.Append(a.ElementAtOrDefault(i).ToArgb().ToString());
				}
				return stringBuilder.ToString();
			}
			return "";
		}
	}

	public static string ArrayToCSS(string[] a)
	{
		checked
		{
			if (a.Count() > 0)
			{
				StringBuilder stringBuilder = new StringBuilder();
				int count = a.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					if (i > 0)
					{
						stringBuilder.Append(",");
					}
					stringBuilder.Append(a[i]);
				}
				return stringBuilder.ToString();
			}
			return "";
		}
	}
}
