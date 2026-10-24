using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using H5Assist.VB;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.TinHelpers;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class HDF5Storage
{
	public static void ReadStringColumn(FeatureLayer ftl, H5Reader hr, string datasetName, string colName, SingleString ss = null)
	{
		ReadStringColumn(ftl.FeatureTable(), hr, datasetName, colName, ss);
	}

	public static void ReadStringColumn(DataTable dt, H5Reader hr, string datasetName, string colName, SingleString ss = null)
	{
		checked
		{
			try
			{
				if (!hr.ExistsAsDataset(datasetName))
				{
					return;
				}
				int indexOf = dt.Columns.IndexOf(colName);
				if (indexOf < 0)
				{
					throw new Exception("Error reading feature columns from file: " + hr.Filename + " column: '" + colName + "' not found.");
				}
				if (dt.Columns[indexOf].DataType != typeof(string))
				{
					throw new Exception("Error reading feature columns from file: " + hr.Filename + " column: '" + colName + "' is not a string type.");
				}
				if (ss == null)
				{
					ss = new SingleString();
				}
				using HDFBufferedStringReader hDFBufferedStringReader = new HDFBufferedStringReader(hr, datasetName);
				string elem = "";
				int count = System.Math.Min(hDFBufferedStringReader.Rows, dt.Rows.Count) - 1;
				for (int i = 0; i <= count; i++)
				{
					hDFBufferedStringReader.ReadRow(ref elem);
					DataTableExtensions.Write(dt, i, indexOf, ss.GetFirstInstance(elem));
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				ProjectData.ClearProjectError();
			}
		}
	}

	public static void WriteStringColumn(FeatureLayer ftl, H5Writer hw, string datasetName, string colName)
	{
		WriteStringColumn(ftl.FeatureTable(), hw, datasetName, colName);
	}

	public static void WriteStringColumn(DataTable dt, H5Writer hw, string datasetName, string colName)
	{
		if (dt.Rows.Count <= 0)
		{
			hw.Delete(datasetName);
			return;
		}
		int indexOf = dt.Columns.IndexOf(colName);
		if (indexOf < 0)
		{
			throw new Exception("Error writing feature columns from file: " + hw.Filename + " - Column: '" + colName + "' not found.");
		}
		int maxLength = dt.Columns[indexOf].MaxLength;
		checked
		{
			if (maxLength == -1)
			{
				int count = dt.Rows.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					string dataRowItemToString = Form.DataRowItemToString(dt.Rows[i], indexOf);
					if (dataRowItemToString != null)
					{
						maxLength = System.Math.Max(maxLength, dataRowItemToString.Length);
					}
				}
			}
			if (maxLength > 0)
			{
				using (HDFBufferedStringWriter hDFBufferedStringWriter = new HDFBufferedStringWriter(hw, datasetName, dt.Rows.Count, maxLength, "Feature", colName))
				{
					int count2 = dt.Rows.Count - 1;
					for (int j = 0; j <= count2; j++)
					{
						hDFBufferedStringWriter.AddRow(Form.DataRowItemToString(dt.Rows[j], indexOf));
					}
					return;
				}
			}
			if (hw.ExistsAsDataset(datasetName))
			{
				hw.Delete(datasetName);
			}
		}
	}

	public static void ReadColumns(FeatureLayer ftl, H5Reader hr, string datasetName, params string[] colNames)
	{
		ReadColumns(ftl.FeatureTable(), hr, datasetName, colNames);
	}

	public static void ReadColumns(DataTable dt, H5Reader hr, string datasetName, params string[] colNames)
	{
		if (!hr.ExistsAsDataset(datasetName))
		{
			return;
		}
		int colNamesTerm = colNames.Count();
		checked
		{
			int[] intValues = new int[colNamesTerm - 1 + 1];
			int count = colNames.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				intValues[i] = dt.Columns.IndexOf(colNames[i]);
				if (intValues[i] < 0)
				{
					throw new Exception("Error reading feature columns from file: " + hr.Filename + " - Column: '" + colNames[i] + "' not found.");
				}
			}
			Type[] typeValues = new Type[colNamesTerm - 1 + 1];
			typeValues[0] = dt.Columns[intValues[0]].DataType;
			int count2 = colNames.Count() - 1;
			for (int j = 1; j <= count2; j++)
			{
				typeValues[j] = dt.Columns[intValues[j]].DataType;
				if (typeValues[j] != typeValues[j - 1])
				{
					throw new Exception("Error reading feature columns from file: " + hr.Filename + " - Column types not consistent.");
				}
			}
			if (typeValues[0] == typeof(int))
			{
				using (HDFBufferedReader<int> hDFBufferedReader = new HDFBufferedReader<int>(hr, datasetName))
				{
					if (hDFBufferedReader.Rows != dt.Rows.Count)
					{
						if (SharedData.DebugMode)
						{
							throw new Exception("Bad file - row count mismatch.");
						}
					}
					else
					{
						int[] intValuesArray = new int[colNamesTerm - 1 + 1];
						int count3 = hDFBufferedReader.Rows - 1;
						for (int k = 0; k <= count3; k++)
						{
							hDFBufferedReader.ReadRow(intValuesArray);
							int count4 = colNamesTerm - 1;
							for (int l = 0; l <= count4; l++)
							{
								dt.Rows[k][intValues[l]] = intValuesArray[l];
							}
						}
					}
					return;
				}
			}
			if (typeValues[0] == typeof(float))
			{
				using (HDFBufferedReader<float> hDFBufferedReader2 = new HDFBufferedReader<float>(hr, datasetName))
				{
					if (hDFBufferedReader2.Rows != dt.Rows.Count)
					{
						if (SharedData.DebugMode)
						{
							throw new Exception("Bad file - row count mismatch.");
						}
					}
					else
					{
						float[] floatValues = new float[colNamesTerm - 1 + 1];
						int count5 = hDFBufferedReader2.Rows - 1;
						for (int m = 0; m <= count5; m++)
						{
							hDFBufferedReader2.ReadRow(floatValues);
							int count6 = colNamesTerm - 1;
							for (int n = 0; n <= count6; n++)
							{
								if (!float.IsNaN(floatValues[n]) && floatValues[n] < float.MaxValue)
								{
									dt.Rows[m][intValues[n]] = floatValues[n];
								}
							}
						}
					}
					return;
				}
			}
			if (typeValues[0] == typeof(double))
			{
				using (HDFBufferedReader<double> hDFBufferedReader3 = new HDFBufferedReader<double>(hr, datasetName))
				{
					if (hDFBufferedReader3.Rows != dt.Rows.Count)
					{
						if (SharedData.DebugMode)
						{
							throw new Exception("Bad file - row count mismatch.");
						}
					}
					else
					{
						double[] doubleValues = new double[colNamesTerm - 1 + 1];
						int count7 = hDFBufferedReader3.Rows - 1;
						for (int intervalIndex = 0; intervalIndex <= count7; intervalIndex++)
						{
							hDFBufferedReader3.ReadRow(doubleValues);
							int count8 = colNamesTerm - 1;
							for (int intervalIndex2 = 0; intervalIndex2 <= count8; intervalIndex2++)
							{
								dt.Rows[intervalIndex][intValues[intervalIndex2]] = doubleValues[intervalIndex2];
							}
						}
					}
					return;
				}
			}
			throw new Exception();
		}
	}

	public static void WriteColumns(FeatureLayer ftl, H5Writer hw, string datasetName, params string[] colNames)
	{
		WriteColumns(ftl.FeatureTable(), hw, datasetName, colNames);
	}

	public static void WriteColumns(DataTable dt, H5Writer hw, string datasetName, params string[] colNames)
	{
		if (dt.Rows.Count <= 0)
		{
			hw.Delete(datasetName);
			return;
		}
		int colNamesTerm = colNames.Count();
		checked
		{
			int[] intValues = new int[colNamesTerm - 1 + 1];
			int count = colNamesTerm - 1;
			for (int i = 0; i <= count; i++)
			{
				intValues[i] = dt.Columns.IndexOf(colNames[i]);
				if (intValues[i] < 0)
				{
					throw new Exception("Error reading feature columns from file: " + hw.Filename + " - Column: '" + colNames[i] + "' not found.");
				}
			}
			Type[] typeValues = new Type[colNamesTerm - 1 + 1];
			typeValues[0] = dt.Columns[intValues[0]].DataType;
			int count2 = colNamesTerm - 1;
			for (int j = 1; j <= count2; j++)
			{
				typeValues[j] = dt.Columns[intValues[j]].DataType;
				if (typeValues[j] != typeValues[j - 1])
				{
					throw new Exception("Error reading feature columns from file: " + hw.Filename + " - Column types not consistent.");
				}
			}
			if (typeValues[0] == typeof(int))
			{
				using (HDFBufferedWriter<int> hDFBufferedWriter = new HDFBufferedWriter<int>(hw, datasetName, dt.Rows.Count, colNamesTerm, "Feature", colNames))
				{
					int[] intValuesArray = new int[colNamesTerm - 1 + 1];
					int count3 = dt.Rows.Count - 1;
					for (int k = 0; k <= count3; k++)
					{
						int count4 = colNamesTerm - 1;
						for (int l = 0; l <= count4; l++)
						{
							intValuesArray[l] = Form.DataRowItemToInteger(dt.Rows[k], intValues[l]);
						}
						hDFBufferedWriter.AddRow(intValuesArray);
					}
					return;
				}
			}
			if (typeValues[0] == typeof(float))
			{
				using (HDFBufferedWriter<float> hDFBufferedWriter2 = new HDFBufferedWriter<float>(hw, datasetName, dt.Rows.Count, colNamesTerm, "Feature", colNames))
				{
					float[] floatValues = new float[colNamesTerm - 1 + 1];
					int count5 = dt.Rows.Count - 1;
					for (int m = 0; m <= count5; m++)
					{
						int count6 = colNamesTerm - 1;
						for (int n = 0; n <= count6; n++)
						{
							floatValues[n] = Form.DataRowItemToSingle(dt.Rows[m], intValues[n]);
						}
						hDFBufferedWriter2.AddRow(floatValues);
					}
					return;
				}
			}
			if (typeValues[0] == typeof(double))
			{
				using (HDFBufferedWriter<double> hDFBufferedWriter3 = new HDFBufferedWriter<double>(hw, datasetName, dt.Rows.Count, colNamesTerm, "Feature", colNames))
				{
					double[] doubleValues = new double[colNamesTerm - 1 + 1];
					int count7 = dt.Rows.Count - 1;
					for (int intervalIndex = 0; intervalIndex <= count7; intervalIndex++)
					{
						int count8 = colNamesTerm - 1;
						for (int intervalIndex2 = 0; intervalIndex2 <= count8; intervalIndex2++)
						{
							doubleValues[intervalIndex2] = Form.DataRowItemToDouble(dt.Rows[intervalIndex], intValues[intervalIndex2]);
						}
						hDFBufferedWriter3.AddRow(doubleValues);
					}
					return;
				}
			}
			if (typeValues[0] == typeof(string))
			{
				throw new ArgumentException("Cannot call WriteColumnsShared with a string argument. Use function WriteStringColumn");
			}
			throw new NotImplementedException();
		}
	}

	public static bool BeginPointsBhr(H5Reader hr, string datasetname, ref bool hasZ, ref bool hasM, ref HDFBufferedReader<int> infoBhw, ref HDFBufferedReader<double> pointsBhw)
	{
		string datasetnameTerm = datasetname + " Info";
		string datasetnameTerm2 = datasetname + " Points";
		if (hr.ExistsAsDataset(datasetnameTerm) && hr.ExistsAsDataset(datasetnameTerm2))
		{
			infoBhw = new HDFBufferedReader<int>(hr, datasetnameTerm);
			pointsBhw = new HDFBufferedReader<double>(hr, datasetnameTerm2);
			PointBhwZM(pointsBhw, ref hasZ, ref hasM);
			return true;
		}
		return false;
	}

	public static bool BeginPolyBhr(H5Reader hr, string datasetname, ref bool hasZ, ref bool hasM, ref bool isPolygon, ref HDFBufferedReader<int> infoBhw, ref HDFBufferedReader<int> partBhw, ref HDFBufferedReader<double> pointsBhw)
	{
		string datasetnameTerm = datasetname + " Info";
		string datasetnameTerm2 = datasetname + " Parts";
		string datasetnameTerm3 = datasetname + " Points";
		if (hr.ExistsAsDataset(datasetnameTerm) && hr.ExistsAsDataset(datasetnameTerm3))
		{
			infoBhw = new HDFBufferedReader<int>(hr, datasetnameTerm);
			if (hr.ExistsAsDataset(datasetnameTerm2))
			{
				partBhw = new HDFBufferedReader<int>(hr, datasetnameTerm2);
			}
			pointsBhw = new HDFBufferedReader<double>(hr, datasetnameTerm3);
			PointBhwZM(pointsBhw, ref hasZ, ref hasM);
			string textValue = "";
			hr.ReadAttributeScalar(datasetnameTerm, "Feature Type", ref textValue);
			isPolygon = Operators.CompareString(textValue.ToLowerInvariant(), "polygon", TextCompare: false) == 0;
			return true;
		}
		return false;
	}

	public static bool BeginTINBhr(H5Reader hr, string datasetname, ref bool hasZ, ref bool hasM, ref HDFBufferedReader<int> infoBhw, ref HDFBufferedReader<int> triBhw, ref HDFBufferedReader<double> pointsBhw)
	{
		string datasetnameTerm = datasetname + " Info";
		string datasetnameTerm2 = datasetname + " Triangles";
		string datasetnameTerm3 = datasetname + " Points";
		if (hr.ExistsAsDataset(datasetnameTerm) && hr.ExistsAsDataset(datasetnameTerm2) && hr.ExistsAsDataset(datasetnameTerm3))
		{
			infoBhw = new HDFBufferedReader<int>(hr, datasetnameTerm);
			triBhw = new HDFBufferedReader<int>(hr, datasetnameTerm2);
			pointsBhw = new HDFBufferedReader<double>(hr, datasetnameTerm3);
			PointBhwZM(pointsBhw, ref hasZ, ref hasM);
			return true;
		}
		return false;
	}

	public static bool BeginProfilesBhr(H5Reader hr, string datasetPrefix, ref HDFBufferedReader<int> infoBhw, ref HDFBufferedReader<float> valuesBhw)
	{
		string datasetPrefixTerm = datasetPrefix + " Info";
		string datasetPrefixTerm2 = datasetPrefix + " Values";
		if (hr.ExistsAsDataset(datasetPrefixTerm) && hr.ExistsAsDataset(datasetPrefixTerm2))
		{
			infoBhw = new HDFBufferedReader<int>(hr, datasetPrefixTerm);
			valuesBhw = new HDFBufferedReader<float>(hr, datasetPrefixTerm2);
			return true;
		}
		return false;
	}

	public static void BeginPointBhw(H5Writer hw, string datasetname, int rows, bool hasZ, bool hasM, ref HDFBufferedWriter<double> pointsBhw)
	{
		pointsBhw = new HDFBufferedWriter<double>(hw, datasetname, rows, PointColumnCount(hasZ, hasM), "Points", PointColumnNames(hasZ, hasM));
		hw.WriteAttribute(datasetname, "Feature Type", "Point");
	}

	public static void BeginPointsBhw(H5Writer hw, string datasetname, int rows, bool hasZ, bool hasM, ref HDFBufferedWriter<int> infoBhw, ref HDFBufferedWriter<double> pointsBhw)
	{
		infoBhw = new HDFBufferedWriter<int>(hw, datasetname + " Info", rows, 2, "Feature", "Point Starting Index", "Point Count");
		pointsBhw = new HDFBufferedWriter<double>(hw, datasetname + " Points", 0, PointColumnCount(hasZ, hasM), "Points", PointColumnNames(hasZ, hasM));
		hw.WriteAttribute(datasetname + " Info", "Feature Type", "MultiPoint");
	}

	public static void DeletePointsDataset(H5Writer hw, string datasetName)
	{
		if (hw.ExistsAsDataset(datasetName + " Info"))
		{
			hw.Delete(datasetName + " Info");
		}
		if (hw.ExistsAsDataset(datasetName + " Points"))
		{
			hw.Delete(datasetName + " Points");
		}
	}

	public static void BeginPolyBhw(H5Writer hw, string datasetname, int rows, bool hasZ, bool hasM, bool isPolygon, ref HDFBufferedWriter<int> infoBhw, ref HDFBufferedWriter<int> partBhw, ref HDFBufferedWriter<double> pointsBhw)
	{
		infoBhw = new HDFBufferedWriter<int>(hw, datasetname + " Info", rows, 4, "Feature", "Point Starting Index", "Point Count", "Part Starting Index", "Part Count");
		partBhw = new HDFBufferedWriter<int>(hw, datasetname + " Parts", 0, 2, "Part", "Point Starting Index", "Point Count");
		pointsBhw = new HDFBufferedWriter<double>(hw, datasetname + " Points", 0, PointColumnCount(hasZ, hasM), "Points", PointColumnNames(hasZ, hasM));
		if (isPolygon)
		{
			hw.WriteAttribute(datasetname + " Info", "Feature Type", "Polygon");
		}
		else
		{
			hw.WriteAttribute(datasetname + " Info", "Feature Type", "Polyline");
		}
	}

	public static void DeletePolyDataset(H5Writer hw, string datasetName)
	{
		if (hw.ExistsAsDataset(datasetName + " Info"))
		{
			hw.Delete(datasetName + " Info");
		}
		if (hw.ExistsAsDataset(datasetName + " Parts"))
		{
			hw.Delete(datasetName + " Parts");
		}
		if (hw.ExistsAsDataset(datasetName + " Points"))
		{
			hw.Delete(datasetName + " Points");
		}
	}

	public static void BeginTINBhw(H5Writer hw, string datasetname, int rows, bool hasZ, bool hasM, ref HDFBufferedWriter<int> infoBhw, ref HDFBufferedWriter<int> triBhw, ref HDFBufferedWriter<double> pointsBhw)
	{
		infoBhw = new HDFBufferedWriter<int>(hw, datasetname + " Info", rows, 4, "Feature", "Point Starting Index", "Point Count", "Triangle Starting Index", "Triangle Count");
		triBhw = new HDFBufferedWriter<int>(hw, datasetname + " Triangles", 0, 3, "Row", "Index0", "Index1", "Index2");
		pointsBhw = new HDFBufferedWriter<double>(hw, datasetname + " Points", 0, PointColumnCount(hasZ, hasM), "Points", PointColumnNames(hasZ, hasM));
		hw.WriteAttribute(datasetname + " Info", "Feature Type", "TIN");
	}

	public static void DeleteTINDataset(H5Writer hw, string datasetName)
	{
		if (hw.ExistsAsDataset(datasetName + " Info"))
		{
			hw.Delete(datasetName + " Info");
		}
		if (hw.ExistsAsDataset(datasetName + " Triangles"))
		{
			hw.Delete(datasetName + " Triangles");
		}
		if (hw.ExistsAsDataset(datasetName + " Points"))
		{
			hw.Delete(datasetName + " Points");
		}
	}

	public static void BeginProfile(H5Writer hw, string datasetname, int rows, ref HDFBufferedWriter<int> infoBhw, ref HDFBufferedWriter<float> valuesBhw, string xAttributeName = "X", string yAttributeName = "Y")
	{
		infoBhw = new HDFBufferedWriter<int>(hw, datasetname + " Info", rows, 2, "Feature", "Starting Index", "Count");
		valuesBhw = new HDFBufferedWriter<float>(hw, datasetname + " Values", 0, 2, "Values", xAttributeName, yAttributeName);
	}

	public static void DeleteMultipointDataset(H5Writer hw, string datasetName)
	{
		if (hw.ExistsAsDataset(datasetName + " Info"))
		{
			hw.Delete(datasetName + " Info");
		}
		if (hw.ExistsAsDataset(datasetName + " Values"))
		{
			hw.Delete(datasetName + " Values");
		}
	}

	public static void BeginBlock(H5Writer hw, string datasetname, int rows, ref HDFBufferedWriter<int> infoBhw, ref HDFBufferedWriter<float> valuesBhw)
	{
		infoBhw = new HDFBufferedWriter<int>(hw, datasetname + " Info", rows, 2, "Feature", "Starting Index", "Count");
		valuesBhw = new HDFBufferedWriter<float>(hw, datasetname + " Values", 0, 3, "Values", "Left", "Right", "Elevation");
	}

	private static void PointBhwZM(HDFBufferedReader<double> pointsBhr, ref bool hasZ, ref bool hasM)
	{
		if (pointsBhr.Cols == 4)
		{
			hasZ = true;
			hasM = true;
		}
		else if (pointsBhr.Cols == 3)
		{
			string[] values = null;
			pointsBhr.hr.ReadAttribute(pointsBhr.DatasetName, "Column", ref values);
			if (Operators.CompareString(values[2], "Z", TextCompare: false) == 0)
			{
				hasZ = true;
				hasM = false;
			}
			else
			{
				hasZ = false;
				hasM = true;
			}
		}
		else
		{
			hasZ = false;
			hasM = false;
		}
	}

	private static void PointFromBhw(bool hasZ, bool hasM, HDFBufferedReader<double> pointsBhr, ref PointM PointM)
	{
		if (hasZ & hasM)
		{
			pointsBhr.ReadRow(ref PointM.X, ref PointM.Y, ref PointM.Z, ref PointM.M);
		}
		else if (hasZ)
		{
			pointsBhr.ReadRow(ref PointM.X, ref PointM.Y, ref PointM.Z);
			PointM.M = double.NaN;
		}
		else if (hasM)
		{
			pointsBhr.ReadRow(ref PointM.X, ref PointM.Y, ref PointM.M);
			PointM.Z = double.NaN;
		}
		else
		{
			pointsBhr.ReadRow(ref PointM.X, ref PointM.Y);
			PointM.Z = double.NaN;
			PointM.M = double.NaN;
		}
	}

	private static int PointColumnCount(bool hasZ, bool hasM)
	{
		int ordinal = 2;
		checked
		{
			if (hasZ)
			{
				ordinal++;
			}
			if (hasM)
			{
				ordinal++;
			}
			return ordinal;
		}
	}

	private static string[] PointColumnNames(bool hasZ, bool hasM)
	{
		if (hasZ & hasM)
		{
			return new string[4] { "X", "Y", "Z", "M" };
		}
		if (hasZ)
		{
			return new string[3] { "X", "Y", "Z" };
		}
		if (hasM)
		{
			return new string[3] { "X", "Y", "M" };
		}
		return new string[2] { "X", "Y" };
	}

	private static void WritePointHelper(Extent extent, HDFBufferedWriter<double> pointsBhw, PointM PointM)
	{
		if (extent.HasZ & extent.HasM)
		{
			pointsBhw.AddRow(PointM.X, PointM.Y, PointM.Z, PointM.M);
		}
		else if (extent.HasZ)
		{
			pointsBhw.AddRow(PointM.X, PointM.Y, PointM.Z);
		}
		else if (extent.HasM)
		{
			pointsBhw.AddRow(PointM.X, PointM.Y, PointM.M);
		}
		else
		{
			pointsBhw.AddRow(PointM.X, PointM.Y);
		}
	}

	public static void ReadPointFeatures(PointFeatureLayer ftl, H5Reader hr, string datasetName)
	{
		if (!hr.ExistsAsDataset(datasetName))
		{
			return;
		}
		checked
		{
			using HDFBufferedReader<double> hDFBufferedReader = new HDFBufferedReader<double>(hr, datasetName);
			bool hasZ = default;
			bool hasM = default;
			PointBhwZM(hDFBufferedReader, ref hasZ, ref hasM);
			int count = hDFBufferedReader.Rows - 1;
			PointM PointM = default;
			for (int i = 0; i <= count; i++)
			{
				PointFromBhw(hasZ, hasM, hDFBufferedReader, ref PointM);
				ftl.AddFeature(new Point(PointM));
			}
		}
	}

	public static void WritePointFeatures(PointFeatureLayer ftl, H5Writer hw, string datasetName)
	{
		if (hw.ExistsAsDataset(datasetName))
		{
			hw.Delete(datasetName);
		}
		DataTable dataTable = ftl.FeatureTable();
		if (dataTable.Rows.Count <= 0)
		{
			return;
		}
		Extent extent = ftl.Extent;
		HDFBufferedWriter<double> pointsBhw = null;
		BeginPointBhw(hw, datasetName, dataTable.Rows.Count, extent.HasZ, extent.HasM, ref pointsBhw);
		foreach (Point featuresOfType in ftl.FeaturesOfType<Point>())
		{
			WritePointHelper(extent, pointsBhw, featuresOfType.PointM());
		}
		pointsBhw.Dispose();
	}

	public static void ReadEllipseFeatures(EllipseFeatureLayer ctl, H5Reader hr, string datasetName)
	{
		if (!hr.ExistsAsDataset(datasetName))
		{
			return;
		}
		Type datasetType = hr.GetDatasetType(datasetName);
		checked
		{
			if (datasetType == typeof(double))
			{
				double[,] values = null;
				hr.ReadDataset(datasetName, ref values);
				if (values.GetLength(1) != 5)
				{
					throw new Exception("Malformed ellipse hdf5 dataset " + datasetName);
				}
				int count = values.GetLength(0) - 1;
				for (int i = 0; i <= count; i++)
				{
					ctl.AddFeature(new Ellipse(values[i, 0], values[i, 1], values[i, 2], values[i, 3], values[i, 4]));
				}
				return;
			}
			if (datasetType == typeof(float))
			{
				float[,] values2 = null;
				hr.ReadDataset(datasetName, ref values2);
				if (values2.GetLength(1) != 5)
				{
					throw new Exception("Malformed ellipse hdf5 dataset " + datasetName);
				}
				int count2 = values2.GetLength(0) - 1;
				for (int j = 0; j <= count2; j++)
				{
					ctl.AddFeature(new Ellipse(values2[j, 0], values2[j, 1], values2[j, 2], values2[j, 3], values2[j, 4]));
				}
				return;
			}
			throw new NotImplementedException();
		}
	}

	public static void WriteEllipseFeatures(EllipseFeatureLayer ctl, H5Writer hw, string datasetName)
	{
		if (hw.ExistsAsDataset(datasetName))
		{
			hw.Delete(datasetName);
		}
		checked
		{
			if (ctl.FeatureTable().Rows.Count > 0)
			{
				double[,] doubleValues = new double[ctl.FeatureCount() - 1 + 1, 5];
				int count = ctl.FeatureCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					Ellipse ellipse = ctl.Ellipse(i);
					doubleValues[i, 0] = ellipse.Center.X;
					doubleValues[i, 1] = ellipse.Center.Y;
					doubleValues[i, 2] = ellipse.RadiusX;
					doubleValues[i, 3] = ellipse.RadiusY;
					doubleValues[i, 4] = ellipse.AngleRotation;
				}
				hw.WriteArray(datasetName, doubleValues);
				hw.WriteAttribute(datasetName, "Column Ordering", "X, Y, RadiusX, RadiusY, AngleRotation");
			}
		}
	}

	public static void ReadRectangleFeatures(RectangleFeatureLayer ctl, H5Reader hr, string datasetName)
	{
		if (!hr.ExistsAsDataset(datasetName))
		{
			return;
		}
		Type datasetType = hr.GetDatasetType(datasetName);
		checked
		{
			if (datasetType == typeof(double))
			{
				double[,] values = null;
				hr.ReadDataset(datasetName, ref values);
				if (values.GetLength(1) != 5)
				{
					throw new Exception("Malformed rectangular hdf5 dataset " + datasetName);
				}
				int count = values.GetLength(0) - 1;
				for (int i = 0; i <= count; i++)
				{
					double x = values[i, 0];
					double y = values[i, 1];
					double width = values[i, 2];
					double height = values[i, 3];
					double angleRotation = values[i, 4];
					ctl.AddFeature(new RectangleFeature(x, y, width, height, angleRotation));
				}
				return;
			}
			if (datasetType == typeof(float))
			{
				float[,] values2 = null;
				hr.ReadDataset(datasetName, ref values2);
				if (values2.GetLength(1) != 5)
				{
					throw new Exception("Malformed rectangular hdf5 dataset " + datasetName);
				}
				int count2 = values2.GetLength(0) - 1;
				for (int j = 0; j <= count2; j++)
				{
					double x2 = values2[j, 0];
					double y2 = values2[j, 1];
					double width2 = values2[j, 2];
					double height2 = values2[j, 3];
					double angleRotation2 = values2[j, 4];
					ctl.AddFeature(new RectangleFeature(x2, y2, width2, height2, angleRotation2));
				}
				return;
			}
			throw new NotImplementedException();
		}
	}

	public static void WriteRectangleFeatures(RectangleFeatureLayer rfl, H5Writer hw, string datasetName)
	{
		if (hw.ExistsAsDataset(datasetName))
		{
			hw.Delete(datasetName);
		}
		checked
		{
			if (rfl.FeatureTable().Rows.Count > 0)
			{
				double[,] doubleValues = new double[rfl.FeatureCount() - 1 + 1, 5];
				int count = rfl.FeatureCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					RectangleFeature rectangleFeature = rfl.Rectangle(i);
					doubleValues[i, 0] = rectangleFeature.Center.X;
					doubleValues[i, 1] = rectangleFeature.Center.Y;
					doubleValues[i, 2] = rectangleFeature.Width;
					doubleValues[i, 3] = rectangleFeature.Height;
					doubleValues[i, 4] = rectangleFeature.AngleRotation;
				}
				hw.WriteArray(datasetName, doubleValues);
				hw.WriteAttribute(datasetName, "Column Ordering", "X, Y, Width, Height, AngleRotation");
			}
		}
	}

	public static void ReadTriangleFeatures(TriangleFeatureLayer ctl, H5Reader hr, string datasetName)
	{
		if (!hr.ExistsAsDataset(datasetName))
		{
			return;
		}
		Type datasetType = hr.GetDatasetType(datasetName);
		checked
		{
			if (datasetType == typeof(double))
			{
				double[,] values = null;
				hr.ReadDataset(datasetName, ref values);
				if (values.GetLength(1) != 9)
				{
					throw new Exception("Malformed rectangular hdf5 dataset " + datasetName);
				}
				int count = values.GetLength(0) - 1;
				for (int i = 0; i <= count; i++)
				{
					double ax = values[i, 0];
					double ay = values[i, 1];
					double x = values[i, 2];
					double y = values[i, 3];
					double x2 = values[i, 4];
					double y2 = values[i, 5];
					double x3 = values[i, 6];
					double y3 = values[i, 7];
					double angleRotation = values[i, 8];
					ctl.AddFeature(new Triangle(new PointM(ax, ay), new Point(x, y), new Point(x2, y2), new Point(x3, y3), angleRotation));
				}
				return;
			}
			if (datasetType == typeof(float))
			{
				float[,] values2 = null;
				hr.ReadDataset(datasetName, ref values2);
				if (values2.GetLength(1) != 9)
				{
					throw new Exception("Malformed rectangular hdf5 dataset " + datasetName);
				}
				int count2 = values2.GetLength(0) - 1;
				for (int j = 0; j <= count2; j++)
				{
					double ax2 = values2[j, 0];
					double ay2 = values2[j, 1];
					double x4 = values2[j, 2];
					double y4 = values2[j, 3];
					double x5 = values2[j, 4];
					double y5 = values2[j, 5];
					double x6 = values2[j, 6];
					double y6 = values2[j, 7];
					double angleRotation2 = values2[j, 8];
					ctl.AddFeature(new Triangle(new PointM(ax2, ay2), new Point(x4, y4), new Point(x5, y5), new Point(x6, y6), angleRotation2));
				}
				return;
			}
			throw new NotImplementedException();
		}
	}

	public static void WriteTriangleFeatures(TriangleFeatureLayer tfl, H5Writer hw, string datasetName)
	{
		if (hw.ExistsAsDataset(datasetName))
		{
			hw.Delete(datasetName);
		}
		checked
		{
			if (tfl.FeatureTable().Rows.Count > 0)
			{
				double[,] doubleValues = new double[tfl.FeatureCount() - 1 + 1, 9];
				int count = tfl.FeatureCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					Triangle triangle = tfl.Triangle(i);
					doubleValues[i, 0] = triangle.Center.X;
					doubleValues[i, 1] = triangle.Center.Y;
					doubleValues[i, 2] = triangle.PointA.X;
					doubleValues[i, 3] = triangle.PointA.Y;
					doubleValues[i, 4] = triangle.PointB.X;
					doubleValues[i, 5] = triangle.PointB.Y;
					doubleValues[i, 6] = triangle.PointC.X;
					doubleValues[i, 7] = triangle.PointC.Y;
					doubleValues[i, 8] = triangle.AngleRotation;
				}
				hw.WriteArray(datasetName, doubleValues);
				hw.WriteAttribute(datasetName, "Column Order", "Point A X Point A Y, Point B X, Point B Y, Point C X, Point C Y, AngleRotation");
			}
		}
	}

	public static void ReadPierFeatures(PierFeatureLayer ptl, H5Reader hr, string datasetName)
	{
		if (!hr.ExistsAsDataset(datasetName))
		{
			return;
		}
		Type datasetType = hr.GetDatasetType(datasetName);
		checked
		{
			if (datasetType == typeof(double))
			{
				double[,] values = null;
				hr.ReadDataset(datasetName, ref values);
				if (values.GetLength(1) != 15)
				{
					throw new Exception("Malformed rectangular hdf5 dataset " + datasetName);
				}
				int count = values.GetLength(0) - 1;
				PointM center = default;
				double width = default;
				for (int i = 0; i <= count; i++)
				{
					double computed = values[i, 0];
					double computed2 = values[i, 1];
					double width3 = values[i, 2];
					double rectangleHeight = values[i, 3];
					double computed3 = values[i, 4];
					double computed4 = values[i, 5];
					double computed5 = values[i, 6];
					double topCapRadiusY = values[i, 7];
					double a = values[i, 8];
					double computed6 = values[i, 9];
					double computed7 = values[i, 10];
					double computed8 = values[i, 11];
					double bottomCapRadiusY = values[i, 12];
					double a2 = values[i, 13];
					double angleRotation = values[i, 14];
					if (computed > 0.0 && computed2 > 0.0)
					{
						center = new PointM(computed, computed2);
						width = width3;
					}
					else
					{
						if (computed3 > 0.0 && computed4 > 0.0)
						{
							center = new PointM(computed3, computed4);
							width = computed5 * 2.0;
						}
						if (computed6 > 0.0 && computed7 > 0.0)
						{
							center = new PointM(computed6, computed7);
							width = computed8 * 2.0;
						}
					}
					ptl.AddFeature(new Pier(center, angleRotation, width, rectangleHeight, topCapRadiusY, Pier.IntToCapType[(int)System.Math.Round(a)], bottomCapRadiusY, Pier.IntToCapType[(int)System.Math.Round(a2)]));
				}
				return;
			}
			if (datasetType == typeof(float))
			{
				float[,] values2 = null;
				hr.ReadDataset(datasetName, ref values2);
				if (values2.GetLength(1) != 15)
				{
					throw new Exception("Malformed rectangular hdf5 dataset " + datasetName);
				}
				int count2 = values2.GetLength(0) - 1;
				PointM center2 = default;
				double width2 = default;
				for (int j = 0; j <= count2; j++)
				{
					double values2Term = values2[j, 0];
					double values2Term2 = values2[j, 1];
					double width22 = values2[j, 2];
					double rectangleHeight2 = values2[j, 3];
					double values2Term3 = values2[j, 4];
					double values2Term4 = values2[j, 5];
					double values2Term5 = values2[j, 6];
					double topCapRadiusY2 = values2[j, 7];
					double a3 = values2[j, 8];
					double values2Term6 = values2[j, 9];
					double values2Term7 = values2[j, 10];
					double values2Term8 = values2[j, 11];
					double bottomCapRadiusY2 = values2[j, 12];
					double a4 = values2[j, 13];
					double angleRotation2 = values2[j, 14];
					if (values2Term > 0.0 && values2Term2 > 0.0)
					{
						center2 = new PointM(values2Term, values2Term2);
						width2 = width22;
					}
					else
					{
						if (values2Term3 > 0.0 && values2Term4 > 0.0)
						{
							center2 = new PointM(values2Term3, values2Term4);
							width2 = values2Term5 * 2.0;
						}
						if (values2Term6 > 0.0 && values2Term7 > 0.0)
						{
							center2 = new PointM(values2Term6, values2Term7);
							width2 = values2Term8 * 2.0;
						}
					}
					ptl.AddFeature(new Pier(center2, angleRotation2, width2, rectangleHeight2, topCapRadiusY2, Pier.IntToCapType[(int)System.Math.Round(a3)], bottomCapRadiusY2, Pier.IntToCapType[(int)System.Math.Round(a4)]));
				}
				return;
			}
			throw new NotImplementedException();
		}
	}

	public static void WritePierFeatures(PierFeatureLayer pfl, H5Writer hw, string datasetName)
	{
		if (hw.ExistsAsDataset(datasetName))
		{
			hw.Delete(datasetName);
		}
		if (pfl.FeatureTable().Rows.Count <= 0)
		{
			return;
		}
		checked
		{
			double[,] doubleValues = new double[pfl.FeatureCount() - 1 + 1, 15];
			int count = pfl.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				doubleValues[i, 14] = 0.0;
				AffineFeature topCap = pfl.Pier(i).TopCap;
				double radiusXEllipseTerm = 0.0;
				double radiusYEllipseTerm = 0.0;
				double unity = 0.0;
				if (topCap != null)
				{
					if (topCap.GetType() == typeof(Ellipse))
					{
						Ellipse ellipse = (Ellipse)topCap;
						radiusXEllipseTerm = ellipse.RadiusX;
						radiusYEllipseTerm = ellipse.RadiusY;
						unity = 0.0;
					}
					else if (topCap.GetType() == typeof(Triangle))
					{
						Triangle triangle = (Triangle)topCap;
						radiusXEllipseTerm = triangle.Width / 2.0;
						radiusYEllipseTerm = triangle.Height / 2.0;
						unity = 1.0;
					}
				}
				if (topCap == null)
				{
					doubleValues[i, 4] = 0.0;
					doubleValues[i, 5] = 0.0;
					doubleValues[i, 6] = 0.0;
					doubleValues[i, 7] = 0.0;
					doubleValues[i, 8] = 0.0;
				}
				else
				{
					doubleValues[i, 4] = topCap.Center.X;
					doubleValues[i, 5] = topCap.Center.Y;
					doubleValues[i, 6] = radiusXEllipseTerm;
					doubleValues[i, 7] = radiusYEllipseTerm;
					doubleValues[i, 8] = unity;
					doubleValues[i, 14] = topCap.AngleRotation;
				}
				AffineFeature bottomCap = pfl.Pier(i).BottomCap;
				double radiusXEllipse2Term = 0.0;
				double radiusYEllipse2Term = 0.0;
				double unity2 = 0.0;
				if (bottomCap != null)
				{
					if (bottomCap.GetType() == typeof(Ellipse))
					{
						Ellipse ellipse2 = (Ellipse)bottomCap;
						radiusXEllipse2Term = ellipse2.RadiusX;
						radiusYEllipse2Term = ellipse2.RadiusY;
						unity2 = 0.0;
					}
					else if (bottomCap.GetType() == typeof(Triangle))
					{
						Triangle triangle2 = (Triangle)bottomCap;
						radiusXEllipse2Term = triangle2.Width / 2.0;
						radiusYEllipse2Term = triangle2.Height / 2.0;
						unity2 = 1.0;
					}
				}
				if (bottomCap == null)
				{
					doubleValues[i, 9] = 0.0;
					doubleValues[i, 10] = 0.0;
					doubleValues[i, 11] = 0.0;
					doubleValues[i, 12] = 0.0;
					doubleValues[i, 13] = 0.0;
				}
				else
				{
					doubleValues[i, 9] = bottomCap.Center.X;
					doubleValues[i, 10] = bottomCap.Center.Y;
					doubleValues[i, 11] = radiusXEllipse2Term;
					doubleValues[i, 12] = radiusYEllipse2Term;
					doubleValues[i, 13] = unity2;
					doubleValues[i, 14] = (bottomCap.AngleRotation + 180.0) % 360.0;
				}
				RectangleFeature body = pfl.Pier(i).Body;
				if (body == null)
				{
					doubleValues[i, 0] = 0.0;
					doubleValues[i, 1] = 0.0;
					doubleValues[i, 2] = 0.0;
					doubleValues[i, 3] = 0.0;
				}
				else
				{
					doubleValues[i, 0] = body.Center.X;
					doubleValues[i, 1] = body.Center.Y;
					doubleValues[i, 2] = body.Width;
					doubleValues[i, 3] = body.Height;
					doubleValues[i, 14] = body.AngleRotation;
				}
			}
			hw.WriteArray(datasetName, doubleValues);
			hw.WriteAttribute(datasetName, "Column Ordering", "Body X, Body Y, Body Width, Body Height, TopCap X, TopCap Y, TopCap RadiusX, TopCap RadiusY, TopCap Type, BottomCap X, BottomCap Y, BottomCap RadiusX, BottomCap RadiusY, BottomCap Type, AngleRotation");
		}
	}

	public static void ReadMultipointFeatures(FeatureLayer ftl, H5Reader hr, string datasetName)
	{
		HDFBufferedReader<int> infoBhw = null;
		HDFBufferedReader<double> pointsBhw = null;
		bool hasZ = default;
		bool hasM = default;
		if (!BeginPointsBhr(hr, datasetName, ref hasZ, ref hasM, ref infoBhw, ref pointsBhw))
		{
			return;
		}
		PointMs pointMs = new PointMs();
		checked
		{
			int count = infoBhw.Rows - 1;
			int col = default;
			int col2 = default;
			PointM PointM = default;
			for (int i = 0; i <= count; i++)
			{
				infoBhw.ReadRow(ref col, ref col2);
				pointMs.Clear();
				if (col2 > pointMs.Capacity)
				{
					pointMs.Capacity = col2;
				}
				int col22 = col2;
				for (int j = 1; j <= col22; j++)
				{
					PointFromBhw(hasZ, hasM, pointsBhw, ref PointM);
					pointMs.Add(PointM);
				}
				ftl.AddFeature(new MultiPoint(pointMs));
			}
			infoBhw.Dispose();
			pointsBhw.Dispose();
		}
	}

	public static void WriteMultiPointFeatures(FeatureLayer ftl, H5Writer hw, string datasetName)
	{
		if (hw.ExistsAsDataset(datasetName))
		{
			hw.Delete(datasetName);
		}
		DataTable dataTable = ftl.FeatureTable();
		if (dataTable.Rows.Count <= 0)
		{
			return;
		}
		if (ftl.Extent == null)
		{
			ftl.ComputeExtent();
		}
		Extent extent = ftl.Extent;
		HDFBufferedWriter<int> infoBhw = null;
		HDFBufferedWriter<double> pointsBhw = null;
		BeginPointsBhw(hw, datasetName, dataTable.Rows.Count, extent.HasZ, extent.HasM, ref infoBhw, ref pointsBhw);
		checked
		{
			int count = dataTable.Rows.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Feature feature = ftl.Feature(i);
				if (feature is MultiPoint)
				{
					MultiPoint multiPoint = (MultiPoint)feature;
					infoBhw.AddRow(pointsBhw.RowsWritten, multiPoint.Count);
					int count2 = multiPoint.Count - 1;
					for (int j = 0; j <= count2; j++)
					{
						WritePointHelper(extent, pointsBhw, multiPoint.PointM(j));
					}
				}
				else
				{
					infoBhw.AddRow(pointsBhw.RowsWritten, 0);
				}
			}
			infoBhw.Dispose();
			pointsBhw.Dispose();
		}
	}

	public static void ReadPolyFeatures(FeatureLayer ftl, H5Reader hr, string datasetName, bool sanitizeDuplicatePoints = false, bool forcePolygon = false)
	{
		HDFBufferedReader<int> infoBhw = null;
		HDFBufferedReader<int> partBhw = null;
		HDFBufferedReader<double> pointsBhw = null;
		bool hasZ = default;
		bool hasM = default;
		bool isPolygon = default;
		if (!BeginPolyBhr(hr, datasetName, ref hasZ, ref hasM, ref isPolygon, ref infoBhw, ref partBhw, ref pointsBhw))
		{
			return;
		}
		if (forcePolygon)
		{
			isPolygon = true;
		}
		PointMs pointMs = new PointMs();
		checked
		{
			int count = infoBhw.Rows - 1;
			int col = default;
			int col2 = default;
			int col3 = default;
			int col4 = default;
			int col5 = default;
			int col6 = default;
			PointM PointM = default;
			for (int i = 0; i <= count; i++)
			{
				infoBhw.ReadRow(ref col, ref col2, ref col3, ref col4);
				int[] partStartIndex = null;
				if (partBhw != null)
				{
					List<int> intList = new List<int>();
					int col42 = col4;
					for (int j = 1; j <= col42; j++)
					{
						partBhw.ReadRow(ref col5, ref col6);
						intList.Add(col5);
					}
					partStartIndex = intList.ToArray();
				}
				pointMs.Clear();
				if (col2 > pointMs.Capacity)
				{
					pointMs.Capacity = col2;
				}
				PointM pointM = new PointM(double.MinValue, double.MinValue);
				int col22 = col2;
				for (int k = 1; k <= col22; k++)
				{
					PointFromBhw(hasZ, hasM, pointsBhw, ref PointM);
					if (!sanitizeDuplicatePoints || !pointM.EqualXY(PointM))
					{
						pointMs.Add(PointM);
						pointM = PointM;
					}
				}
				if (ftl.FeatureCount() <= i)
				{
					if (isPolygon)
					{
						ftl.AddFeature(new Polygon(pointMs, partStartIndex));
					}
					else
					{
						ftl.AddFeature(new Polyline(pointMs, partStartIndex));
					}
				}
				else if (isPolygon)
				{
					ftl.SetFeature(i, new Polygon(pointMs, partStartIndex));
				}
				else
				{
					ftl.SetFeature(i, new Polyline(pointMs, partStartIndex));
				}
			}
			infoBhw.Dispose();
			partBhw?.Dispose();
			pointsBhw.Dispose();
		}
	}

	public static void WritePolylineFeatures(PolylineFeatureLayer ftl, H5Writer hw, string datasetName)
	{
		WritePolyFeatures(ftl, hw, datasetName, isPolygon: false);
	}

	public static void WritePolygonFeatures(PolygonFeatureLayer ftl, H5Writer hw, string datasetName)
	{
		WritePolyFeatures(ftl, hw, datasetName, isPolygon: true);
	}

	private static void WritePolyFeatures(FeatureLayer ftl, H5Writer hw, string datasetName, bool isPolygon)
	{
		DeletePolyDataset(hw, datasetName);
		DataTable dataTable = ftl.FeatureTable();
		if (dataTable.Rows.Count <= 0)
		{
			return;
		}
		if (ftl.Extent == null)
		{
			ftl.ComputeExtent();
		}
		Extent extent = ftl.Extent;
		HDFBufferedWriter<int> infoBhw = null;
		HDFBufferedWriter<int> partBhw = null;
		HDFBufferedWriter<double> pointsBhw = null;
		BeginPolyBhw(hw, datasetName, dataTable.Rows.Count, extent.HasZ, extent.HasM, isPolygon, ref infoBhw, ref partBhw, ref pointsBhw);
		checked
		{
			foreach (Feature feature in ftl.Features())
			{
				if (feature is Polyline)
				{
					Polyline polyline = (Polyline)feature;
					infoBhw.AddRow(pointsBhw.RowsWritten, polyline.Count, partBhw.RowsWritten, polyline.PartsCount());
					int count = polyline.PartsCount() - 1;
					for (int i = 0; i <= count; i++)
					{
						partBhw.AddRow(polyline.PartStartIndex(i), polyline.PointCountInPart(i));
					}
					int count2 = polyline.Count - 1;
					for (int j = 0; j <= count2; j++)
					{
						WritePointHelper(extent, pointsBhw, polyline.PointM(j));
					}
				}
				else
				{
					infoBhw.AddRow(pointsBhw.RowsWritten, 0, 0, 0);
				}
			}
			infoBhw.Dispose();
			partBhw.Dispose();
			pointsBhw.Dispose();
		}
	}

	public static void ReadTINFeatures(FeatureLayer ftl, H5Reader hr, string datasetname)
	{
		HDFBufferedReader<int> infoBhw = null;
		HDFBufferedReader<int> triBhw = null;
		HDFBufferedReader<double> pointsBhw = null;
		bool hasZ = default;
		bool hasM = default;
		if (!BeginTINBhr(hr, datasetname, ref hasZ, ref hasM, ref infoBhw, ref triBhw, ref pointsBhw))
		{
			return;
		}
		PointMs pointMs = new PointMs();
		new List<int>();
		checked
		{
			int count = infoBhw.Rows - 1;
			int col = default;
			int col2 = default;
			int col3 = default;
			int col4 = default;
			PointM PointM = default;
			for (int i = 0; i <= count; i++)
			{
				infoBhw.ReadRow(ref col, ref col2, ref col3, ref col4);
				TriangleStruct[] triangleStructs = new TriangleStruct[col4 - 1 + 1];
				int count2 = col4 - 1;
				for (int j = 0; j <= count2; j++)
				{
					triBhw.ReadRow(ref triangleStructs[j].p0, ref triangleStructs[j].p1, ref triangleStructs[j].p2);
				}
				pointMs.Clear();
				if (col2 > pointMs.Capacity)
				{
					pointMs.Capacity = col2;
				}
				int col22 = col2;
				for (int k = 1; k <= col22; k++)
				{
					PointFromBhw(hasZ, hasM, pointsBhw, ref PointM);
					pointMs.Add(PointM);
				}
				Tin tin = new Tin(pointMs, triangleStructs);
				tin.ComputeEdgesAndAdjacentTriangles();
				ftl.AddFeature(tin);
			}
			infoBhw.Dispose();
			triBhw.Dispose();
			pointsBhw.Dispose();
		}
	}

	public static void WriteTINFeatures(FeatureLayer ftl, H5Writer hw, string datasetName)
	{
		DeleteTINDataset(hw, datasetName);
		DataTable dataTable = ftl.FeatureTable();
		if (dataTable.Rows.Count <= 0)
		{
			return;
		}
		if (ftl.Extent == null)
		{
			ftl.ComputeExtent();
		}
		Extent extent = ftl.Extent;
		HDFBufferedWriter<int> infoBhw = null;
		HDFBufferedWriter<int> triBhw = null;
		HDFBufferedWriter<double> pointsBhw = null;
		if (ftl.Extent == null)
		{
			ftl.ComputeExtent();
		}
		BeginTINBhw(hw, datasetName, dataTable.Rows.Count, extent.HasZ, extent.HasM, ref infoBhw, ref triBhw, ref pointsBhw);
		checked
		{
			int count = dataTable.Rows.Count - 1;
			int p = default;
			int p2 = default;
			int p3 = default;
			for (int i = 0; i <= count; i++)
			{
				Feature feature = ftl.Feature(i);
				if (feature is Tin)
				{
					Tin tin = (Tin)feature;
					infoBhw.AddRow(pointsBhw.RowsWritten, tin.Count, triBhw.RowsWritten, tin.TriangleCount());
					int count2 = tin.TriangleCount() - 1;
					for (int j = 0; j <= count2; j++)
					{
						tin.TrianglePoints(j, ref p, ref p2, ref p3);
						triBhw.AddRow(p, p2, p3);
					}
					int count3 = tin.Count - 1;
					for (int k = 0; k <= count3; k++)
					{
						WritePointHelper(extent, pointsBhw, tin.PointM(k));
					}
				}
				else
				{
					infoBhw.AddRow(pointsBhw.RowsWritten, 0);
				}
			}
			infoBhw.Dispose();
			triBhw.Dispose();
			pointsBhw.Dispose();
		}
	}

	public static void ReadPointCollection<T>(FeatureLayer ftl, H5Reader hr, string datasetname, string colName) where T : List<Point2D>, new()
	{
		DataTable dataTable = ftl.FeatureTable();
		if (dataTable.Rows.Count <= 0)
		{
			return;
		}
		int indexOf = dataTable.Columns.IndexOf(colName);
		if (indexOf < 0)
		{
			throw new Exception("Error reading feature columns from file: " + hr.Filename + " column: '" + colName + "' not found.");
		}
		if (!ftl.ColumnMatchesType(indexOf, typeof(IEnumerable<Point2D>)))
		{
			throw new Exception("Error reading feature columns from file: " + hr.Filename + " column: '" + colName + "' is not a Point2DCollection type.");
		}
		HDFBufferedReader<int> infoBhw = null;
		HDFBufferedReader<float> valuesBhw = null;
		if (!BeginProfilesBhr(hr, datasetname, ref infoBhw, ref valuesBhw))
		{
			return;
		}
		checked
		{
			int count = infoBhw.Rows - 1;
			int col = default;
			int col2 = default;
			float col3 = default;
			float col4 = default;
			for (int i = 0; i <= count; i++)
			{
				infoBhw.ReadRow(ref col, ref col2);
				T rows = new T
				{
					Capacity = col2
				};
				int col22 = col2;
				for (int j = 1; j <= col22; j++)
				{
					valuesBhw.ReadRow(ref col3, ref col4);
					rows.Add(new Point2D(col3, col4));
				}
				dataTable.Rows[i][indexOf] = rows;
			}
			infoBhw.Dispose();
			valuesBhw.Dispose();
		}
	}

	public static void ReadPointCollection(H5Reader hr, string datasetname, Action<int, List<Point2D>> SetPointCollection)
	{
		HDFBufferedReader<int> infoBhw = null;
		HDFBufferedReader<float> valuesBhw = null;
		if (!BeginProfilesBhr(hr, datasetname, ref infoBhw, ref valuesBhw))
		{
			return;
		}
		checked
		{
			int count = infoBhw.Rows - 1;
			int col = default;
			int col2 = default;
			float col3 = default;
			float col4 = default;
			for (int i = 0; i <= count; i++)
			{
				infoBhw.ReadRow(ref col, ref col2);
				List<Point2D> point2Ds = new List<Point2D>();
				point2Ds.Capacity = col2;
				int col22 = col2;
				for (int j = 1; j <= col22; j++)
				{
					valuesBhw.ReadRow(ref col3, ref col4);
					point2Ds.Add(new Point2D(col3, col4));
				}
				SetPointCollection(i, point2Ds);
			}
			infoBhw.Dispose();
			valuesBhw.Dispose();
		}
	}

	public static List<List<Point2D>> ReadPointCollection(H5Reader hr, string datasetname)
	{
		List<List<Point2D>> pointCollection = new List<List<Point2D>>();
		HDFBufferedReader<int> infoBhw = null;
		HDFBufferedReader<float> valuesBhw = null;
		checked
		{
			if (BeginProfilesBhr(hr, datasetname, ref infoBhw, ref valuesBhw))
			{
				int count = infoBhw.Rows - 1;
				int col = default;
				int col2 = default;
				float col3 = default;
				float col4 = default;
				for (int i = 0; i <= count; i++)
				{
					infoBhw.ReadRow(ref col, ref col2);
					List<Point2D> point2Ds = new List<Point2D>();
					point2Ds.Capacity = col2;
					int col22 = col2;
					for (int j = 1; j <= col22; j++)
					{
						valuesBhw.ReadRow(ref col3, ref col4);
						point2Ds.Add(new Point2D(col3, col4));
					}
					pointCollection.Add(point2Ds);
				}
				infoBhw.Dispose();
				valuesBhw.Dispose();
			}
			return pointCollection;
		}
	}

	public static void WritePointCollection(FeatureLayer ftl, H5Writer hw, string datasetName, string colName)
	{
		DataTable dataTable = ftl.FeatureTable();
		if (dataTable.Rows.Count > 0)
		{
			int indexOf = dataTable.Columns.IndexOf(colName);
			if (indexOf < 0)
			{
				throw new Exception("Error writing layer columns to HDF5. Profile column " + colName + " not found.");
			}
			if (!ftl.ColumnMatchesType(indexOf, typeof(IEnumerable<Point2D>)))
			{
				throw new Exception("Error writing layer columns to HDF5. Profile " + colName + " is not a collection of Point2D.");
			}
			IEnumerable<IEnumerable<Point2D>> pointCollections = from row in ftl.FeatureRows()
				select row[indexOf] into obj
				select Information.IsDBNull(RuntimeHelpers.GetObjectValue(obj)) ? null : ((IEnumerable<Point2D>)obj);
			WritePointCollection(hw, datasetName, pointCollections);
		}
	}

	public static void WritePointCollection(H5Writer hw, string datasetName, IEnumerable<IEnumerable<Point2D>> pointCollections, string xAttributeName = "X", string yAttributeName = "Y")
	{
		DeleteMultipointDataset(hw, datasetName);
		HDFBufferedWriter<int> infoBhw = null;
		HDFBufferedWriter<float> valuesBhw = null;
		BeginProfile(hw, datasetName, pointCollections.Count(), ref infoBhw, ref valuesBhw, xAttributeName, yAttributeName);
		foreach (IEnumerable<Point2D> pointCollection in pointCollections)
		{
			if (pointCollection == null || pointCollection.Count() == 0)
			{
				infoBhw.AddRow(valuesBhw.RowsWritten, 0);
				continue;
			}
			infoBhw.AddRow(valuesBhw.RowsWritten, pointCollection.Count());
			foreach (Point2D pointCollection2 in pointCollection)
			{
				valuesBhw.AddRow(pointCollection2.X, pointCollection2.Y);
			}
		}
		infoBhw.Dispose();
		valuesBhw.Dispose();
	}

	public static List<SavedProfileInfo> ReadProfileMetadata(H5Reader hr, string datasetPrefix)
	{
		List<SavedProfileInfo> savedProfileInfos = new List<SavedProfileInfo>();
		string datasetPrefixTerm = datasetPrefix + " Info";
		string datasetPrefixTerm2 = datasetPrefix + " Values";
		checked
		{
			if (hr.ExistsAsDataset(datasetPrefixTerm) && hr.ExistsAsDataset(datasetPrefixTerm2))
			{
				int[,] values = null;
				hr.ReadDataset(datasetPrefixTerm, ref values);
				float[,] values2 = null;
				hr.ReadDataset(datasetPrefixTerm2, ref values2);
				int count = values.GetLength(0) - 1;
				for (int i = 0; i <= count; i++)
				{
					int ordinal = values[i, 0];
					int ordinal2 = values[i, 1];
					if (ordinal2 == 0)
					{
						savedProfileInfos.Add(new SavedProfileInfo(ordinal, ordinal2, 0f, 0f, hr.Filename, datasetPrefix + " Values"));
					}
					else
					{
						savedProfileInfos.Add(new SavedProfileInfo(ordinal, ordinal2, values2[ordinal, 0], values2[ordinal + ordinal2 - 1, 0], hr.Filename, datasetPrefix + " Values"));
					}
				}
			}
			return savedProfileInfos;
		}
	}

	public static void SaveAttribute(H5Writer hw, string objectName, RasterM raster, string attributePrefix = "Raster")
	{
		string attributeName = attributePrefix + " Rows";
		string attributeName2 = attributePrefix + " Cols";
		string attributeName3 = attributePrefix + " Left";
		string attributeName4 = attributePrefix + " Top";
		string attributeName5 = attributePrefix + " Cellsize";
		hw.WriteAttribute(objectName, attributeName2, raster.Cols);
		hw.WriteAttribute(objectName, attributeName, raster.Rows);
		hw.WriteAttribute(objectName, attributeName3, raster.Left);
		hw.WriteAttribute(objectName, attributeName4, raster.Top);
		hw.WriteAttribute(objectName, attributeName5, raster.CellSize);
	}

	public static bool TryReadRaster(H5Reader hr, string objectName, ref RasterM raster, string attributePrefix = "Raster")
	{
		string attributeName = attributePrefix + " Rows";
		string attributeName2 = attributePrefix + " Cols";
		string attributeName3 = attributePrefix + " Left";
		string attributeName4 = attributePrefix + " Top";
		string attributeName5 = attributePrefix + " Cellsize";
		int ordinal = default;
		if (!hr.TryReadAttributeScalar(objectName, attributeName, ref ordinal))
		{
			return false;
		}
		int ordinal2 = default;
		if (!hr.TryReadAttributeScalar(objectName, attributeName2, ref ordinal2))
		{
			return false;
		}
		double computed = default;
		if (!hr.TryReadAttributeScalar(objectName, attributeName3, ref computed))
		{
			return false;
		}
		double computed2 = default;
		if (!hr.TryReadAttributeScalar(objectName, attributeName4, ref computed2))
		{
			return false;
		}
		double computed3 = default;
		if (!hr.TryReadAttributeScalar(objectName, attributeName5, ref computed3) && !hr.TryReadAttributeScalar(objectName, attributePrefix + " CellSize", ref computed3))
		{
			return false;
		}
		raster = new RasterM(new PointM(computed, computed2), ordinal, ordinal2, computed3);
		return true;
	}
}
