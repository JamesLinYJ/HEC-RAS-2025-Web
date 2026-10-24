using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Hec.Dss;

public static class PathAssist
{
	public static List<string> FilterByPart(IList<string> pathList, DssPath pathFilter)
	{
		return FilterByPart(pathList, pathFilter.Apart, pathFilter.Bpart, pathFilter.Cpart, pathFilter.Dpart, pathFilter.Epart, pathFilter.Fpart);
	}

	public static List<string> FilterByPart(IList<string> ListOfPaths, string aPart = "", string bPart = "", string cPart = "", string dPart = "", string ePart = "", string fPart = "")
	{
		List<string> stringList = new List<string>();
		aPart = ((aPart == "") ? "(.*?)" : Regex.Escape(aPart));
		bPart = ((bPart == "") ? "(.*?)" : Regex.Escape(bPart));
		cPart = ((cPart == "") ? "(.*?)" : Regex.Escape(cPart));
		dPart = ((dPart == "") ? "(.*?)" : Regex.Escape(dPart));
		ePart = ((ePart == "") ? "(.*?)" : Regex.Escape(ePart));
		fPart = ((fPart == "") ? "(.*?)" : Regex.Escape(fPart));
		Regex regex = new Regex("/" + aPart + "/" + bPart + "/" + cPart + "/" + dPart + "/" + ePart + "/" + fPart + "/", RegexOptions.IgnoreCase);
		for (int i = 0; i < ListOfPaths.Count; i++)
		{
			Match match = regex.Match(ListOfPaths[i]);
			if (match.Success)
			{
				stringList.Add(match.Value);
			}
		}
		return stringList;
	}

	public static List<DssPath> FilterByPart(IList<DssPath> ListOfPaths, string aPart = "", string bPart = "", string cPart = "", string dPart = "", string ePart = "", string fPart = "")
	{
		List<DssPath> dssPaths = new List<DssPath>();
		aPart = ((aPart == "") ? "(.*?)" : Regex.Escape(aPart));
		bPart = ((bPart == "") ? "(.*?)" : Regex.Escape(bPart));
		cPart = ((cPart == "") ? "(.*?)" : Regex.Escape(cPart));
		dPart = ((dPart == "") ? "(.*?)" : Regex.Escape(dPart));
		ePart = ((ePart == "") ? "(.*?)" : Regex.Escape(ePart));
		fPart = ((fPart == "") ? "(.*?)" : Regex.Escape(fPart));
		Regex regex = new Regex("/" + aPart + "/" + bPart + "/" + cPart + "/" + dPart + "/" + ePart + "/" + fPart + "/", RegexOptions.IgnoreCase);
		for (int i = 0; i < ListOfPaths.Count; i++)
		{
			if (regex.Match(ListOfPaths[i].FullPath).Success)
			{
				dssPaths.Add(ListOfPaths[i]);
			}
		}
		return dssPaths;
	}

	public static List<string> GetUniqueAParts(IList<DssPath> ListOfPaths)
	{
		HashSet<string> stringSet = new HashSet<string>();
		foreach (DssPath ListOfPath in ListOfPaths)
		{
			stringSet.Add(ListOfPath.Apart);
		}
		return stringSet.ToList();
	}

	public static List<string> GetUniqueBParts(IList<DssPath> ListOfPaths)
	{
		HashSet<string> stringSet = new HashSet<string>();
		foreach (DssPath ListOfPath in ListOfPaths)
		{
			stringSet.Add(ListOfPath.Bpart);
		}
		return stringSet.ToList();
	}

	public static List<string> GetUniqueCParts(IList<DssPath> ListOfPaths)
	{
		HashSet<string> stringSet = new HashSet<string>();
		foreach (DssPath ListOfPath in ListOfPaths)
		{
			stringSet.Add(ListOfPath.Cpart);
		}
		return stringSet.ToList();
	}

	public static List<string> GetUniqueDParts(IList<DssPath> ListOfPaths)
	{
		HashSet<string> stringSet = new HashSet<string>();
		foreach (DssPath ListOfPath in ListOfPaths)
		{
			stringSet.Add(ListOfPath.Dpart);
		}
		return stringSet.ToList();
	}

	public static List<string> GetUniqueEParts(IList<DssPath> ListOfPaths)
	{
		HashSet<string> stringSet = new HashSet<string>();
		foreach (DssPath ListOfPath in ListOfPaths)
		{
			stringSet.Add(ListOfPath.Epart);
		}
		return stringSet.ToList();
	}

	public static List<string> GetUniqueFParts(IList<DssPath> ListOfPaths)
	{
		HashSet<string> stringSet = new HashSet<string>();
		foreach (DssPath ListOfPath in ListOfPaths)
		{
			stringSet.Add(ListOfPath.Fpart);
		}
		return stringSet.ToList();
	}

	public static List<RecordType> GetUniqueRecordTypeNames(IList<DssPath> ListOfPaths)
	{
		HashSet<RecordType> recordTypes = new HashSet<RecordType>();
		foreach (DssPath ListOfPath in ListOfPaths)
		{
			recordTypes.Add(ListOfPath.RecordType);
		}
		return recordTypes.ToList();
	}
}
