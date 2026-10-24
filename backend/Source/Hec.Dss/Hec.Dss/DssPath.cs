using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Hec.Dss;

[DebuggerDisplay("{FullPath}")]
public class DssPath : IComparable<DssPath>
{
	public class DatelessComparer : IEqualityComparer<DssPath>
	{
		public bool Equals(DssPath x, DssPath y)
		{
			if (string.Compare(x.Apart, y.Apart, ignoreCase: true) == 0 && string.Compare(x.Bpart, y.Bpart, ignoreCase: true) == 0 && string.Compare(x.Cpart, y.Cpart, ignoreCase: true) == 0 && CompareEParts(x.Epart, y.Epart))
			{
				return string.Compare(x.Fpart, y.Fpart, ignoreCase: true) == 0;
			}
			return false;
		}

		public int GetHashCode(DssPath obj)
		{
			return CombineHashCodes<string>(obj.Apart, obj.Bpart, obj.Cpart, obj.Epart, obj.Fpart);
		}

		public static int CombineHashCodes<T>(params T[] inputs)
		{
			int hashCodeTerm = 5381;
			for (int i = 0; i < inputs.Length; i++)
			{
				T input = inputs[i];
				int hashCode = input.GetHashCode();
				hashCodeTerm = ((hashCodeTerm << 5) + hashCodeTerm) ^ hashCode;
			}
			return hashCodeTerm;
		}
	}

	private class PathComparer : IComparer<DssPath>
	{
		private const int ExpectedCollectionLen = 18;

		public int Compare(DssPath x, DssPath y)
		{
			int apartTerm = x.Apart.CompareTo(y.Apart);
			if (apartTerm != 0)
			{
				return apartTerm;
			}
			apartTerm = x.Bpart.CompareTo(y.Bpart);
			if (apartTerm != 0)
			{
				return apartTerm;
			}
			apartTerm = x.Cpart.CompareTo(y.Cpart);
			if (apartTerm != 0)
			{
				return apartTerm;
			}
			DateTime dPartAsDateTime = x.DPartAsDateTime;
			DateTime dPartAsDateTime2 = y.DPartAsDateTime;
			if (dPartAsDateTime == default(DateTime) || dPartAsDateTime2 == default(DateTime))
			{
				apartTerm = x.Dpart.CompareTo(y.Dpart);
				if (apartTerm != 0)
				{
					return apartTerm;
				}
			}
			else
			{
				apartTerm = dPartAsDateTime.CompareTo(dPartAsDateTime2);
				if (apartTerm != 0)
				{
					return apartTerm;
				}
			}
			apartTerm = x.Epart.CompareTo(y.Epart);
			if (apartTerm != 0)
			{
				return apartTerm;
			}
			if (!x.Fpart.StartsWith("C:") || x.Fpart.Length != 18)
			{
				return x.Fpart.CompareTo(y.Fpart);
			}
			apartTerm = string.Compare(x.Fpart, 11, y.Fpart, 11, 7);
			if (apartTerm != 0)
			{
				return apartTerm;
			}
			return string.Compare(x.Fpart, 2, y.Fpart, 2, 6);
		}

		private string CollectionSortable(DssPath x)
		{
			string apartBpartTerm = x.Apart + x.Bpart + x.Cpart + x.SortableDPart + x.Epart;
			if (!x.Fpart.StartsWith("C:"))
			{
				return apartBpartTerm;
			}
			string[] parts = x.Fpart.Split('|');
			if (parts.Length != 2)
			{
				return x.PathWithoutDate;
			}
			return apartBpartTerm + parts[1].Split(':')[1] + parts[0].Split(':')[1];
		}
	}

	private string _Apart;

	private string _Bpart;

	private string _Cpart;

	private string _Dpart;

	private string _Epart;

	private string _Fpart;

	public static DssPath NotFound = new DssPath();

	private static string[] minutesV7 = new string[11]
	{
		"30Minute", "20Minute", "15Minute", "12Minute", "10Minute", "6Minute", "5Minute", "4Minute", "3Minute", "2Minute",
		"1Minute"
	};

	private static string[] minutesV6 = new string[11]
	{
		"30MIN", "20MIN", "15MIN", "12MIN", "10MIN", "6MIN", "5MIN", "4MIN", "3MIN", "2MIN",
		"1MIN"
	};

	private const int partEMinLength = 4;

	private const int partEMaxLength = 8;

	public RecordType RecordType { get; internal set; }

