using System;
using System.Collections.Generic;
using Utility.Memory;

namespace Ras.Process.H5Commands.Explorer;

public class ColumnStatistics
{
	public int ColumnIndex { get; set; }

	public string ColumnName { get; set; }

	public Type ColumnType { get; set; }

	public double? Min { get; set; }

	public double? Max { get; set; }

	public double? Mean { get; set; }

	public double? StdDev { get; set; }

	public int TotalCount { get; set; }

	public int ValidCount { get; set; }

	public int NullCount { get; set; }

	public bool IsNumeric { get; set; }

	public string ErrorMessage { get; set; }

	public static ColumnStatistics Calculate(ExplorerState state, int columnIndex)
	{
		if (state.CurrentTable != null)
		{
			return CalculateFromTable(state.CurrentTable, columnIndex, state.DeletedRows);
		}
		if (state.CurrentData != null)
		{
			if (state.CurrentData.Rank == 1)
			{
				return CalculateFrom1DArray(state.CurrentData, state.DeletedRows);
			}
			if (state.CurrentData.Rank == 2)
			{
				return CalculateFrom2DArray(state.CurrentData, columnIndex, state.DeletedRows);
			}
		}
		return new ColumnStatistics
		{
			IsNumeric = false,
			ErrorMessage = "No data available"
		};
	}

	private static ColumnStatistics CalculateFromTable(Table table, int columnIndex, HashSet<int> deletedRows)
	{
		if (columnIndex < 0 || columnIndex >= table.Columns.Count)
		{
			return new ColumnStatistics
			{
				IsNumeric = false,
				ErrorMessage = "Invalid column index"
			};
		}
		TableColumn tableColumn = table.Columns[columnIndex];
		ColumnStatistics columnStatistics = new ColumnStatistics
		{
			ColumnIndex = columnIndex,
			ColumnName = tableColumn.Name,
			ColumnType = tableColumn.Type
		};
		if (!IsNumericType(tableColumn.Type))
		{
			columnStatistics.IsNumeric = false;
			columnStatistics.ErrorMessage = "Column type '" + tableColumn.Type.Name + "' is not numeric - statistics not available";
			return columnStatistics;
		}
		columnStatistics.IsNumeric = true;
		List<double> doubleList = new List<double>();
		int totalCount = 0;
		int nullCount = 0;
		for (int i = 0; i < table.Rows.Count; i++)
		{
			if (!deletedRows.Contains(i))
			{
				totalCount++;
				object row = table.Rows[i][columnIndex];
				if (row == null)
				{
					nullCount++;
				}
				else
				{
					doubleList.Add(ConvertToDouble(row));
				}
			}
		}
		columnStatistics.TotalCount = totalCount;
		columnStatistics.NullCount = nullCount;
		columnStatistics.ValidCount = doubleList.Count;
		if (doubleList.Count > 0)
		{
			CalculateStats(doubleList, columnStatistics);
		}
		return columnStatistics;
	}

	private static ColumnStatistics CalculateFrom1DArray(Array data, HashSet<int> deletedRows)
	{
		ColumnStatistics columnStatistics = new ColumnStatistics
		{
			ColumnIndex = 0,
			ColumnName = "Value",
			ColumnType = data.GetType().GetElementType()
		};
		if (!IsNumericType(columnStatistics.ColumnType))
		{
			columnStatistics.IsNumeric = false;
			columnStatistics.ErrorMessage = "Array type '" + columnStatistics.ColumnType.Name + "' is not numeric - statistics not available";
			return columnStatistics;
		}
		columnStatistics.IsNumeric = true;
		List<double> doubleList = new List<double>();
		int totalCount = 0;
		int nullCount = 0;
		for (int i = 0; i < data.Length; i++)
		{
			if (!deletedRows.Contains(i))
			{
				totalCount++;
				object getValue = data.GetValue(i);
				if (getValue == null)
				{
					nullCount++;
				}
				else
				{
					doubleList.Add(ConvertToDouble(getValue));
				}
			}
		}
		columnStatistics.TotalCount = totalCount;
		columnStatistics.NullCount = nullCount;
		columnStatistics.ValidCount = doubleList.Count;
		if (doubleList.Count > 0)
		{
			CalculateStats(doubleList, columnStatistics);
		}
		return columnStatistics;
	}

	private static ColumnStatistics CalculateFrom2DArray(Array data, int columnIndex, HashSet<int> deletedRows)
	{
		int length = data.GetLength(1);
		if (columnIndex < 0 || columnIndex >= length)
		{
			return new ColumnStatistics
			{
				IsNumeric = false,
				ErrorMessage = "Invalid column index"
			};
		}
		ColumnStatistics columnStatistics = new ColumnStatistics
		{
			ColumnIndex = columnIndex,
			ColumnName = columnIndex.ToString(),
			ColumnType = data.GetType().GetElementType()
		};
		if (!IsNumericType(columnStatistics.ColumnType))
		{
			columnStatistics.IsNumeric = false;
			columnStatistics.ErrorMessage = "Array type '" + columnStatistics.ColumnType.Name + "' is not numeric - statistics not available";
			return columnStatistics;
		}
		columnStatistics.IsNumeric = true;
		List<double> doubleList = new List<double>();
		int totalCount = 0;
		int nullCount = 0;
		int length2 = data.GetLength(0);
		for (int i = 0; i < length2; i++)
		{
			if (!deletedRows.Contains(i))
			{
				totalCount++;
				object getValue = data.GetValue(i, columnIndex);
				if (getValue == null)
				{
					nullCount++;
				}
				else
				{
					doubleList.Add(ConvertToDouble(getValue));
				}
			}
		}
		columnStatistics.TotalCount = totalCount;
		columnStatistics.NullCount = nullCount;
		columnStatistics.ValidCount = doubleList.Count;
		if (doubleList.Count > 0)
		{
			CalculateStats(doubleList, columnStatistics);
		}
		return columnStatistics;
	}

	private static void CalculateStats(List<double> values, ColumnStatistics stats)
	{
		if (values.Count == 0)
		{
			return;
		}
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		double computed = 0.0;
		foreach (double computed2 in values)
		{
			if (computed2 < maxValue)
			{
				maxValue = computed2;
			}
			if (computed2 > minValue)
			{
				minValue = computed2;
			}
			computed += computed2;
		}
		stats.Min = maxValue;
		stats.Max = minValue;
		stats.Mean = computed / (double)values.Count;
		if (values.Count > 1)
		{
			double computed3 = 0.0;
			double meanStatsTerm = stats.Mean.Value;
			foreach (double computed4 in values)
			{
				double computed5 = computed4 - meanStatsTerm;
				computed3 += computed5 * computed5;
			}
			computed3 /= (double)values.Count;
			stats.StdDev = Math.Sqrt(computed3);
		}
		else
		{
			stats.StdDev = 0.0;
		}
	}

	private static bool IsNumericType(Type type)
	{
		if (!(type == typeof(float)) && !(type == typeof(double)) && !(type == typeof(int)) && !(type == typeof(uint)) && !(type == typeof(long)) && !(type == typeof(ulong)) && !(type == typeof(short)) && !(type == typeof(ushort)) && !(type == typeof(byte)))
		{
			return type == typeof(sbyte);
		}
		return true;
	}

	private static double ConvertToDouble(object localValue)
	{
		return Convert.ToDouble(localValue);
	}
}
