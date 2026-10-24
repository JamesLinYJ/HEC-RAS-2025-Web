using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib.Utilities;

public abstract class DSS
{
	public class CompareDSSDates : IComparer<string>
	{
		public int Compare(string t1, string t2)
		{
			DateTime t3 = ConvertFromDSSDateTime(t1);
			DateTime t4 = ConvertFromDSSDateTime(t2);
			return DateTime.Compare(t3, t4);
		}

		int IComparer<string>.Compare(string t1, string t2)
		{
			//ILSpy generated this explicit interface implementation from .override directive in Compare
			return this.Compare(t1, t2);
		}
	}

	public static int NearestDSSDateIndex(string[] dssDates, string dssDate)
	{
		int binarySearch = Array.BinarySearch(dssDates, dssDate, new CompareDSSDates());
		checked
		{
			if (binarySearch < 0)
			{
				binarySearch = ~binarySearch;
				if (binarySearch == dssDates.Count())
				{
					return dssDates.Count() - 1;
				}
				if (binarySearch == 0)
				{
					return 0;
				}
				DateTime dateTime = ConvertFromDSSDateTime(dssDate);
				DateTime dateTime2 = ConvertFromDSSDateTime(dssDates[binarySearch - 1]);
				DateTime dateTime3 = ConvertFromDSSDateTime(dssDates[binarySearch]);
				if (dateTime - dateTime2 < dateTime3 - dateTime)
				{
					return binarySearch - 1;
				}
				return binarySearch;
			}
			return binarySearch;
		}
	}

	public static DateTime ConvertFromDSSDateTime(string dateTime)
	{
		DateTime result2;
		try
		{
			DateTime dateTime2 = DateTime.MinValue;
			string trim = dateTime.Trim();
			trim = trim.Replace(" ", "");
			trim = trim.Replace(":", "");
			if (trim.Length >= 9)
			{
				dateTime2 = DateTime.Parse("1/1/" + trim.Substring(5, 4));
				switch (trim.Substring(2, 3).ToUpper())
				{
				case "FEB":
					dateTime2 = dateTime2.AddMonths(1);
					break;
				case "MAR":
					dateTime2 = dateTime2.AddMonths(2);
					break;
				case "APR":
					dateTime2 = dateTime2.AddMonths(3);
					break;
				case "MAY":
					dateTime2 = dateTime2.AddMonths(4);
					break;
				case "JUN":
					dateTime2 = dateTime2.AddMonths(5);
					break;
				case "JUL":
					dateTime2 = dateTime2.AddMonths(6);
					break;
				case "AUG":
					dateTime2 = dateTime2.AddMonths(7);
					break;
				case "SEP":
					dateTime2 = dateTime2.AddMonths(8);
					break;
				case "OCT":
					dateTime2 = dateTime2.AddMonths(9);
					break;
				case "NOV":
					dateTime2 = dateTime2.AddMonths(10);
					break;
				case "DEC":
					dateTime2 = dateTime2.AddMonths(11);
					break;
				}
				double parseSubstringTerm = double.Parse(trim.Substring(0, 2)) - 1.0;
				dateTime2 = dateTime2.AddDays(parseSubstringTerm);
				if (trim.Length >= 11)
				{
					double parse = double.Parse(trim.Substring(9, 2));
					dateTime2 = dateTime2.AddHours(parse);
				}
				if (trim.Length >= 13)
				{
					double parse2 = double.Parse(trim.Substring(11, 2));
					dateTime2 = dateTime2.AddMinutes(parse2);
				}
				if (trim.Length >= 15)
				{
					double parse3 = double.Parse(trim.Substring(13, 2));
					dateTime2 = dateTime2.AddSeconds(parse3);
				}
				if (trim.Length >= 18)
				{
					int result = 0;
					if (int.TryParse(trim.Substring(15, 3), out result))
					{
						dateTime2 = dateTime2.AddMilliseconds(result);
					}
				}
			}
			result2 = dateTime2;
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			result2 = DateTime.MinValue;
			ProjectData.ClearProjectError();
		}
		return result2;
	}

	public static string ConvertToDSSDateTime(DateTime dt)
	{
		if (dt.Hour == 0 && dt.Minute == 0 && DateTime.Compare(dt, DateTime.MinValue) != 0)
		{
			DateTime dateTime = dt.AddDays(-1.0);
			return Strings.Format(dateTime, "dd") + DSSMonth(dateTime) + Strings.Format(dateTime, "yyyy") + " 24:00:00";
		}
		return Strings.Format(dt, "dd") + DSSMonth(dt) + Strings.Format(dt, "yyyy HH:mm:ss");
	}

	public static string DSSMonth(DateTime dt)
	{
		return dt.Month switch
		{
			1 => "JAN", 
			2 => "FEB", 
			3 => "MAR", 
			4 => "APR", 
			5 => "MAY", 
			6 => "JUN", 
			7 => "JUL", 
			8 => "AUG", 
			9 => "SEP", 
			10 => "OCT", 
			11 => "NOV", 
			12 => "DEC", 
			_ => "", 
		};
	}
}
