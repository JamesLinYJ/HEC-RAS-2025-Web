using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using H5Assist.Identifiers;
using H5Assist.VB;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class HDF5MemoryDiskMap
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__14_002D0<_0024CLS0> where _0024CLS0 : struct
	{
		public string diskColumnName;

		public Func<int, _0024CLS0> writeValue;

		public _Closure_0024__14_002D0(_Closure_0024__14_002D0<_0024CLS0> other)
		{
			if (other != null)
			{
				diskColumnName = other.diskColumnName;
				writeValue = other.writeValue;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(DataTable dt, CompoundTypeDefinition ctd)
		{
			ctd.AddField<_0024CLS0>(diskColumnName);
		}

		[SpecialName]
		internal void _Lambda_0024__1(H5Assist.VB.CompoundWriter hcw, DataTable dt, Type type)
		{
			int columnIndex = hcw.DataType.ColumnIndex(diskColumnName);
			checked
			{
				if (columnIndex != -1)
				{
					_0024CLS0[] values = new _0024CLS0[dt.Rows.Count - 1 + 1];
					int countOffset = dt.Rows.Count - 1;
					for (int i = 0; i <= countOffset; i++)
					{
						values[i] = writeValue(i);
					}
					hcw.WriteColumn(columnIndex, values);
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__15_002D0
	{
		public Func<int, string> writeValue;

		public string diskColumnName;

		public _Closure_0024__15_002D0(_Closure_0024__15_002D0 other)
		{
			if (other != null)
			{
				writeValue = other.writeValue;
				diskColumnName = other.diskColumnName;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(DataTable dt, CompoundTypeDefinition ctd)
		{
			int ordinal = 0;
			checked
			{
				int countOffset = dt.Rows.Count - 1;
				for (int i = 0; i <= countOffset; i++)
				{
					ordinal = Math.Max(ordinal, writeValue(i).Length);
				}
				ctd.AddStringField(diskColumnName, ordinal);
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(H5Assist.VB.CompoundWriter hcw, DataTable dt, Type type)
		{
			int columnIndex = hcw.DataType.ColumnIndex(diskColumnName);
			checked
			{
				if (columnIndex != -1)
				{
					string[] strings = new string[dt.Rows.Count - 1 + 1];
					int countOffset = dt.Rows.Count - 1;
					for (int i = 0; i <= countOffset; i++)
					{
						strings[i] = writeValue(i);
					}
					hcw.WriteColumn(columnIndex, strings);
				}
			}
		}
	}

	private static Dictionary<Type, Func<object, string>> _serializers = new Dictionary<Type, Func<object, string>>();

	private static Dictionary<Type, Func<string, object>> _deserializers = new Dictionary<Type, Func<string, object>>();

	private static Dictionary<Type, int> _maxStringLengths = new Dictionary<Type, int>();

	private List<Action<DataTable, Dictionary<CompoundFieldDefinition, Array>>> _readExecutions;

	private List<Action<DataTable, CompoundTypeDefinition>> _preWriteExecutions;

	private List<Action<H5Assist.VB.CompoundWriter, DataTable, Type>> _writeExecutions;

	private List<string> _writeColumnNames;

	public HDF5MemoryDiskMap()
	{
		_readExecutions = new List<Action<DataTable, Dictionary<CompoundFieldDefinition, Array>>>();
		_preWriteExecutions = new List<Action<DataTable, CompoundTypeDefinition>>();
		_writeExecutions = new List<Action<H5Assist.VB.CompoundWriter, DataTable, Type>>();
		_writeColumnNames = new List<string>();
	}

	public static void SetTypeConverter<TMemory, TDisk>() where TDisk : struct
	{
		throw new NotImplementedException();
	}

	public static void SetTypeSerializer<TMemory>(Func<object, string> serializer, Func<string, object> deserializer, int maxStrLen = -1)
	{
		SetTypeSerializer(typeof(TMemory), serializer, deserializer, maxStrLen);
	}

	public static void SetTypeSerializer(Type t, Func<object, string> serializer, Func<string, object> deserializer, int maxStrLen = -1)
	{
		_serializers[t] = serializer;
		_deserializers[t] = deserializer;
		if (maxStrLen > 0)
		{
			_maxStringLengths[t] = maxStrLen;
		}
	}

	public void Add(string memoryColumnName, string diskColumnName, Type colType, bool outputMessageIfNotFound)
	{
		_readExecutions.Add([SpecialName] (DataTable dt, Dictionary<CompoundFieldDefinition, Array> columns) =>
		{
			TryFillField(dt, columns, memoryColumnName, diskColumnName, outputMessageIfNotFound);
		});
		_writeColumnNames.Add(diskColumnName);
		if (colType == typeof(int))
		{
			_preWriteExecutions.Add([SpecialName] (DataTable dt, CompoundTypeDefinition ctd) =>
			{
				ctd.AddField<int>(diskColumnName);
			});
			_writeExecutions.Add([SpecialName] (H5Assist.VB.CompoundWriter hcw, DataTable dt, Type type) =>
			{
				TryWriteField(dt, hcw, memoryColumnName, diskColumnName, 0);
			});
			return;
		}
		if (colType == typeof(short))
		{
			_preWriteExecutions.Add([SpecialName] (DataTable dt, CompoundTypeDefinition ctd) =>
			{
				ctd.AddField<short>(diskColumnName);
			});
			_writeExecutions.Add([SpecialName] (H5Assist.VB.CompoundWriter hcw, DataTable dt, Type type) =>
			{
				TryWriteField(dt, hcw, memoryColumnName, diskColumnName, (short)0);
			});
			return;
		}
		if (colType == typeof(byte))
		{
			_preWriteExecutions.Add([SpecialName] (DataTable dt, CompoundTypeDefinition ctd) =>
			{
				ctd.AddField<byte>(diskColumnName);
			});
			_writeExecutions.Add([SpecialName] (H5Assist.VB.CompoundWriter hcw, DataTable dt, Type type) =>
			{
				TryWriteField(dt, hcw, memoryColumnName, diskColumnName, (byte)0);
			});
			return;
		}
		if (colType == typeof(bool))
		{
			_preWriteExecutions.Add([SpecialName] (DataTable dt, CompoundTypeDefinition ctd) =>
			{
				ctd.AddField<bool>(diskColumnName);
			});
			_writeExecutions.Add([SpecialName] (H5Assist.VB.CompoundWriter hcw, DataTable dt, Type type) =>
			{
				TryWriteField(dt, hcw, memoryColumnName, diskColumnName, fallBackValue: false);
			});
			return;
		}
		if (colType == typeof(long))
		{
			_preWriteExecutions.Add([SpecialName] (DataTable dt, CompoundTypeDefinition ctd) =>
			{
				ctd.AddField<long>(diskColumnName);
			});
			_writeExecutions.Add([SpecialName] (H5Assist.VB.CompoundWriter hcw, DataTable dt, Type type) =>
			{
				TryWriteField(dt, hcw, memoryColumnName, diskColumnName, 0L);
			});
			return;
		}
		if (colType == typeof(float))
		{
			_preWriteExecutions.Add([SpecialName] (DataTable dt, CompoundTypeDefinition ctd) =>
			{
				ctd.AddField<float>(diskColumnName);
			});
			_writeExecutions.Add([SpecialName] (H5Assist.VB.CompoundWriter hcw, DataTable dt, Type type) =>
			{
				TryWriteField(dt, hcw, memoryColumnName, diskColumnName, float.NaN);
			});
			return;
		}
		if (colType == typeof(double))
		{
			_preWriteExecutions.Add([SpecialName] (DataTable dt, CompoundTypeDefinition ctd) =>
			{
				ctd.AddField<double>(diskColumnName);
			});
			_writeExecutions.Add([SpecialName] (H5Assist.VB.CompoundWriter hcw, DataTable dt, Type type) =>
			{
				TryWriteField(dt, hcw, memoryColumnName, diskColumnName, double.NaN);
			});
			return;
		}
		if (colType == typeof(string))
		{
			_preWriteExecutions.Add([SpecialName] (DataTable dt, CompoundTypeDefinition ctd) =>
			{
				ctd.AddStringField(diskColumnName, MaxStringLength(dt, memoryColumnName));
			});
			_writeExecutions.Add([SpecialName] (H5Assist.VB.CompoundWriter hcw, DataTable dt, Type type) =>
			{
				TryWriteStringField(dt, hcw, memoryColumnName, diskColumnName);
			});
			return;
		}
		if (_serializers.ContainsKey(colType))
		{
			_preWriteExecutions.Add([SpecialName] (DataTable dt, CompoundTypeDefinition ctd) =>
			{
				ctd.AddStringField(diskColumnName, MaxStringLength(dt, memoryColumnName));
			});
			_writeExecutions.Add([SpecialName] (H5Assist.VB.CompoundWriter hcw, DataTable dt, Type type) =>
			{
				TryWriteStringField(dt, hcw, memoryColumnName, diskColumnName);
			});
			return;
		}
		throw new NotImplementedException();
	}

	private void AutoInitializeSerializableEnums(Type colType)
	{
	}

	public void AddWriteOnly<T>(string diskColumnName, Func<int, T> writeValue) where T : struct
	{
		_Closure_0024__14_002D0<T> capturedLocals = default;
		_Closure_0024__14_002D0<T> capturedLocals2 = new _Closure_0024__14_002D0<T>(capturedLocals);
		capturedLocals2.diskColumnName = diskColumnName;
		capturedLocals2.writeValue = writeValue;
		_writeColumnNames.Add(capturedLocals2.diskColumnName);
		_preWriteExecutions.Add([SpecialName] (DataTable dt, CompoundTypeDefinition ctd) =>
		{
			ctd.AddField<T>(capturedLocals2.diskColumnName);
		});
		_writeExecutions.Add(checked([SpecialName] (H5Assist.VB.CompoundWriter hcw, DataTable dt, Type type) =>
		{
			int columnIndex = hcw.DataType.ColumnIndex(capturedLocals2.diskColumnName);
			if (columnIndex != -1)
			{
				T[] values = new T[dt.Rows.Count - 1 + 1];
				int count = dt.Rows.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					values[i] = capturedLocals2.writeValue(i);
				}
				hcw.WriteColumn(columnIndex, values);
			}
		}));
	}

	public void AddStringWriteOnly(string diskColumnName, Func<int, string> writeValue)
	{
		_Closure_0024__15_002D0 capturedLocals = default;
		_Closure_0024__15_002D0 capturedLocals2 = new _Closure_0024__15_002D0(capturedLocals);
		capturedLocals2.diskColumnName = diskColumnName;
		capturedLocals2.writeValue = writeValue;
		_writeColumnNames.Add(capturedLocals2.diskColumnName);
		checked
		{
			_preWriteExecutions.Add([SpecialName] (DataTable dt, CompoundTypeDefinition ctd) =>
			{
				int capturedLocals3 = 0;
				int count = dt.Rows.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					capturedLocals3 = Math.Max(capturedLocals3, capturedLocals2.writeValue(i).Length);
				}
				ctd.AddStringField(capturedLocals2.diskColumnName, capturedLocals3);
			});
			_writeExecutions.Add([SpecialName] (H5Assist.VB.CompoundWriter hcw, DataTable dt, Type type) =>
			{
				int columnIndex = hcw.DataType.ColumnIndex(capturedLocals2.diskColumnName);
				if (columnIndex != -1)
				{
					string[] strings = new string[dt.Rows.Count - 1 + 1];
					int count2 = dt.Rows.Count - 1;
					for (int i = 0; i <= count2; i++)
					{
						strings[i] = capturedLocals2.writeValue(i);
					}
					hcw.WriteColumn(columnIndex, strings);
				}
			});
		}
	}

	public void Read(H5Reader hr, string dataset, DataTable dt)
	{
		if (_readExecutions.Count == 0)
		{
			return;
		}
		int count = dt.Rows.Count;
		Dictionary<CompoundFieldDefinition, Array> compoundElements = hr.ReadCompoundElements(dataset);
		checked
		{
			if (compoundElements.Count > 0)
			{
				int length = compoundElements.First().Value.Length;
				if (length != count && count == 0)
				{
					int count2 = length - 1;
					for (int i = 0; i <= count2; i++)
					{
						dt.Rows.Add(dt.NewRow());
					}
				}
			}
			int count3 = _readExecutions.Count - 1;
			for (int j = 0; j <= count3; j++)
			{
				_readExecutions[j](dt, compoundElements);
			}
		}
	}

	public void Write(H5Writer hw, string dataset, DataTable dt)
	{
		if (dt.Rows.Count == 0 || (_preWriteExecutions.Count == 0 && _writeExecutions.Count == 0))
		{
			return;
		}
		CompoundTypeDefinition compoundTypeDefinition = new CompoundTypeDefinition();
		checked
		{
			int count = _preWriteExecutions.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				_preWriteExecutions[i](dt, compoundTypeDefinition);
			}
			hw.Create1DCompoundDataset(dataset, dt.Rows.Count, compoundTypeDefinition);
			using H5Assist.VB.CompoundWriter compoundWriter = new H5Assist.VB.CompoundWriter(hw, dataset, dt.Rows.Count);
			int count2 = _writeExecutions.Count - 1;
			for (int j = 0; j <= count2; j++)
			{
				_writeExecutions[j](compoundWriter, dt, compoundTypeDefinition.Fields[j].Type);
			}
		}
	}

	private bool TryFillField(DataTable dt, Dictionary<CompoundFieldDefinition, Array> columns, string dtColumnName, string diskColumnName, bool outputMessageIfNotFound)
	{
		int indexOf = dt.Columns.IndexOf(dtColumnName);
		if (indexOf == -1)
		{
			return false;
		}
		Type dataType = dt.Columns[indexOf].DataType;
		checked
		{
			foreach (KeyValuePair<CompoundFieldDefinition, Array> column in columns)
			{
				if (Operators.CompareString(column.Key.Name, diskColumnName, TextCompare: false) != 0)
				{
					continue;
				}
				Array columnTerm = column.Value;
				int rowsDtTerm = Math.Min(columnTerm.Length, dt.Rows.Count);
				if (column.Key.Type == typeof(float))
				{
					int count = rowsDtTerm - 1;
					for (int i = 0; i <= count; i++)
					{
						float rows = Conversions.ToSingle(columnTerm.GetValue(i));
						if (float.IsNaN(rows))
						{
							dt.Rows[i][indexOf] = DBNull.Value;
						}
						else
						{
							dt.Rows[i][indexOf] = rows;
						}
					}
				}
				else if (column.Key.Type == typeof(double))
				{
					int count2 = rowsDtTerm - 1;
					for (int j = 0; j <= count2; j++)
					{
						double rows2 = Conversions.ToDouble(columnTerm.GetValue(j));
						if (double.IsNaN(rows2))
						{
							dt.Rows[j][indexOf] = DBNull.Value;
						}
						else
						{
							dt.Rows[j][indexOf] = rows2;
						}
					}
				}
				else if (_serializers.ContainsKey(dataType))
				{
					Func<string, object> func = _deserializers[dataType];
					int count3 = rowsDtTerm - 1;
					for (int k = 0; k <= count3; k++)
					{
						string conversions = Conversions.ToString(columnTerm.GetValue(k));
						dt.Rows[k][indexOf] = RuntimeHelpers.GetObjectValue(func(conversions));
					}
				}
				else
				{
					int count4 = rowsDtTerm - 1;
					for (int l = 0; l <= count4; l++)
					{
						dt.Rows[l][indexOf] = RuntimeHelpers.GetObjectValue(columnTerm.GetValue(l));
					}
				}
				return true;
			}
			_ = outputMessageIfNotFound & !SharedData.IsTestingUtility;
			return false;
		}
	}

	private bool TryWriteField<T>(DataTable dt, H5Assist.VB.CompoundWriter hcw, string dtColumnName, string diskColumnName, T fallBackValue = default(T)) where T : struct
	{
		int indexOf = dt.Columns.IndexOf(dtColumnName);
		if (indexOf == -1)
		{
			return false;
		}
		int columnIndex = hcw.DataType.ColumnIndex(diskColumnName);
		if (columnIndex == -1)
		{
			return false;
		}
		checked
		{
			T[] values = new T[dt.Rows.Count - 1 + 1];
			int count = dt.Rows.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				object objectValue = RuntimeHelpers.GetObjectValue(dt.Rows[i][indexOf]);
				if (objectValue == null || Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
				{
					values[i] = fallBackValue;
				}
				else
				{
					values[i] = Conversions.ToGenericParameter<T>(dt.Rows[i][indexOf]);
				}
			}
			hcw.WriteColumn(columnIndex, values);
			return true;
		}
	}

	private bool TryWriteStringField(DataTable dt, H5Assist.VB.CompoundWriter hcw, string dtColumnName, string diskColumnName)
	{
		int indexOf = dt.Columns.IndexOf(dtColumnName);
		if (indexOf == -1)
		{
			return false;
		}
		int columnIndex = hcw.DataType.ColumnIndex(diskColumnName);
		if (columnIndex == -1)
		{
			return false;
		}
		Type dataType = dt.Columns[indexOf].DataType;
		Func<object, string> func = null;
		if (_serializers.ContainsKey(dataType))
		{
			func = _serializers[dataType];
		}
		checked
		{
			string[] strings = new string[dt.Rows.Count - 1 + 1];
			int count = dt.Rows.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				object objectValue = RuntimeHelpers.GetObjectValue(dt.Rows[i][indexOf]);
				if (func != null)
				{
					if (dataType.IsEnum)
					{
						objectValue = ((!Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue))) ? RuntimeHelpers.GetObjectValue(Enum.ToObject(dataType, RuntimeHelpers.GetObjectValue(objectValue))) : null);
					}
					strings[i] = func(RuntimeHelpers.GetObjectValue(objectValue));
				}
				else
				{
					strings[i] = objectValue as string;
				}
			}
			hcw.WriteColumn(columnIndex, strings);
			return true;
		}
	}

	private int MaxStringLength(DataTable dt, string colName)
	{
		int indexOf = dt.Columns.IndexOf(colName);
		if (indexOf == -1)
		{
			return 0;
		}
		Type dataType = dt.Columns[indexOf].DataType;
		Func<object, string> func = null;
		if (_serializers.ContainsKey(dataType))
		{
			if (_maxStringLengths.ContainsKey(dataType))
			{
				return _maxStringLengths[dataType];
			}
			func = _serializers[dataType];
		}
		int ordinal = 0;
		checked
		{
			int count = dt.Rows.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				object objectValue = RuntimeHelpers.GetObjectValue(dt.Rows[i][indexOf]);
				if (objectValue != null && !Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
				{
					string funcGetObjectValueTerm = "";
					funcGetObjectValueTerm = ((func != null) ? func(RuntimeHelpers.GetObjectValue(objectValue)) : Conversions.ToString(objectValue));
					ordinal = Math.Max(ordinal, funcGetObjectValueTerm.Length);
				}
			}
			return ordinal;
		}
	}

	public HDF5MemoryDiskMap Merge(HDF5MemoryDiskMap other)
	{
		HDF5MemoryDiskMap hDF5MemoryDiskMap = new HDF5MemoryDiskMap();
		checked
		{
			int count = _writeColumnNames.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (!hDF5MemoryDiskMap._writeColumnNames.Contains(_writeColumnNames[i]))
				{
					hDF5MemoryDiskMap._writeColumnNames.Add(_writeColumnNames[i]);
					hDF5MemoryDiskMap._preWriteExecutions.Add(_preWriteExecutions[i]);
					hDF5MemoryDiskMap._writeExecutions.Add(_writeExecutions[i]);
				}
			}
			int count2 = other._writeColumnNames.Count - 1;
			for (int j = 0; j <= count2; j++)
			{
				if (!hDF5MemoryDiskMap._writeColumnNames.Contains(other._writeColumnNames[j]))
				{
					hDF5MemoryDiskMap._writeColumnNames.Add(other._writeColumnNames[j]);
					hDF5MemoryDiskMap._preWriteExecutions.Add(other._preWriteExecutions[j]);
					hDF5MemoryDiskMap._writeExecutions.Add(other._writeExecutions[j]);
				}
			}
			return hDF5MemoryDiskMap;
		}
	}
}
