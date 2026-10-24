using System;
using System.Collections.Generic;
using H5Assist;

namespace Ras.Output;

public class SummaryDataset
{
	private readonly List<string> _rowNames;

	private readonly List<double[]> _rowData;

	public string Name { get; }

	public int ElementCount { get; }

	public int RowCount => _rowNames.Count;

	public IReadOnlyList<string> RowNames => _rowNames;

	public SummaryDataset(string name, int elementCount)
	{
		Name = name;
		ElementCount = elementCount;
		_rowNames = new List<string>();
		_rowData = new List<double[]>();
	}

	private SummaryDataset(string name, int elementCount, List<string> rowNames, List<double[]> rowData)
	{
		Name = name;
		ElementCount = elementCount;
		_rowNames = rowNames;
		_rowData = rowData;
	}

	public void AddRow(string rowName, double[] values)
	{
		if (values.Length != ElementCount)
		{
			throw new ArgumentException($"Expected {ElementCount} values but got {values.Length}", "values");
		}
		_rowNames.Add(rowName);
		_rowData.Add(values);
	}

	public bool TryGetRow(string rowName, out double[] values)
	{
		int indexOf = _rowNames.IndexOf(rowName);
		if (indexOf < 0)
		{
			values = null;
			return false;
		}
		values = _rowData[indexOf];
		return true;
	}

	public void WriteTo(H5Writer hw, string parentGroup)
	{
		if (RowCount == 0)
		{
			return;
		}
		double[,] doubleValues = new double[RowCount, ElementCount];
		for (int i = 0; i < RowCount; i++)
		{
			double[] rowData = _rowData[i];
			for (int j = 0; j < ElementCount; j++)
			{
				doubleValues[i, j] = rowData[j];
			}
		}
		string parentGroupTerm = parentGroup + Name;
		hw.WriteDataset(parentGroupTerm, doubleValues);
		hw.WriteAttribute(parentGroupTerm, "Row Value", _rowNames.ToArray());
	}

	public static SummaryDataset TryReadFrom(H5Reader hr, string datasetPath, string name)
	{
		if (!hr.ExistsAsDataset(datasetPath))
		{
			return null;
		}
		hr.ReadDataset(datasetPath, out double[,] values);
		int length = values.GetLength(0);
		int length2 = values.GetLength(1);
		string[] values2;
		if (hr.ExistsAsAttribute(datasetPath, "Row Value"))
		{
			hr.ReadAttribute(datasetPath, "Row Value", out values2);
		}
		else
		{
			values2 = new string[length];
		}
		List<string> rowNames = new List<string>(values2);
		List<double[]> doubleValuesList = new List<double[]>(length);
		for (int i = 0; i < length; i++)
		{
			double[] doubleValues = new double[length2];
			for (int j = 0; j < length2; j++)
			{
				doubleValues[j] = values[i, j];
			}
			doubleValuesList.Add(doubleValues);
		}
		return new SummaryDataset(name, length2, rowNames, doubleValuesList);
	}
}
