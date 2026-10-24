using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Utility.DataStructures;

namespace Geospatial.Rasters.Classifications;

public class MergedClassification : IClassification
{
	private class IDCollection : IReadOnlyCollection<int>, IEnumerable<int>, IEnumerable
	{
		private readonly MergedClassification _cls;

		public int Count => _cls.Count;

		public IDCollection(MergedClassification cls)
		{
			_cls = cls;
		}

		public IEnumerator<int> GetEnumerator()
		{
			for (int i = 0; i < _cls.Count; i++)
			{
				yield return i;
			}
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}

	private class NameCollection : IReadOnlyCollection<string>, IEnumerable<string>, IEnumerable
	{
		private readonly MergedClassification _cls;

		public int Count => _cls.Count;

		public NameCollection(MergedClassification cls)
		{
			_cls = cls;
		}

		public IEnumerator<string> GetEnumerator()
		{
			for (int i = 0; i < _cls.Count; i++)
			{
				yield return _cls.GetName(i);
			}
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}

	public const string DefaultDelimeter = " : ";

	private static readonly string[] DelimitArr = new string[1] { " : " };

	private readonly IClassification _clsA;

	private readonly IClassification _clsB;

	private readonly Map<int, int> _clsMapA;

	private readonly Map<int, int> _clsMapB;

	public IClassification ClassificationA => _clsA;

	public IClassification ClassificationB => _clsB;

	private int NumRows => _clsA.Count;

	private int NumCols => _clsB.Count;

	public int Count => NumRows * NumCols;

	public IReadOnlyCollection<int> IDs { get; }

	public IReadOnlyCollection<string> Classes { get; }

	public MergedClassification(IClassification clsA, IClassification clsB)
	{
		_clsA = clsA;
		_clsB = clsB;
		_clsMapA = MakeConsecutiveClassMap(_clsA);
		_clsMapB = MakeConsecutiveClassMap(_clsB);
		IDs = new IDCollection(this);
		Classes = new NameCollection(this);
	}

	private static Map<int, int> MakeConsecutiveClassMap(IClassification cls)
	{
		Map<int, int> map = new Map<int, int>();
		map.Add(0, 0);
		int[] iDs = cls.IDs.ToArray();
		int unity = 1;
		for (int i = 0; i < cls.Count; i++)
		{
			int iDsIndex = iDs[i];
			if (iDsIndex != 0)
			{
				map.Add(iDsIndex, unity);
				unity++;
			}
		}
		return map;
	}

	public Dictionary<int, string> GetAllClassifications()
	{
		Dictionary<int, string> allClassifications = new Dictionary<int, string>();
		for (int i = 0; i < Count; i++)
		{
			allClassifications[i] = GetName(i);
		}
		return allClassifications;
	}

	public int GetID(string className)
	{
		if (!TrySplitName(className, out var name, out var name2))
		{
			return -1;
		}
		int iD = _clsA.GetID(name);
		int iD2 = _clsB.GetID(name2);
		return GetOutputID(iD, iD2);
	}

	public int GetOutputID(int idA, int idB)
	{
		if (idA == -1 || idB == -1)
		{
			return -1;
		}
		int row = _clsMapA.Forward[idA];
		int col = _clsMapB.Forward[idB];
		return RasterDefinition.Compute1DIndex(NumCols, row, col);
	}

	public void Set(int id, string name)
	{
		throw new NotSupportedException("MergedClassification doesn't allow for adding new entries.");
	}

	public Classification ToClassification(string newDelimiter = " : ")
	{
		Dictionary<int, string> stringByInt = new Dictionary<int, string>();
		for (int i = 0; i < Count; i++)
		{
			stringByInt[i] = GetName(i).Replace(" : ", newDelimiter);
		}
		return new Classification(stringByInt);
	}

	public void GetInputIDs(int outputID, out int idA, out int idB)
	{
		if (outputID == 0)
		{
			idA = 0;
			idB = 0;
		}
		else
		{
			RasterDefinition.Compute2DIndex(NumCols, outputID, out var row, out var col);
			idA = _clsMapA.Backward[row];
			idB = _clsMapB.Backward[col];
		}
	}

	public string GetName(int id)
	{
		if (id == 0)
		{
			return "NoData";
		}
		GetInputIDs(id, out var idA, out var idB);
		if (idA == 0 && idB == 0)
		{
			return "NoData";
		}
		return MergeName(_clsA.GetName(idA), _clsB.GetName(idB));
	}

	public static string MergeName(string name1, string name2)
	{
		return name1 + " : " + name2;
	}

	public static bool TrySplitName(string mergedName, out string name1, out string name2)
	{
		string[] parts = mergedName.Split(DelimitArr, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length != 2)
		{
			name1 = string.Empty;
			name2 = string.Empty;
			return false;
		}
		name1 = parts[0];
		name2 = parts[1];
		return true;
	}
}
