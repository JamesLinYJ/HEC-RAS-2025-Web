using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace Hec.Dss;

public class DssPathCollection : IList<DssPath>, ICollection<DssPath>, IEnumerable<DssPath>, IEnumerable
{
	public IList<DssPath> Paths = new List<DssPath>();

	private IList<string> _aParts;

	private IList<string> _bParts;

	private IList<string> _cParts;

	private IList<string> _dParts;

	private IList<string> _eParts;

	private IList<string> _fParts;

	internal IList<RecordType> _recordTypeNames;

	internal IList<string> _dataUnits;

	internal IList<string> _dataTypes;

	internal IList<double> _xs;

	internal IList<double> _ys;

	internal IList<double> _zs;

	internal bool _isInterned;

	public string Filename { get; private set; }

	public DateTime FileDateTime { get; private set; }

	public bool HasRecordTypes { get; internal set; }

	public int Count => Paths.Count;

	public bool IsReadOnly
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public DssPath this[int index]
	{
		get
		{
			return Paths[index];
		}
		set
		{
			Paths[index] = value;
		}
	}

	public IList<DssPathCondensed> CondensedPaths { get; private set; }

	public IList<DssPath> UnCondensedPaths { get; private set; }

	public DataTable ToDataTable()
	{
		DataTable dataTable = new DataTable();
		dataTable.Columns.Add("index", typeof(int));
		dataTable.Columns.Add("A");
		dataTable.Columns.Add("B");
		dataTable.Columns.Add("C");
		dataTable.Columns.Add("D");
		dataTable.Columns.Add("E");
		dataTable.Columns.Add("F");
		dataTable.Columns.Add("RecordType");
		dataTable.PrimaryKey = new DataColumn[1] { dataTable.Columns[0] };
		for (int i = 0; i < Paths.Count; i++)
		{
			DssPath dssPath = Paths[i];
			dataTable.Rows.Add(i, dssPath.Apart, dssPath.Bpart, dssPath.Cpart, dssPath.Dpart, dssPath.Epart, dssPath.Fpart, dssPath.RecordType.ToString());
		}
		return dataTable;
	}

	internal DssPathCollection(string fileName, string[] strPaths, RecordType[] recordTypes, bool condensed)
	{
		Filename = fileName;
		if (File.Exists(Filename))
		{
			FileDateTime = File.GetLastWriteTime(Filename);
		}
		List<DssPath> unCondensedPaths = new List<DssPath>();
		Paths = new List<DssPath>();
		HashSet<string> stringSet = new HashSet<string>();
		HashSet<string> stringSetSet = new HashSet<string>();
		HashSet<string> stringSet2 = new HashSet<string>();
		HashSet<string> stringSet3 = new HashSet<string>();
		HashSet<string> stringSet4 = new HashSet<string>();
		HashSet<string> stringSet5 = new HashSet<string>();
		HashSet<RecordType> recordTypesSet = new HashSet<RecordType>();
		if (recordTypes == null)
		{
			HasRecordTypes = false;
		}
		else
		{
			HasRecordTypes = true;
		}
		for (int i = 0; i < strPaths.Length; i++)
		{
			string[] parts = strPaths[i].Split('/');
			string actualValue = parts[1];
			string actualValue2 = parts[2];
			string actualValue3 = parts[3];
			string actualValue4 = parts[4];
			string actualValue5 = parts[5];
			string actualValue6 = parts[6];
			if (!stringSet.Contains(actualValue))
			{
				stringSet.Add(actualValue);
			}
			if (!stringSetSet.Contains(actualValue2))
			{
				stringSetSet.Add(actualValue2);
			}
			if (!stringSet2.Contains(actualValue3))
			{
				stringSet2.Add(actualValue3);
			}
			if (!stringSet3.Contains(actualValue4))
			{
				stringSet3.Add(actualValue4);
			}
			if (!stringSet4.Contains(actualValue5))
			{
				stringSet4.Add(actualValue5);
			}
			if (!stringSet5.Contains(actualValue6))
			{
				stringSet5.Add(actualValue6);
			}
			stringSet.TryGetValue(actualValue, out actualValue);
			stringSetSet.TryGetValue(actualValue2, out actualValue2);
			stringSet2.TryGetValue(actualValue3, out actualValue3);
			stringSet3.TryGetValue(actualValue4, out actualValue4);
			stringSet4.TryGetValue(actualValue5, out actualValue5);
			stringSet5.TryGetValue(actualValue6, out actualValue6);
			DssPath dssPath = new DssPath(actualValue, actualValue2, actualValue3, actualValue4, actualValue5, actualValue6);
			if (!HasRecordTypes || i >= recordTypes.Length)
			{
				dssPath.RecordType = RecordType.Unknown;
			}
			else
			{
				if (!recordTypesSet.Contains(recordTypes[i]))
				{
					recordTypesSet.Add(recordTypes[i]);
				}
				recordTypesSet.TryGetValue(recordTypes[i], out var actualValue7);
				dssPath.RecordType = actualValue7;
			}
			unCondensedPaths.Add(dssPath);
		}
		_aParts = stringSet.ToList();
		(_aParts as List<string>).Sort();
		_bParts = stringSetSet.ToList();
		(_bParts as List<string>).Sort();
		_cParts = stringSet2.ToList();
		(_cParts as List<string>).Sort();
		_dParts = stringSet3.ToList();
		(_dParts as List<string>).Sort();
		_eParts = stringSet4.ToList();
		(_eParts as List<string>).Sort();
		_fParts = stringSet5.ToList();
		(_fParts as List<string>).Sort();
		_recordTypeNames = recordTypesSet.ToList();
		(_recordTypeNames as List<RecordType>).Sort();
		UnCondensedPaths = unCondensedPaths;
		CondensedPaths = new List<DssPathCondensed>();
		if (!condensed)
		{
			Paths = UnCondensedPaths;
			GetCondensedPaths(unCondensedPaths);
			return;
		}
		GetCondensedPaths(unCondensedPaths);
		CondensedPaths = CondensedPaths.OrderBy((DssPathCondensed q) => q).ToList();
		Paths = Paths.OrderBy((DssPath q) => q).ToList();
		_isInterned = true;
	}

