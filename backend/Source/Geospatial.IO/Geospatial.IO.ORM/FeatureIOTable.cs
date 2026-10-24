using System.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using H5Assist;
using H5Assist.Identifiers;

namespace Geospatial.IO.ORM;

public class FeatureIOTable
{
	private readonly List<string> _colNames = new List<string>();

	private readonly List<Type> _colTypes = new List<Type>();

	private readonly List<object[]> _rows = new List<object[]>();

	public IReadOnlyList<string> ColumnNames => _colNames;

	public IReadOnlyList<Type> ColumnTypes => _colTypes;

	public int ColumnCount => _colNames.Count;

	public int RowCount => _rows.Count;

	public int AddColumn(string colName, Type colType)
	{
		_colNames.Add(colName);
		_colTypes.Add(colType);
		int rowCount = RowCount;
		if (rowCount != 0)
		{
			int columnCount = ColumnCount;
			object instance = (colType.IsValueType ? Activator.CreateInstance(colType) : null);
			for (int i = 0; i < rowCount; i++)
			{
				object[] row = _rows[i];
				object[] rows = new object[columnCount];
				for (int j = 0; j < columnCount - 1; j++)
				{
					rows[j] = row[j];
				}
				rows[columnCount - 1] = instance;
				_rows[i] = rows;
			}
		}
		return _colNames.Count - 1;
	}

	public void AddColumn<T>(string colName)
	{
		AddColumn(colName, typeof(T));
	}

	public object[] AddRow()
	{
		object[] objectValues = new object[ColumnCount];
		_rows.Add(objectValues);
		return objectValues;
	}

	public void AddRow(object[] rowData)
	{
		if (rowData == null)
		{
			throw new ArgumentNullException("rowData");
		}
		if (rowData.Length != ColumnCount)
		{
			throw new ArgumentException("rowData column count incorrect.");
		}
		_rows.Add(rowData.ToArray());
	}

	public object[] GetRow(int idx)
	{
		return _rows[idx];
	}

	public void WriteCompoundDataset(H5Writer h5w, string dset)
	{
		if (ColumnCount == 0)
		{
			h5w.Delete(dset);
			return;
		}
		CompoundTypeDefinition compoundTypeDefinition = new CompoundTypeDefinition();
		for (int i = 0; i < ColumnCount; i++)
		{
			string name = _colNames[i];
			Type type = _colTypes[i];
			if (type.IsPrimitive)
			{
				compoundTypeDefinition.AddField(type, name);
				continue;
			}
			if (type == typeof(string))
			{
				int rowTerm = 0;
				for (int j = 0; j < _rows.Count; j++)
				{
					string row = _rows[j][i] as string;
					if (!string.IsNullOrEmpty(row))
					{
						rowTerm = Math.Max(rowTerm, Encoding.UTF8.GetByteCount(row));
					}
				}
				compoundTypeDefinition.AddStringField(name, rowTerm);
				continue;
			}
			throw new NotSupportedException("Column type must be a primitive or string. Actual: " + type.FullName.ToString());
		}
		h5w.Create1DCompoundDataset(dset, 100, compoundTypeDefinition);
		for (int k = 0; k < _rows.Count; k++)
		{
			h5w.AddCompoundElement(dset, _rows[k]);
		}
	}
}
