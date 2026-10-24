using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Geospatial.Vectors;
using OSGeo.OGR;
using Utility.Progress;

namespace Geospatial.GDALAssist.Vectors;

public sealed class VectorDataset : IDisposable
{
	[DebuggerDisplay("{ToString()}")]
	public class DataSetStats
	{
		public DataSet DataSet { get; }

		public string DataSetName => DataSet.DataSetName;

		public List<DataTableStats> TableStats { get; }

		public DataSetStats(DataSet ds, List<DataTableStats> stats)
		{
			DataSet = ds;
			TableStats = stats;
		}

		public static DataSetStats ComputeFrom(DataSet ds)
		{
			List<DataTableStats> dataTableStats = new List<DataTableStats>();
			for (int i = 0; i < ds.Tables.Count; i++)
			{
				dataTableStats.Add(DataTableStats.ComputeFrom(ds.Tables[i]));
			}
			return new DataSetStats(ds, dataTableStats);
		}

		public DataTableStats GetTable(string name)
		{
			return TableStats.FirstOrDefault((DataTableStats ts) => ts.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));
		}

		public override string ToString()
		{
			return DataSetName + ": " + DataSet.Tables.Count + " Tables";
		}
	}

	[DebuggerDisplay("{ToString()}")]
	public class DataTableStats
	{
		public DataTable Table { get; }

		public string Name => Table.TableName;

		public int ColumnCount => Table.Columns.Count;

		public int RowCount => Table.Rows.Count;

		public List<DataColumnStats> ColumnStats { get; }

		public DataTableStats(DataTable table, List<DataColumnStats> colStats)
		{
			Table = table;
			ColumnStats = colStats;
		}

		public static DataTableStats ComputeFrom(DataTable table)
		{
			List<DataColumnStats> dataColumnStats = new List<DataColumnStats>();
			for (int i = 0; i < table.Columns.Count; i++)
			{
				dataColumnStats.Add(DataColumnStats.Compute(table, table.Columns[i].ColumnName));
			}
			return new DataTableStats(table, dataColumnStats);
		}

		public DataColumnStats GetColumn(string name)
		{
			return ColumnStats.FirstOrDefault((DataColumnStats ts) => ts.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));
		}

		public override string ToString()
		{
			return Name + ": " + RowCount + " Rows & " + ColumnCount + " Columns";
		}
	}

	[DebuggerDisplay("{ToString()}")]
	public class DataColumnStats
	{
		public string Name { get; }

		public Type Type { get; }

		public int DBNullCount { get; }

		public Dictionary<object, int> ValueCounter { get; }

		public DataColumnStats(string name, Type type, int dbNullCount, Dictionary<object, int> valueCounter)
		{
			Name = name;
			Type = type;
			DBNullCount = dbNullCount;
			ValueCounter = valueCounter;
		}

		public static DataColumnStats Compute(DataTable table, string colName)
		{
			DataColumn dataColumn = table.Columns[colName];
			Dictionary<object, int> intByObject = new Dictionary<object, int>();
			int ordinal = 0;
			for (int i = 0; i < table.Rows.Count; i++)
			{
				object row = table.Rows[i][dataColumn];
				int ordinal2;
				if (Convert.IsDBNull(row))
				{
					ordinal++;
				}
				else if (intByObject.TryGetValue(row, out ordinal2))
				{
					intByObject[row] = ordinal2 + 1;
				}
				else
				{
					intByObject[row] = 1;
				}
			}
			return new DataColumnStats(colName, dataColumn.DataType, ordinal, intByObject);
		}

		public override string ToString()
		{
			return Name + ": " + ValueCounter.Count + " Unique Values";
		}
	}

	private readonly DataSource _srcDs;

	private readonly Driver _srcDrv;

	public string Filename { get; }

	public VectorDataset(string filename)
	{
		if (!File.Exists(filename) && !Directory.Exists(filename))
		{
			throw new FileNotFoundException(filename);
		}
		Filename = filename;
		_srcDs = Ogr.OpenShared(filename, 0);
		if (_srcDs == null)
		{
			throw new Exception("OGR could not open file " + filename);
		}
		_srcDrv = _srcDs.GetDriver();
		if (_srcDrv == null)
		{
			throw new Exception("Driver could not be opened, OGR may not support this vector format.");
		}
	}

	public int LayerCount()
	{
		return _srcDs.GetLayerCount();
	}

	public VectorLayer GetLayer(int lyrIdx)
	{
		return new VectorLayer(_srcDs.GetLayerByIndex(lyrIdx));
	}

	public VectorLayer GetLayer(string layerName)
	{
		return new VectorLayer(_srcDs.GetLayerByName(layerName));
	}

	public List<string> GetLayerNames()
	{
		int layerCount = LayerCount();
		List<string> layerNames = new List<string>(layerCount);
		for (int i = 0; i < layerCount; i++)
		{
			using VectorLayer vectorLayer = GetLayer(i);
			layerNames.Add(vectorLayer?.GetName());
		}
		return layerNames;
	}

	public static void GetInfo(string filename, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		new DataSet();
		using VectorDataset vectorDataset = new VectorDataset(filename);
		prog.ReportMessage("File: " + filename);
		int layerCount = vectorDataset.LayerCount();
		prog.ReportMessage("Layers: " + layerCount);
		for (int i = 0; i < layerCount; i++)
		{
			prog.ReportMessage("");
			using VectorLayer vectorLayer = vectorDataset.GetLayer(i);
			prog.ReportMessage("  Layer Name: " + vectorLayer.GetName() + " (" + (i + 1) + "/" + layerCount + ")");
			prog.ReportMessage("    Geometry Type: " + vectorLayer.GetGeometryType());
			prog.ReportMessage("    " + (int)vectorLayer.GetFeatureCount() + " Features");
			prog.ReportMessage("    Fields:");
			foreach (KeyValuePair<string, Type> allField in vectorLayer.GetAllFields())
			{
				prog.ReportMessage("      " + allField.Key.ToString() + ": " + allField.Value);
			}
		}
	}

	public VectorLayer ExecuteSQL(string sql, Extent? extent = null)
	{
		Layer layer = null;
		if (extent.HasValue)
		{
			Extent extent2 = extent.Value;
			using Geometry geometry = new Geometry(wkbGeometryType.wkbLinearRing);
			geometry.AddPoint_2D(extent2.UpperLeft.X, extent2.UpperLeft.Y);
			geometry.AddPoint_2D(extent2.LowerLeft.X, extent2.LowerLeft.Y);
			geometry.AddPoint_2D(extent2.LowerRight.X, extent2.LowerRight.Y);
			geometry.AddPoint_2D(extent2.UpperRight.X, extent2.UpperRight.Y);
			geometry.AddPoint_2D(extent2.UpperLeft.X, extent2.UpperLeft.Y);
			layer = _srcDs.ExecuteSQL(sql, geometry, null);
		}
		else
		{
			layer = _srcDs.ExecuteSQL(sql, null, null);
		}
		if (layer == null)
		{
			return null;
		}
		return new VectorLayer(layer, _srcDs, isSqlQuery: true);
	}

	public DataSetStats ReadComputeStats(IList<string> tableNames = null, ProgressReporter prog = null)
	{
		return DataSetStats.ComputeFrom(GetSummary(instantiateGeometry: false, tableNames, prog));
	}

	public DataSet GetSummary(bool instantiateGeometry, IList<string> tableNames = null, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		prog.ReportMessage("File: " + Filename);
		DataSet dataSet = new DataSet(Path.GetFileNameWithoutExtension(Filename));
		int layerCount = LayerCount();
		prog.ReportMessage("Total Layers: " + layerCount);
		for (int i = 0; i < layerCount; i++)
		{
			using VectorLayer vectorLayer = GetLayer(i);
			string name = vectorLayer.GetName();
			if (tableNames == null || tableNames.Contains(name, StringComparer.CurrentCultureIgnoreCase))
			{
				prog.ReportMessage("  Reading '" + name + "' (" + (i + 1) + "/" + layerCount + ")");
				dataSet.Tables.Add(vectorLayer.GetTable(instantiateGeometry, prog));
			}
			else
			{
				prog.ReportMessage("  Skipping '" + name + "' (" + (i + 1) + "/" + layerCount + ")");
			}
		}
		return dataSet;
	}

	public void Dispose()
	{
		_srcDs?.Dispose();
		_srcDrv?.Dispose();
	}
}