	private void GetCondensedPaths(List<DssPath> unsorted)
	{
		List<DssPath> dssPaths = new List<DssPath>();
		List<DssPath> dssPathsList = new List<DssPath>();
		foreach (DssPath unsorted2 in unsorted)
		{
			if (!unsorted2.IsTimeSeries())
			{
				dssPaths.Add(unsorted2);
				Paths.Add(unsorted2);
			}
			else
			{
				dssPathsList.Add(unsorted2);
			}
		}
		foreach (List<DssPath> dssPaths2 in (from grp in dssPathsList.GroupBy((DssPath dssPath) => dssPath, new DssPath.DatelessComparer())
			select grp.OrderBy((DssPath dssp) => dssp.DPartAsDateTime).ToList()).ToList())
		{
			if (dssPaths2.Count == 1)
			{
				CondensedPaths.Add(new DssPathCondensed(dssPaths2[0].PathWithoutDate, new List<string> { dssPaths2[0].Dpart }));
				Paths.Add(dssPaths2[0]);
				continue;
			}
			DssPathCondensed dssPathCondensed = new DssPathCondensed(dssPaths2[0].Apart, dssPaths2[0].Bpart, dssPaths2[0].Cpart, dssPaths2.Select((DssPath path) => path.Dpart).ToList(), dssPaths2[0].Epart, dssPaths2[0].Fpart, dssPaths2[0].RecordType);
			CondensedPaths.Add(dssPathCondensed);
			Paths.Add(dssPathCondensed);
		}
		foreach (DssPath dssPath2 in dssPaths)
		{
			CondensedPaths.Add(new DssPathCondensed(dssPath2.Apart, dssPath2.Bpart, dssPath2.Cpart, new List<string> { dssPath2.Dpart }, dssPath2.Epart, dssPath2.Fpart, dssPath2.RecordType));
		}
	}

	public DssPathCollection(string fileName, IList<DssPath> paths, IList<string> uniqueAs = null, IList<string> uniqueBs = null, IList<string> uniqueCs = null, IList<string> uniqueDs = null, IList<string> uniqueEs = null, IList<string> uniqueFs = null, IList<RecordType> uniqueRecordTypeNames = null, IList<string> uniqueDataTypes = null, IList<string> uniqueDataUnits = null, IList<double> uniqueXs = null, IList<double> uniqueYs = null, IList<double> uniqueZs = null)
	{
		Filename = fileName;
		if (File.Exists(Filename))
		{
			FileDateTime = File.GetLastWriteTime(Filename);
		}
		if (uniqueAs != null && uniqueBs != null && uniqueCs != null && uniqueDs != null && uniqueEs != null && uniqueFs != null)
		{
			_isInterned = true;
		}
		_aParts = uniqueAs;
		_bParts = uniqueBs;
		_cParts = uniqueCs;
		_dParts = uniqueDs;
		_eParts = uniqueEs;
		_fParts = uniqueFs;
		_recordTypeNames = uniqueRecordTypeNames;
		_dataUnits = uniqueDataUnits;
		_dataTypes = uniqueDataTypes;
		_xs = uniqueXs;
		_ys = uniqueYs;
		_zs = uniqueZs;
		Paths = paths;
		UnCondensedPaths = new List<DssPath>();
		CondensedPaths = new List<DssPathCondensed>();
	}

