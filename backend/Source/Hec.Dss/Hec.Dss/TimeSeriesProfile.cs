using System;
using System.Data;

namespace Hec.Dss;

public class TimeSeriesProfile
{
	public DateTime StartDateTime
	{
		get
		{
			if (Times == null || Times.Length == 0)
			{
				throw new NullReferenceException("Error StartDateTime is not defined");
			}
			return Times[0];
		}
		set
		{
			if (Times == null)
			{
				Times = new DateTime[1] { value };
			}
			else if (Times.Length != 0)
			{
				Times[0] = value;
			}
		}
	}

	public double[,] Values { get; set; }

	public DateTime[] Times { get; set; }

	public string Units { get; set; }

	public string ColumnUnits { get; set; }

	public DssPath Path { get; set; }

	public string DataType { get; set; }

	public int Count => Values.GetLength(0);

	public LocationInformation LocationInformation { get; set; }

	public double[] ColumnValues { get; set; }

	public TimeSeriesPoint this[int i, int j] => new TimeSeriesPoint(Times[i], Values[i, j]);

	public TimeSeriesProfile()
	{
		Units = "";
		DataType = "";
		ColumnUnits = "";
	}

	public int IndexOf(DateTime t)
	{
		for (int i = 0; i < Times.Length; i++)
		{
			if (Times[i].CompareTo(t) == 0)
			{
				return i;
			}
		}
		return -1;
	}

	public void WriteToConsole()
	{
		for (int i = 0; i < Times.Length; i++)
		{
			for (int j = 0; j < Values.GetLength(0); j++)
			{
				Console.WriteLine(Times[i].ToString() + " " + Values[i, j]);
			}
		}
	}

	public DataTable ToDataTable()
	{
		DataTable dataTable = new DataTable();
		dataTable.Columns.Add("DateTime", typeof(DateTime));
		for (int i = 0; i < ColumnValues.Length; i++)
		{
			dataTable.Columns.Add(ColumnValues[i].ToString("F2"), typeof(double));
		}
		for (int j = 0; j < Count; j++)
		{
			DataRow dataRow = dataTable.NewRow();
			dataTable.Rows.Add(dataRow);
			dataRow[0] = Times[j];
			for (int k = 0; k < ColumnValues.Length; k++)
			{
				dataRow[k + 1] = Values[j, k];
			}
		}
		return dataTable;
	}
}
