using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace Hec.Dss;

public class PairedData
{
	public DssPath Path { get; set; }

	public double[] Ordinates { get; set; }

	public List<double[]> Values { get; set; }

	public List<string> Labels { get; set; }

	public string TypeIndependent { get; set; }

	public string TypeDependent { get; set; }

	public string UnitsIndependent { get; set; }

	public string UnitsDependent { get; set; }

	public int CurveCount => Values.Count;

	public double[] FlattenedValues
	{
		get
		{
			double[] doubleValues = new double[Values.Sum((double[] arr) => arr.Length)];
			int ordinal = 0;
			foreach (double[] elements in Values)
			{
				Array.Copy(elements, 0, doubleValues, ordinal, elements.Length);
				ordinal += elements.Length;
			}
			return doubleValues;
		}
	}

	public LocationInformation LocationInformation { get; set; }

	public PairedData()
	{
		Labels = new List<string>();
		Values = new List<double[]>();
	}

	public PairedData(double[] xvalues, double[] yvalues, List<string> labels = null, string xunits = "", string xtype = "", string yunits = "", string ytype = "", string path = null)
	{
		Values = new List<double[]>();
		Path = new DssPath(path);
		Ordinates = xvalues;
		Labels = labels;
		Values.Add(yvalues);
		UnitsIndependent = xunits;
		UnitsDependent = yunits;
		TypeIndependent = xtype;
		TypeDependent = ytype;
	}

	public PairedData(double[] xvalues, List<double[]> yvalues, List<string> labels = null, string xunits = "", string xtype = "", string yunits = "", string ytype = "", string path = null)
	{
		Path = new DssPath(path);
		Ordinates = xvalues;
		Labels = labels;
		Values = yvalues;
		UnitsIndependent = xunits;
		UnitsDependent = yunits;
		TypeIndependent = xtype;
		TypeDependent = ytype;
	}

	public DataTable ToDataTable(bool includeIndex = false)
	{
		DataTable dataTable = new DataTable();
		if (includeIndex)
		{
			dataTable.Columns.Add("index");
		}
		dataTable.Columns.Add("stage", typeof(double));
		if (Labels != null && Labels.Count == Values.Count)
		{
			for (int i = 0; i < Labels.Count; i++)
			{
				dataTable.Columns.Add(Labels[i], typeof(double));
			}
		}
		else
		{
			for (int j = 0; j < Values.Count; j++)
			{
				dataTable.Columns.Add("value" + (j + 1), typeof(double));
			}
		}
		for (int k = 0; k < CurveCount; k++)
		{
			object[] objectValues = new object[0];
			if (includeIndex)
			{
				Array.Resize(ref objectValues, Values.Count + 2);
				objectValues[0] = k + 1;
				objectValues[1] = Ordinates[k];
				int ordinal = 0;
				int ordinal2 = 2;
				while (ordinal < Values.Count)
				{
					objectValues[ordinal2] = Values[ordinal][k];
					ordinal++;
					ordinal2++;
				}
				dataTable.Rows.Add(objectValues);
			}
			else
			{
				Array.Resize(ref objectValues, Values.Count + 1);
				objectValues[0] = Ordinates[k];
				int ordinal3 = 0;
				int unity = 1;
				while (ordinal3 < Values.Count)
				{
					objectValues[unity] = Values[ordinal3][k];
					ordinal3++;
					unity++;
				}
				dataTable.Rows.Add(objectValues);
			}
		}
		return dataTable;
	}

	public static PairedData Create(double[] xvalues, double[] yvalues, string xunits = "", string xtype = "", string yunits = "", string ytype = "", string path = null)
	{
		return new PairedData
		{
			Path = new DssPath(path),
			Ordinates = xvalues,
			Values = { yvalues },
			UnitsIndependent = xunits,
			UnitsDependent = yunits,
			TypeIndependent = xtype,
			TypeDependent = ytype
		};
	}

	public static PairedData Create(double[] xvalues, List<double[]> yvalues, string xunits = "", string xtype = "", string yunits = "", string ytype = "", string path = null)
	{
		return new PairedData
		{
			Path = new DssPath(path),
			Ordinates = xvalues,
			Values = yvalues,
			UnitsIndependent = xunits,
			UnitsDependent = yunits,
			TypeIndependent = xtype,
			TypeDependent = ytype
		};
	}
}