	public static void WriteToFile(DssPathCollection paths, string filename)
	{
		using StreamWriter streamWriter = new StreamWriter(filename);
		streamWriter.WriteLine("index,FullPath,PathWithoutDate");
		for (int i = 0; i < paths.Count; i++)
		{
			DssPath dssPath = paths.Paths[i];
			streamWriter.WriteLine(i + "," + dssPath.FullPath + ", " + dssPath.PathWithoutDate);
		}
	}

	public IList<string> GetUniqueAParts()
	{
		if (_aParts == null)
		{
			_aParts = PathAssist.GetUniqueAParts(Paths);
		}
		return _aParts;
	}

	public IList<string> GetUniqueBParts()
	{
		if (_bParts == null)
		{
			_bParts = PathAssist.GetUniqueBParts(Paths);
		}
		return _bParts;
	}

	public IList<string> GetUniqueCParts()
	{
		if (_cParts == null)
		{
			_cParts = PathAssist.GetUniqueCParts(Paths);
		}
		return _cParts;
	}

	public IList<string> GetUniqueDParts()
	{
		if (_dParts == null)
		{
			_dParts = PathAssist.GetUniqueDParts(Paths);
		}
		return _dParts;
	}

	public IList<string> GetUniqueEParts()
	{
		if (_eParts == null)
		{
			_eParts = PathAssist.GetUniqueEParts(Paths);
		}
		return _eParts;
	}

	public IList<string> GetUniqueFParts()
	{
		if (_fParts == null)
		{
			_fParts = PathAssist.GetUniqueFParts(Paths);
		}
		return _fParts;
	}

	public IList<RecordType> GetUniqueRecordTypeNames()
	{
		if (_recordTypeNames == null)
		{
			_recordTypeNames = PathAssist.GetUniqueRecordTypeNames(Paths);
		}
		return _recordTypeNames;
	}

	internal void SetUniqueRecordTypeNames(List<RecordType> uniqueRecordTypes)
	{
		_recordTypeNames = uniqueRecordTypes;
	}

	public List<DssPath> FilterByPart(string aPart = "", string bPart = "", string cPart = "", string dPart = "", string ePart = "", string fPart = "")
	{
		return PathAssist.FilterByPart(Paths, aPart, bPart, cPart, dPart, ePart, fPart);
	}

	public DssPath FindExactPath(string path)
	{
		for (int i = 0; i < Paths.Count; i++)
		{
			if (string.Compare(path, Paths[i].FullPath, ignoreCase: true) == 0)
			{
				return Paths[i];
			}
		}
		return DssPath.NotFound;
	}

	public DssPathCondensed GetCondensedPath(DssPath path)
	{
		List<DssPath> filterByPart = PathAssist.FilterByPart(this, path.Apart, path.Bpart, path.Cpart, "", path.Epart, path.Fpart);
		for (int i = 0; i < filterByPart.Count; i++)
		{
			string dpart = path.Dpart;
			if (path.Dpart == null)
			{
				dpart = "";
			}
			if (path.IsDPartARange())
			{
				dpart = dpart.Split('-')[0].Trim();
			}
			if (Time.TryConvertFromHecDateTime(dpart, out var date))
			{
				if (!(filterByPart[i] is DssPathCondensed))
				{
					List<string> stringList = new List<string>();
					stringList.Add(filterByPart[i].Dpart);
					return new DssPathCondensed(filterByPart[i].FullPath, stringList);
				}
				Tuple<DateTime, DateTime> dPartAsDateTimeRange = (filterByPart[i] as DssPathCondensed).GetDPartAsDateTimeRange();
				if (date >= dPartAsDateTimeRange.Item1 && date <= dPartAsDateTimeRange.Item2)
				{
					return filterByPart[i] as DssPathCondensed;
				}
			}
		}
		return DssPathCondensed.NotFound;
	}

	public bool TryGetCondensedPath(DssPath path, out DssPathCondensed resultPath)
	{
		resultPath = GetCondensedPath(path);
		if (resultPath == DssPathCondensed.NotFound)
		{
			return false;
		}
		return true;
	}

	public int IndexOf(DssPath dssPath)
	{
		return Paths.IndexOf(dssPath);
	}

	public void Insert(int index, DssPath dssPath)
	{
		Paths.Insert(index, dssPath);
	}

	public void RemoveAt(int index)
	{
		Paths.RemoveAt(index);
	}

	public void Add(DssPath dssPath)
	{
		Paths.Add(dssPath);
	}

	public void Clear()
	{
		Paths.Clear();
	}

	public bool Contains(DssPath dssPath)
	{
		return Paths.Contains(dssPath);
	}

	public void CopyTo(DssPath[] paths, int arrayIndex)
	{
		Paths.CopyTo(paths, arrayIndex);
	}

	public bool Remove(DssPath dssPath)
	{
		return Paths.Remove(dssPath);
	}

	public IEnumerator<DssPath> GetEnumerator()
	{
		return Paths.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return Paths.GetEnumerator();
	}
}