	public DateTime DPartAsDateTime
	{
		get
		{
			if (Time.TryConvertFromHecDateTime(Dpart, out var date))
			{
				return date;
			}
			return default;
		}
	}

	public DateTime EPartAsDateTime
	{
		get
		{
			if (Time.TryConvertFromHecDateTime(Epart, out var date))
			{
				return date;
			}
			return default;
		}
	}

	public bool IsRegular
	{
		get
		{
			if (Epart.StartsWith("IR-", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			if (Epart.StartsWith("~", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			return true;
		}
	}

	public string PathWithoutDate => "/" + Apart + "/" + Bpart + "/" + Cpart + "//" + Epart + "/" + Fpart + "/";

	internal string PathWithoutRange
	{
		get
		{
			string dpart = Dpart;
			int indexOf = dpart.IndexOf("-");
			if (indexOf > 0)
			{
				dpart = dpart.Substring(0, indexOf);
			}
			return "/" + Apart + "/" + Bpart + "/" + Cpart + "/" + dpart + "/" + Epart + "/" + Fpart + "/";
		}
	}

	public virtual string SortableDPart
	{
		get
		{
			if (!Time.TryConvertFromHecDateTime(Dpart, out var date))
			{
				return Dpart;
			}
			return date.ToString("yyyyMMdd");
		}
	}

	public string Apart
	{
		get
		{
			return _Apart;
		}
		set
		{
			_Apart = value;
		}
	}

	public string Bpart
	{
		get
		{
			return _Bpart;
		}
		set
		{
			_Bpart = value;
		}
	}

	public string Cpart
	{
		get
		{
			return _Cpart;
		}
		set
		{
			_Cpart = value;
		}
	}

	public string Dpart
	{
		get
		{
			return _Dpart;
		}
		set
		{
			_Dpart = value;
		}
	}

	public string Epart
	{
		get
		{
			return _Epart;
		}
		set
		{
			_Epart = value;
		}
	}

	public string Fpart
	{
		get
		{
			return _Fpart;
		}
		set
		{
			_Fpart = value;
		}
	}

	public string FullPath => "/" + _Apart.Trim() + "/" + _Bpart.Trim() + "/" + _Cpart.Trim() + "/" + _Dpart.Trim() + "/" + _Epart.Trim() + "/" + _Fpart.Trim() + "/";

	public bool TryParseDPartAsDateTime(out DateTime date)
	{
		return Time.TryConvertFromHecDateTime(_Dpart, out date);
	}

	public bool TryParseEPartAsDateTime(out DateTime date)
	{
		return Time.TryConvertFromHecDateTime(_Epart, out date);
	}

	public bool DPartIsDate()
	{
		return DPartAsDateTime != default(DateTime);
	}

	public bool DPartIsDateOrDateRange()
	{
		if (DPartIsDate())
		{
			return true;
		}
		string[] parts = Dpart.Split('-');
		if (parts.Length == 2)
		{
			if (Time.TryConvertFromHecDateTime(parts[0], out var date))
			{
				return Time.TryConvertFromHecDateTime(parts[1], out date);
			}
			return false;
		}
		return false;
	}

	public override string ToString()
	{
		return FullPath;
	}

	public static bool EqualsDateLess(DssPath x, DssPath y)
	{
		return new DatelessComparer().Equals(x, y);
	}

	public DssPath()
	{
		InitPath("", "", "", "", "", "", RecordType.Unknown);
	}

	public DssPath(string A = "", string B = "", string C = "", string D = "", string E = "", string F = "", RecordType recordType = RecordType.Unknown)
	{
		InitPath(A, B, C, D, E, F, recordType);
	}

	private void InitPath(string A, string B, string C, string D, string E, string F, RecordType recordType)
	{
		Apart = A;
		Bpart = B;
		Cpart = C;
		Dpart = D;
		Epart = E;
		Fpart = F;
		RecordType = recordType;
	}

	public DssPath(string A = "", string B = "", string C = "", DateTime D = default(DateTime), string E = "", string F = "", RecordType recordType = RecordType.Unknown)
	{
		InitPath(A, B, C, D.ToString("yyyyMMdd"), E, F, recordType);
	}

	public bool IsTimeSeries()
	{
		if (RecordType == RecordType.RegularTimeSeries || RecordType == RecordType.IrregularTimeSeries || RecordType == RecordType.RegularTimeSeriesProfile)
		{
			return true;
		}
		if (RecordType == RecordType.Unknown && (Dpart.Trim() == "" || DPartIsDateOrDateRange()) && TimeWindow.SecondsInInterval(this) != 0)
		{
			return true;
		}
		return false;
	}

	public DssPath(string path)
	{
		if (path != null)
		{
			if (!path.StartsWith("/"))
			{
				path = "/" + path;
			}
			if (!path.EndsWith("/"))
			{
				path += "/";
			}
			string[] parts = path.Split('/');
			if (parts.Length != 8)
			{
				throw new Exception("Invalid path given");
			}
			InitPath(parts[1], parts[2], parts[3], parts[4], parts[5], parts[6], RecordType.Unknown);
		}
	}

	public static bool IsValid(string path)
	{
		if (path.Split('/').Length != 8)
		{
			return false;
		}
		return true;
	}

	public static bool TryParse(string pathname, ref DssPath dssPath)
	{
		if (!pathname.StartsWith("/"))
		{
			pathname = "/" + pathname;
		}
		if (!pathname.EndsWith("/"))
		{
			pathname += "/";
		}
		if (pathname.Split('/').Length != 8)
		{
			return false;
		}
		dssPath = new DssPath(pathname);
		return true;
	}

	public bool IsDPartARange()
	{
		return _Dpart.Split('-').Length == 2;
	}

	public bool IsEPartADate()
	{
		DateTime result;
		return DateTime.TryParse(_Epart, out result);
	}

	public DateTime GetDPartAsDateTime()
	{
		if (IsDPartARange())
		{
			return Time.ConvertFromHecDateTime(_Dpart.Split('-')[0]);
		}
		return Time.ConvertFromHecDateTime(_Dpart);
	}

	public DateTime GetEPartAsDateTime()
	{
		return Time.ConvertFromHecDateTime(_Epart);
	}

	public int CompareTo(DssPath y)
	{
		int apartTerm = Apart.CompareTo(y.Apart);
		if (apartTerm != 0)
		{
			return apartTerm;
		}
		apartTerm = Bpart.CompareTo(y.Bpart);
		if (apartTerm != 0)
		{
			return apartTerm;
		}
		apartTerm = Cpart.CompareTo(y.Cpart);
		if (apartTerm != 0)
		{
			return apartTerm;
		}
		DateTime dPartAsDateTime = DPartAsDateTime;
		DateTime dPartAsDateTime2 = y.DPartAsDateTime;
		if (dPartAsDateTime == default(DateTime) || dPartAsDateTime2 == default(DateTime))
		{
			apartTerm = Dpart.CompareTo(y.Dpart);
			if (apartTerm != 0)
			{
				return apartTerm;
			}
		}
		else
		{
			apartTerm = dPartAsDateTime.CompareTo(dPartAsDateTime2);
			if (apartTerm != 0)
			{
				return apartTerm;
			}
		}
		apartTerm = Epart.CompareTo(y.Epart);
		if (apartTerm != 0)
		{
			return apartTerm;
		}
		if (!Fpart.StartsWith("C:") || Fpart.Length != 18)
		{
			return Fpart.CompareTo(y.Fpart);
		}
		apartTerm = string.Compare(Fpart, 11, y.Fpart, 11, 7);
		if (apartTerm != 0)
		{
			return apartTerm;
		}
		return string.Compare(Fpart, 2, y.Fpart, 2, 6);
	}

	private static bool CompareEParts(string x, string y)
	{
		if (string.Compare(x, y, ignoreCase: true) == 0)
		{
			return true;
		}
		if (string.IsNullOrEmpty(x) || string.IsNullOrEmpty(y))
		{
			return false;
		}
		if (x.Length < 4 || y.Length < 4)
		{
			return false;
		}
		if (x.Length > 8 || y.Length > 8)
		{
			return false;
		}
		int index = Array.FindIndex(minutesV6, (string t) => t.Equals(x, StringComparison.InvariantCultureIgnoreCase));
		if (index >= 0)
		{
			return Array.FindIndex(minutesV7, (string t) => t.Equals(y, StringComparison.InvariantCultureIgnoreCase)) == index;
		}
		int index2 = Array.FindIndex(minutesV7, (string t) => t.Equals(x, StringComparison.InvariantCultureIgnoreCase));
		if (index2 >= 0)
		{
			return Array.FindIndex(minutesV6, (string t) => t.Equals(y, StringComparison.InvariantCultureIgnoreCase)) == index2;
		}
		return false;
	}
}
