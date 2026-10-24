using System;
using System.Collections.Generic;

namespace Hec.Dss;

public class DssPathCondensed : DssPath
{
	private List<string> _comprisedDParts;

	internal readonly string DPartStart;

	internal readonly string DPartEnd;

	public new static DssPathCondensed NotFound = new DssPathCondensed(DssPath.NotFound.FullPath, new List<string>());

	public List<string> ComprisedDParts => _comprisedDParts;

	public override string SortableDPart
	{
		get
		{
			if (!Time.TryConvertFromHecDateTime(DPartStart, out var date))
			{
				return Dpart;
			}
			return date.ToString("yyyyMMdd");
		}
	}

	public DssPathCondensed(string Path, List<string> comprisedDParts)
		: base(Path)
	{
		_comprisedDParts = comprisedDParts;
		if (_comprisedDParts.Count > 0)
		{
			DPartStart = _comprisedDParts[0];
			DPartEnd = _comprisedDParts[_comprisedDParts.Count - 1];
			if (_comprisedDParts.Count > 1)
			{
				Dpart = DPartStart + "-" + DPartEnd;
			}
			else
			{
				Dpart = DPartStart;
			}
		}
	}

	public DssPathCondensed(string A, string B, string C, List<string> comprisedDParts, string E, string F, RecordType recordType = RecordType.Unknown)
		: base(A, B, C, default(DateTime), E, F, recordType)
	{
		_comprisedDParts = comprisedDParts;
		DPartStart = _comprisedDParts[0];
		DPartEnd = _comprisedDParts[_comprisedDParts.Count - 1];
		if (_comprisedDParts.Count > 1)
		{
			Dpart = DPartStart + "-" + DPartEnd;
		}
		else
		{
			Dpart = DPartStart;
		}
	}

	public List<DssPath> GetComprisingDSSPaths()
	{
		List<DssPath> dssPaths = new List<DssPath>();
		for (int i = 0; i < _comprisedDParts.Count; i++)
		{
			DssPath dssPath = new DssPath(Apart, Bpart, Cpart, _comprisedDParts[i], Epart, Fpart);
			dssPath.RecordType = RecordType;
			dssPaths.Add(dssPath);
		}
		return dssPaths;
	}

	public DssPath GetPath(int index)
	{
		return new DssPath(Apart, Bpart, Cpart, _comprisedDParts[index], Epart, Fpart);
	}

	public Tuple<DateTime, DateTime> GetDPartAsDateTimeRange()
	{
		string[] parts = Dpart.Split('-');
		if (parts.Length == 2)
		{
			return new Tuple<DateTime, DateTime>(Time.ConvertFromHecDateTime(parts[0]), Time.ConvertFromHecDateTime(parts[1]));
		}
		if (parts.Length == 1)
		{
			return new Tuple<DateTime, DateTime>(Time.ConvertFromHecDateTime(parts[0]), default);
		}
		throw new Exception("Invalid D Part formatting");
	}
}
