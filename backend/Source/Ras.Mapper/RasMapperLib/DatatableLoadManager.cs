using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using H5Assist.Identifiers;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Extensions;

namespace RasMapperLib;

public class DatatableLoadManager
{
	public enum MemoryDiskMappingType
	{
		MemoryOnly,
		DiskOnly,
		MemoryAndDisk
	}

	public class MappedColumnDefinition
	{
		public string InMemoryName;

		public string DiskName;

		public MemoryDiskMappingType Mapping;

		public Type Type;

		public DataColumnViewState ViewState;

		public DataColumnGroup Group;

		public MappedColumnDefinition(string memoryColumnName, string diskName, Type colType, DataColumnGroup colGroup = null, DataColumnViewState viewState = DataColumnViewState.FreeRange)
		{
			ViewState = DataColumnViewState.FreeRange;
			Group = null;
			InMemoryName = memoryColumnName;
			DiskName = diskName;
			Type = colType;
			Group = colGroup;
			ViewState = viewState;
		}

		public bool IsEquivalent(MappedColumnDefinition other)
		{
			if (Operators.CompareString(InMemoryName, other.InMemoryName, TextCompare: false) == 0 && Operators.CompareString(DiskName, other.DiskName, TextCompare: false) == 0 && Mapping == other.Mapping)
			{
				return Type == other.Type;
			}
			return false;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__19_002D0
	{
		public MappedColumnDefinition col;

		public _Closure_0024__19_002D0(_Closure_0024__19_002D0 other)
		{
			if (other != null)
			{
				col = other.col;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(MappedColumnDefinition colMap)
		{
			return col.IsEquivalent(colMap);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__19_002D1
	{
		public MappedColumnDefinition col;

		public _Closure_0024__19_002D1(_Closure_0024__19_002D1 other)
		{
			if (other != null)
			{
				col = other.col;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__1(MappedColumnDefinition colMap)
		{
			return col.IsEquivalent(colMap);
		}
	}

	private List<MappedColumnDefinition> _columnList;

	private HDF5MemoryDiskMap _h5MemoryMap;

	public List<MappedColumnDefinition> ColumnList
	{
		get
		{
			List<MappedColumnDefinition> mappedColumnDefinitions = new List<MappedColumnDefinition>();
			checked
			{
				int count = _columnList.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					mappedColumnDefinitions.Add(new MappedColumnDefinition(_columnList[i].InMemoryName, _columnList[i].DiskName, _columnList[i].Type, _columnList[i].Group, _columnList[i].ViewState));
				}
				return mappedColumnDefinitions;
			}
		}
	}

	public DatatableLoadManager()
	{
		_columnList = new List<MappedColumnDefinition>();
		_h5MemoryMap = new HDF5MemoryDiskMap();
	}

	public void Add(string memoryColumnName, string diskColumnName, Type colType, bool outputMessageIfNotFound = false, DataColumnGroup colGroup = null, DataColumnViewState viewState = DataColumnViewState.FreeRange)
	{
		_h5MemoryMap.Add(memoryColumnName, diskColumnName, colType, outputMessageIfNotFound);
		_columnList.Add(new MappedColumnDefinition(memoryColumnName, diskColumnName, colType, colGroup, viewState));
	}

	public void Add(string columnName, Type colType, bool outputMessageIfNotFound = false, DataColumnGroup colGroup = null, DataColumnViewState viewState = DataColumnViewState.FreeRange)
	{
		_h5MemoryMap.Add(columnName, columnName, colType, outputMessageIfNotFound);
		_columnList.Add(new MappedColumnDefinition(columnName, columnName, colType, colGroup, viewState));
	}

	public void AddMemoryOnly(string memoryColumnName, Type colType, DataColumnGroup colGroup = null, DataColumnViewState viewState = DataColumnViewState.FreeRange)
	{
		_columnList.Add(new MappedColumnDefinition(memoryColumnName, "", colType, colGroup, viewState));
	}

	public void AddWriteOnly<T>(string diskColumnName, Func<int, T> writeValue) where T : struct
	{
		_h5MemoryMap.AddWriteOnly(diskColumnName, writeValue);
	}

	public void InitializeDataTable(DataTable dt)
	{
		checked
		{
			int count = _columnList.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				MappedColumnDefinition mappedColumnDefinition = _columnList[i];
				string inMemoryName = mappedColumnDefinition.InMemoryName;
				if (dt.Columns.IndexOf(inMemoryName) == -1)
				{
					dt.Columns.Add(new CustomDataColumn(inMemoryName, mappedColumnDefinition.Type, mappedColumnDefinition.ViewState, mappedColumnDefinition.Group));
				}
			}
		}
	}

	public void Read(H5Reader hr, string dataset, DataTable dt)
	{
		_h5MemoryMap.Read(hr, dataset, dt);
	}

	public void Write(H5Writer hw, string dataset, DataTable dt)
	{
		_h5MemoryMap.Write(hw, dataset, dt);
	}

	public void ReadColumns(H5Reader hr, string dataset)
	{
		if (!hr.ExistsAsDataset(dataset))
		{
			return;
		}
		Dictionary<CompoundFieldDefinition, Array> compoundElements = hr.ReadCompoundElements(dataset);
		if (compoundElements.Count <= 0)
		{
			return;
		}
		string[] strings = null;
		string[] stringsArray = null;
		string[] strings2 = null;
		int[] intValues = null;
		foreach (KeyValuePair<CompoundFieldDefinition, Array> compoundElement in compoundElements)
		{
			if (compoundElement.Key.Name.RoughlyEquals("InMemoryName"))
			{
				strings = (string[])compoundElement.Value;
			}
			else if (compoundElement.Key.Name.RoughlyEquals("DiskName"))
			{
				stringsArray = (string[])compoundElement.Value;
			}
			else if (compoundElement.Key.Name.RoughlyEquals("Type"))
			{
				strings2 = (string[])compoundElement.Value;
			}
			else if (compoundElement.Key.Name.RoughlyEquals("ViewState"))
			{
				intValues = (int[])compoundElement.Value;
			}
		}
		int count = checked(strings.Length - 1);
		for (int i = 0; i <= count; i = checked(i + 1))
		{
			AddColumn(new MappedColumnDefinition(strings[i], stringsArray[i], Type.GetType(strings2[i]), null, (DataColumnViewState)intValues[i]));
			_h5MemoryMap.Add(strings[i], stringsArray[i], Type.GetType(strings2[i]), outputMessageIfNotFound: false);
		}
	}

	public void WriteColumns(H5Writer hw, string dataset)
	{
		CompoundTypeDefinition compoundTypeDefinition = new CompoundTypeDefinition();
		int inMemoryNameLength = 0;
		int diskNameLength = 0;
		int fullNameLength = 0;
		checked
		{
			string[] strings = new string[_columnList.Count - 1 + 1];
			string[] stringsArray = new string[_columnList.Count - 1 + 1];
			string[] strings2 = new string[_columnList.Count - 1 + 1];
			int[] intValues = new int[_columnList.Count - 1 + 1];
			int count = _columnList.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (_columnList[i].InMemoryName.Length > inMemoryNameLength)
				{
					inMemoryNameLength = _columnList[i].InMemoryName.Length;
				}
				if (_columnList[i].DiskName.Length > diskNameLength)
				{
					diskNameLength = _columnList[i].DiskName.Length;
				}
				if (_columnList[i].Type.FullName.Length > fullNameLength)
				{
					fullNameLength = _columnList[i].Type.FullName.Length;
				}
				strings[i] = _columnList[i].InMemoryName;
				stringsArray[i] = _columnList[i].DiskName;
				strings2[i] = _columnList[i].Type.FullName;
				intValues[i] = unchecked((int)_columnList[i].ViewState);
			}
			compoundTypeDefinition.AddStringField("InMemoryName", inMemoryNameLength);
			compoundTypeDefinition.AddStringField("DiskName", diskNameLength);
			compoundTypeDefinition.AddStringField("Type", fullNameLength);
			compoundTypeDefinition.AddField<int>("ViewState");
			hw.Create1DCompoundDataset(dataset, _columnList.Count, compoundTypeDefinition);
			using H5Assist.VB.CompoundWriter compoundWriter = new H5Assist.VB.CompoundWriter(hw, dataset, _columnList.Count);
			compoundWriter.WriteColumn(0, strings);
			compoundWriter.WriteColumn(1, stringsArray);
			compoundWriter.WriteColumn(2, strings2);
			compoundWriter.WriteColumn(3, intValues);
		}
	}

	public DatatableLoadManager Merge(DatatableLoadManager dtManagerOther)
	{
		DatatableLoadManager datatableLoadManager = new DatatableLoadManager();
		checked
		{
			int count = _columnList.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				datatableLoadManager.AddColumn(_columnList[i]);
			}
			int count2 = dtManagerOther._columnList.Count - 1;
			for (int j = 0; j <= count2; j++)
			{
				datatableLoadManager.AddColumn(dtManagerOther._columnList[j]);
			}
			datatableLoadManager._h5MemoryMap = _h5MemoryMap.Merge(dtManagerOther._h5MemoryMap);
			return datatableLoadManager;
		}
	}

	private void AddColumn(MappedColumnDefinition col)
	{
		_columnList.Add(col);
	}

	public static DatatableLoadManager LoadFrom(H5Reader hr, string dataset)
	{
		if (!hr.ExistsAsDataset(dataset))
		{
			throw new ArgumentException(dataset + " not found in " + hr.Filename);
		}
		Dictionary<CompoundFieldDefinition, Array> compoundElements = hr.ReadCompoundElements(dataset);
		DatatableLoadManager datatableLoadManager = new DatatableLoadManager();
		foreach (KeyValuePair<CompoundFieldDefinition, Array> compoundElement in compoundElements)
		{
			datatableLoadManager.Add(compoundElement.Key.Name, compoundElement.Key.Type);
		}
		return datatableLoadManager;
	}

	public bool IsEquivalent(DatatableLoadManager dtmCompare, [Optional][DefaultParameterValue(null)] ref List<MappedColumnDefinition> onlyInBase, [Optional][DefaultParameterValue(null)] ref List<MappedColumnDefinition> onlyInNew)
	{
		onlyInBase = new List<MappedColumnDefinition>();
		checked
		{
			int count = _columnList.Count - 1;
			_Closure_0024__19_002D0 capturedLocals = default;
			for (int i = 0; i <= count; i++)
			{
				capturedLocals = new _Closure_0024__19_002D0(capturedLocals);
				capturedLocals.col = _columnList[i];
				if (dtmCompare._columnList.FirstOrDefault(capturedLocals._Lambda_0024__0) == null)
				{
					onlyInBase.Add(capturedLocals.col);
				}
			}
			onlyInNew = new List<MappedColumnDefinition>();
			int count2 = dtmCompare._columnList.Count - 1;
			_Closure_0024__19_002D1 capturedLocals2 = default;
			for (int j = 0; j <= count2; j++)
			{
				capturedLocals2 = new _Closure_0024__19_002D1(capturedLocals2);
				capturedLocals2.col = dtmCompare._columnList[j];
				if (_columnList.FirstOrDefault(capturedLocals2._Lambda_0024__1) == null)
				{
					onlyInNew.Add(capturedLocals2.col);
				}
			}
			if (onlyInBase.Count == 0)
			{
				return onlyInNew.Count == 0;
			}
			return false;
		}
	}
}
