using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Geospatial.GDALAssist;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.TinHelpers;
using RasMapperLib.Utilities;
using Utility.Extensions;

namespace RasMapperLib;

public class ShapefileStorage
{
	private class SHP
	{
		public class Point
		{
			public const string Name = "Point";

			public const int XY = 1;

			public const int XYZM = 11;

			public const int XYM = 21;
		}

		public class MultiPoint
		{
			public const string Name = "MultiPoint";

			public const int XY = 8;

			public const int XYZM = 18;

			public const int XYM = 28;
		}

		public class Polyline
		{
			public const string Name = "Polyline";

			public const int XY = 3;

			public const int XYZM = 13;

			public const int XYM = 23;
		}

		public class Polygon
		{
			public const string Name = "Polygon";

			public const int XY = 5;

			public const int XYZM = 15;

			public const int XYM = 25;
		}

		public class MultiPatch
		{
			public const string Name = "MultiPatch";

			public const int ALL = 31;
		}

		public const int ERR = -1;
	}

	private struct dbfField
	{
		public string Name;

		public string TypeID;

		public Type Type;

		public int Length;

		public int NumDecimal;

		public bool WriteField;
	}

	public const string sFileFilter = "Shapefile (*.shp)|*.shp";

	public static string MakeDbfFilename(string shpFilename)
	{
		return System.IO.Path.ChangeExtension(shpFilename, "dbf");
	}

	public static string MakeShxFilename(string shpFilename)
	{
		return System.IO.Path.ChangeExtension(shpFilename, "shx");
	}

	public static string MakePrjFilename(string shpFilename)
	{
		return System.IO.Path.ChangeExtension(shpFilename, "prj");
	}

	public static ESRIProjection GetProjection(string shpFilename)
	{
		string prjFilename = MakePrjFilename(shpFilename);
		ESRIProjection result;
		if (!File.Exists(prjFilename))
		{
			result = null;
		}
		else
		{
			try
			{
				result = new ESRIProjection(prjFilename);
			}
			catch (Exception projectError)
			{
				ProjectData.SetProjectError(projectError);
				result = null;
				ProjectData.ClearProjectError();
			}
		}
		return result;
	}

	private static Extent LoadExtent(string shpFilename)
	{
		if (!File.Exists(shpFilename))
		{
			throw new FileNotFoundException(shpFilename);
		}
		using FileStream fileStream = new FileStream(shpFilename, FileMode.Open, FileAccess.Read);
		byte[] byteValues = new byte[100];
		fileStream.ReadExactly(byteValues, 0, 100);
		fileStream.Close();
		BitConverter.ToInt32(byteValues, 32);
		double minX = BitConverter.ToDouble(byteValues, 36);
		double minY = BitConverter.ToDouble(byteValues, 44);
		double maxX = BitConverter.ToDouble(byteValues, 52);
		double maxY = BitConverter.ToDouble(byteValues, 60);
		double minZ = BitConverter.ToDouble(byteValues, 68);
		double maxZ = BitConverter.ToDouble(byteValues, 76);
		double minM = BitConverter.ToDouble(byteValues, 84);
		double maxM = BitConverter.ToDouble(byteValues, 92);
		ESRIProjection projection = GetProjection(shpFilename);
		if (projection != null && SharedData.SRSProjection != null && !projection.Equals(SharedData.SRSProjection))
		{
			PointMs pointMs = new PointMs();
			pointMs.Add(minX, minY);
			pointMs.Add(minX, maxY);
			pointMs.Add(maxX, maxY);
			pointMs.Add(maxX, minY);
			pointMs.Project(projection, SharedData.SRSProjection);
			pointMs.MaxMinXY(ref maxX, ref minX, ref maxY, ref minY);
		}
		return new Extent(maxX, minX, maxY, minY, maxZ, minZ, maxM, minM);
	}

	public static FeatureLayer LoadFeatureLayerShell(string shpFilename)
	{
		string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(shpFilename);
		string path = MakeShxFilename(shpFilename);
		if (!File.Exists(shpFilename) || !File.Exists(path))
		{
			return new FeatureLayer(fileNameWithoutExtension, shpFilename);
		}
		byte[] byteValues = new byte[36];
		using (FileStream fileStream = File.OpenRead(shpFilename))
		{
			fileStream.ReadExactly(byteValues, 0, 36);
		}
		switch (BitConverter.ToInt32(byteValues, 32))
		{
		case 1:
		case 11:
		case 21:
			return new PointFeatureLayer(fileNameWithoutExtension, shpFilename);
		case 8:
		case 18:
		case 28:
			return new MultiPointFeatureLayer(fileNameWithoutExtension, shpFilename);
		case 3:
		case 13:
		case 23:
			return new PolylineFeatureLayer(fileNameWithoutExtension, shpFilename);
		case 5:
		case 15:
		case 25:
			return new PolygonFeatureLayer(fileNameWithoutExtension, shpFilename);
		case 31:
			return new PolygonFeatureLayer(fileNameWithoutExtension, shpFilename);
		default:
			return new FeatureLayer(fileNameWithoutExtension, shpFilename);
		}
	}

	public static Type GetFeatureLayerType(string shpFilename)
	{
		System.IO.Path.GetFileNameWithoutExtension(shpFilename);
		string path = MakeShxFilename(shpFilename);
		if (!File.Exists(shpFilename) || !File.Exists(path))
		{
			return null;
		}
		byte[] byteValues = new byte[36];
		using (FileStream fileStream = File.OpenRead(shpFilename))
		{
			fileStream.ReadExactly(byteValues, 0, 36);
		}
		switch (BitConverter.ToInt32(byteValues, 32))
		{
		case 1:
		case 11:
		case 21:
			return typeof(PointFeatureLayer);
		case 8:
		case 18:
		case 28:
			return typeof(MultiPointFeatureLayer);
		case 3:
		case 13:
		case 23:
			return typeof(PolylineFeatureLayer);
		case 5:
		case 15:
		case 25:
			return typeof(PolygonFeatureLayer);
		case 31:
			return typeof(PolygonFeatureLayer);
		default:
			return null;
		}
	}

	public static void LoadFeatureTable(string shpFilename, FeatureLayer ftl)
	{
		string dbfFilename = MakeDbfFilename(shpFilename);
		string shxFilename = MakeShxFilename(shpFilename);
		dbfField[] fields = null;
		try
		{
			if (!File.Exists(shpFilename))
			{
				throw new FileNotFoundException(shpFilename);
			}
			if (!File.Exists(dbfFilename))
			{
				throw new FileNotFoundException(dbfFilename);
			}
			if (!File.Exists(shxFilename))
			{
				throw new FileNotFoundException(shxFilename);
			}
			LoadAttributeColumns(shpFilename, ftl, ref fields);
			LoadFeaturesNew(shpFilename, ftl);
			ReadAttributes(shpFilename, ftl, fields);
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ftl.ErrorMessage = "Load Error: " + ex2.Message;
			ProjectData.ClearProjectError();
		}
	}

	private static void LoadAttributeColumns(string shpFilename, FeatureLayer ftl, ref dbfField[] fields)
	{
		DataTable dataTable = ftl.FeatureTable();
		string path = MakeDbfFilename(shpFilename);
		using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read);
		byte[] byteValues = new byte[32];
		fileStream.ReadExactly(byteValues, 0, byteValues.Length);
		_ = byteValues[0];
		_ = byteValues[1];
		_ = byteValues[2];
		_ = byteValues[3];
		BitConverter.ToInt32(byteValues, 4);
		short bitConverterTerm = BitConverter.ToInt16(byteValues, 8);
		BitConverter.ToInt16(byteValues, 10);
		int ordinal = checked(bitConverterTerm - 32 - 1) / 32;
		_ = byteValues[28];
		_ = byteValues[29];
		checked
		{
			byte[] byteValuesArray = new byte[ordinal * 32 - 1 + 1];
			fileStream.ReadExactly(byteValuesArray, 0, byteValuesArray.Length);
			fileStream.Close();
			fields = new dbfField[ordinal - 1 + 1];
			int intervalIndex = 0;
			int count = ordinal - 1;
			for (int i = 0; i <= count; i++)
			{
				ref dbfField reference = ref fields[i];
				reference.Name = Encoding.UTF8.GetString(byteValuesArray, intervalIndex, 11);
				reference.Name = reference.Name.Trim('\0');
				reference.TypeID = Encoding.UTF8.GetString(byteValuesArray, intervalIndex + 11, 1);
				BitConverter.ToInt32(byteValuesArray, intervalIndex + 12);
				reference.Length = byteValuesArray[intervalIndex + 16];
				reference.NumDecimal = byteValuesArray[intervalIndex + 17];
				_ = byteValuesArray[intervalIndex + 18];
				BitConverter.ToInt32(byteValuesArray, intervalIndex + 19);
				BitConverter.ToInt32(byteValuesArray, intervalIndex + 23);
				switch (reference.TypeID)
				{
				case "I":
					reference.Type = typeof(int);
					break;
				case "N":
					if (reference.NumDecimal > 0)
					{
						reference.Type = typeof(double);
					}
					else
					{
						reference.Type = typeof(int);
					}
					break;
				case "F":
				case "B":
					reference.Type = typeof(double);
					break;
				case "L":
					reference.Type = typeof(bool);
					break;
				case "C":
					reference.Type = typeof(string);
					break;
				default:
					reference.Type = typeof(string);
					break;
				}
				DataColumn dataColumn = dataTable.Columns[reference.Name];
				if (dataColumn != null && (object)dataColumn.DataType != reference.Type)
				{
					if ((object)dataColumn.DataType == typeof(float) && (object)reference.Type == typeof(double))
					{
						reference.Type = typeof(float);
					}
					else
					{
						do
						{
							reference.Name += reference.TypeID;
						}
						while (dataTable.Columns.IndexOf(reference.Name) != -1);
					}
				}
				ftl.AddAttributeColumn(reference.Name, reference.Type);
				intervalIndex += 32;
			}
		}
	}

	private static void LoadFeatures(string shpFilename, FeatureLayer ftLayer)
	{
		string path = MakeShxFilename(shpFilename);
		if (!File.Exists(shpFilename) || !File.Exists(path))
		{
			throw new FileNotFoundException();
		}
		checked
		{
			double[,] XY = new double[1000 + 1, 2];
			double[] Z = new double[1000 + 1];
			double[] M = new double[1000 + 1];
			PointMs pointMs = new PointMs();
			bool condition = false;
			ESRIProjection projection = GetProjection(shpFilename);
			if (projection != null && SharedData.SRSProjection != null && !projection.IsEqual(SharedData.SRSProjection))
			{
				condition = true;
			}
			byte[] bytes = File.ReadAllBytes(path);
			int systemBitConverterSwapBytesToIntegerTerm = (int)System.Math.Round((double)(RasMapperLib.Utilities.Math.BitConverterSwapBytesToInteger(bytes, 24) - 50) / 4.0);
			byte[] allBytes = File.ReadAllBytes(shpFilename);
			BitConverter.ToInt32(allBytes, 32);
			int ordinal = 100;
			int count4 = systemBitConverterSwapBytesToIntegerTerm;
			double z = default;
			double m2 = default;
			for (int i = 1; i <= count4; i++)
			{
				int bitConverterSwapBytesToIntegerUtilitiesTerm = RasMapperLib.Utilities.Math.BitConverterSwapBytesToInteger(bytes, ordinal) * 2;
				ordinal += 8;
				RasMapperLib.Utilities.Math.BitConverterSwapBytesToInteger(allBytes, bitConverterSwapBytesToIntegerUtilitiesTerm);
				RasMapperLib.Utilities.Math.BitConverterSwapBytesToInteger(allBytes, bitConverterSwapBytesToIntegerUtilitiesTerm + 4);
				bitConverterSwapBytesToIntegerUtilitiesTerm += 8;
				int bitConverterAllBytesTerm = BitConverter.ToInt32(allBytes, bitConverterSwapBytesToIntegerUtilitiesTerm);
				Feature feature = null;
				switch (bitConverterAllBytesTerm)
				{
				case 1:
				case 11:
				case 21:
				{
					double x = BitConverter.ToDouble(allBytes, bitConverterSwapBytesToIntegerUtilitiesTerm + 4);
					double y = BitConverter.ToDouble(allBytes, bitConverterSwapBytesToIntegerUtilitiesTerm + 12);
					switch (bitConverterAllBytesTerm)
					{
					case 1:
						z = double.NaN;
						m2 = double.NaN;
						break;
					case 11:
						z = BitConverter.ToDouble(allBytes, bitConverterSwapBytesToIntegerUtilitiesTerm + 20);
						m2 = BitConverter.ToDouble(allBytes, bitConverterSwapBytesToIntegerUtilitiesTerm + 28);
						break;
					case 21:
						z = double.NaN;
						m2 = BitConverter.ToDouble(allBytes, bitConverterSwapBytesToIntegerUtilitiesTerm + 20);
						break;
					}
					if (condition)
					{
						pointMs.Clear();
						pointMs.Add(x, y, z, m2);
						pointMs.Project(projection, SharedData.SRSProjection);
						feature = new Point(pointMs[0]);
					}
					else
					{
						feature = new Point(x, y, z, m2);
					}
					break;
				}
				case 8:
				case 18:
				case 28:
				{
					int count2 = BitConverter.ToInt32(allBytes, bitConverterSwapBytesToIntegerUtilitiesTerm + 36);
					bitConverterSwapBytesToIntegerUtilitiesTerm += 40;
					bool getZ2 = bitConverterAllBytesTerm == 18;
					bool getM2 = (bitConverterAllBytesTerm == 18) | (bitConverterAllBytesTerm == 28);
					ReadFeatures_Points(ref bitConverterSwapBytesToIntegerUtilitiesTerm, allBytes, getZ2, getM2, count2, ref XY, ref Z, ref M, pointMs, condition, projection, SharedData.SRSProjection);
					feature = new MultiPoint(pointMs);
					break;
				}
				case 3:
				case 13:
				case 23:
				{
					int bitConverterAllBytesTerm2 = BitConverter.ToInt32(allBytes, bitConverterSwapBytesToIntegerUtilitiesTerm + 36);
					int count = BitConverter.ToInt32(allBytes, bitConverterSwapBytesToIntegerUtilitiesTerm + 40);
					bitConverterSwapBytesToIntegerUtilitiesTerm += 44;
					int[] intValues = new int[bitConverterAllBytesTerm2 - 1 + 1];
					int count5 = bitConverterAllBytesTerm2 - 1;
					for (int intervalIndex = 0; intervalIndex <= count5; intervalIndex++)
					{
						intValues[intervalIndex] = BitConverter.ToInt32(allBytes, bitConverterSwapBytesToIntegerUtilitiesTerm);
						bitConverterSwapBytesToIntegerUtilitiesTerm += 4;
					}
					bool getZ = bitConverterAllBytesTerm == 13;
					bool getM = (bitConverterAllBytesTerm == 13) | (bitConverterAllBytesTerm == 23);
					ReadFeatures_Points(ref bitConverterSwapBytesToIntegerUtilitiesTerm, allBytes, getZ, getM, count, ref XY, ref Z, ref M, pointMs, condition, projection, SharedData.SRSProjection);
					feature = new Polyline(pointMs, intValues);
					break;
				}
				case 5:
				case 15:
				case 25:
				{
					int bitConverterAllBytesTerm3 = BitConverter.ToInt32(allBytes, bitConverterSwapBytesToIntegerUtilitiesTerm + 36);
					int count3 = BitConverter.ToInt32(allBytes, bitConverterSwapBytesToIntegerUtilitiesTerm + 40);
					bitConverterSwapBytesToIntegerUtilitiesTerm += 44;
					int[] intValuesArray = new int[bitConverterAllBytesTerm3 - 1 + 1];
					int count6 = bitConverterAllBytesTerm3 - 1;
					for (int intervalIndex2 = 0; intervalIndex2 <= count6; intervalIndex2++)
					{
						intValuesArray[intervalIndex2] = BitConverter.ToInt32(allBytes, bitConverterSwapBytesToIntegerUtilitiesTerm);
						bitConverterSwapBytesToIntegerUtilitiesTerm += 4;
					}
					bool condition2 = bitConverterAllBytesTerm == 15;
					bool condition3 = (bitConverterAllBytesTerm == 15) | (bitConverterAllBytesTerm == 25);
					if (!condition2 && !condition3)
					{
						feature = Polygon.FromBuffersMutable(ReadFeatures_PointsXY(ref bitConverterSwapBytesToIntegerUtilitiesTerm, allBytes, condition2, condition3, count3, condition, projection, SharedData.SRSProjection), intValuesArray);
						break;
					}
					ReadFeatures_Points(ref bitConverterSwapBytesToIntegerUtilitiesTerm, allBytes, condition2, condition3, count3, ref XY, ref Z, ref M, pointMs, condition, projection, SharedData.SRSProjection);
					feature = new Polygon(pointMs, intValuesArray);
					break;
				}
				case 31:
				{
					int bitConverterAllBytesTerm4 = BitConverter.ToInt32(allBytes, bitConverterSwapBytesToIntegerUtilitiesTerm + 36);
					int bitConverterAllBytesTerm5 = BitConverter.ToInt32(allBytes, bitConverterSwapBytesToIntegerUtilitiesTerm + 40);
					bitConverterSwapBytesToIntegerUtilitiesTerm += 44;
					int[] intValues2 = new int[bitConverterAllBytesTerm4 - 1 + 1];
					int count7 = bitConverterAllBytesTerm4 - 1;
					for (int j = 0; j <= count7; j++)
					{
						intValues2[j] = BitConverter.ToInt32(allBytes, bitConverterSwapBytesToIntegerUtilitiesTerm);
						bitConverterSwapBytesToIntegerUtilitiesTerm += 4;
					}
					int[] intValues3 = new int[bitConverterAllBytesTerm4 - 1 + 1];
					int count8 = bitConverterAllBytesTerm4 - 2;
					for (int k = 0; k <= count8; k++)
					{
						intValues3[k] = intValues2[k + 1] - intValues2[k];
					}
					intValues3[bitConverterAllBytesTerm4 - 1] = bitConverterAllBytesTerm5 - intValues2[bitConverterAllBytesTerm4 - 1];
					int[] intValues4 = new int[bitConverterAllBytesTerm4 - 1 + 1];
					bool condition4 = false;
					int count9 = bitConverterAllBytesTerm4 - 1;
					for (int l = 0; l <= count9; l++)
					{
						intValues4[l] = BitConverter.ToInt32(allBytes, bitConverterSwapBytesToIntegerUtilitiesTerm);
						if ((intValues4[l] == 0) | (intValues4[l] == 1))
						{
							condition4 = true;
						}
						bitConverterSwapBytesToIntegerUtilitiesTerm += 4;
					}
					ReadFeatures_Points(ref bitConverterSwapBytesToIntegerUtilitiesTerm, allBytes, getZ: true, getM: true, bitConverterAllBytesTerm5, ref XY, ref Z, ref M, pointMs, condition, projection, SharedData.SRSProjection);
					if (condition4)
					{
						int intervalIndex3 = 0;
						int count10 = bitConverterAllBytesTerm4 - 1;
						for (int m = 0; m <= count10; m++)
						{
							if ((intValues4[m] == 0) | (intValues4[m] == 1))
							{
								intervalIndex3 += intValues3[m] - 2;
							}
						}
						TriangleStruct[] triangleStructs = new TriangleStruct[intervalIndex3 - 1 + 1];
						int ordinal2 = 0;
						intervalIndex3 = 0;
						int count11 = bitConverterAllBytesTerm4 - 1;
						for (int n = 0; n <= count11; n++)
						{
							int pt = ordinal2;
							ordinal2 += 2;
							int count12 = intValues3[n] - 1;
							for (int ordinal3 = 2; ordinal3 <= count12; ordinal3++)
							{
								if (intValues4[n] == 0)
								{
									triangleStructs[intervalIndex3].AssignPoints(ordinal2, ordinal2 - 1, ordinal2 - 2);
								}
								else if (intValues4[n] == 1)
								{
									triangleStructs[intervalIndex3].AssignPoints(ordinal2, ordinal2 - 1, pt);
								}
								intervalIndex3++;
								ordinal2++;
							}
						}
						feature = new Tin(pointMs, triangleStructs);
					}
					else
					{
						feature = new Polygon(pointMs, intValues2);
					}
					break;
				}
				}
				ftLayer.AddFeature(feature);
			}
		}
	}

	private static int[] LoadSHXRecs(string shxFilename)
	{
		byte[] bytes = File.ReadAllBytes(shxFilename);
		checked
		{
			int systemBitConverterSwapBytesToIntegerTerm = (int)System.Math.Round((double)(RasMapperLib.Utilities.Math.BitConverterSwapBytesToInteger(bytes, 24) - 50) / 4.0);
			int[] intValues = new int[systemBitConverterSwapBytesToIntegerTerm - 1 + 1];
			int ordinal = 100;
			int count = systemBitConverterSwapBytesToIntegerTerm - 1;
			for (int i = 0; i <= count; i++)
			{
				intValues[i] = RasMapperLib.Utilities.Math.BitConverterSwapBytesToInteger(bytes, ordinal) * 2;
				ordinal += 8;
			}
			return intValues;
		}
	}

	private static void LoadFeaturesNew(string shpFilename, FeatureLayer ftl)
	{
		string shxFilename = MakeShxFilename(shpFilename);
		if (!File.Exists(shpFilename) || !File.Exists(shxFilename))
		{
			throw new FileNotFoundException();
		}
		bool condition = false;
		ESRIProjection eSRIProjection = GetProjection(shpFilename);
		if (eSRIProjection != null && SharedData.SRSProjection != null && !eSRIProjection.IsEqual(SharedData.SRSProjection))
		{
			condition = true;
		}
		if (!condition)
		{
			eSRIProjection = null;
		}
		int[] records = LoadSHXRecs(shxFilename);
		byte[] shpBytes = File.ReadAllBytes(shpFilename);
		if (ftl is PolygonFeatureLayer)
		{
			List<Polygon> loadPolygonFeatures = LoadPolygonFeatures(records, shpBytes, eSRIProjection);
			{
				foreach (Polygon loadPolygonFeature in loadPolygonFeatures)
				{
					ftl.AddFeature(loadPolygonFeature);
				}
				return;
			}
		}
		if (ftl is PolylineFeatureLayer)
		{
			List<Polyline> loadPolylineFeatures = LoadPolylineFeatures(records, shpBytes, eSRIProjection);
			{
				foreach (Polyline loadPolylineFeature in loadPolylineFeatures)
				{
					ftl.AddFeature(loadPolylineFeature);
				}
				return;
			}
		}
		if (ftl is MultiPointFeatureLayer)
		{
			List<MultiPoint> loadMultiPointFeatures = LoadMultiPointFeatures(records, shpBytes, eSRIProjection);
			{
				foreach (MultiPoint loadMultiPointFeature in loadMultiPointFeatures)
				{
					ftl.AddFeature(loadMultiPointFeature);
				}
				return;
			}
		}
		if (ftl is PointFeatureLayer)
		{
			List<Point> loadPointFeatures = LoadPointFeatures(records, shpBytes, eSRIProjection);
			{
				foreach (Point loadPointFeature in loadPointFeatures)
				{
					ftl.AddFeature(loadPointFeature);
				}
				return;
			}
		}
		List<Polygon> loadPolygonFeaturesList = LoadPolygonFeatures(records, shpBytes, eSRIProjection);
		foreach (Polygon loadPolygonFeature2 in loadPolygonFeaturesList)
		{
			ftl.AddFeature(loadPolygonFeature2);
		}
		List<Polyline> loadPolylineFeaturesList = LoadPolylineFeatures(records, shpBytes, eSRIProjection);
		foreach (Polyline loadPolylineFeature2 in loadPolylineFeaturesList)
		{
			ftl.AddFeature(loadPolylineFeature2);
		}
		List<MultiPoint> loadMultiPointFeaturesList = LoadMultiPointFeatures(records, shpBytes, eSRIProjection);
		foreach (MultiPoint loadMultiPointFeature2 in loadMultiPointFeaturesList)
		{
			ftl.AddFeature(loadMultiPointFeature2);
		}
		List<Point> loadPointFeaturesList = LoadPointFeatures(records, shpBytes, eSRIProjection);
		foreach (Point loadPointFeature2 in loadPointFeaturesList)
		{
			ftl.AddFeature(loadPointFeature2);
		}
	}

	private static List<Point> LoadPointFeatures(int[] records, byte[] shpBytes, Projection destProjection = null)
	{
		List<Point> points = new List<Point>();
		PointMs pointMs = new PointMs();
		checked
		{
			int count = records.Count() - 1;
			double z = default;
			double m = default;
			for (int i = 0; i <= count; i++)
			{
				int recordsTerm = records[i];
				RasMapperLib.Utilities.Math.BitConverterSwapBytesToInteger(shpBytes, recordsTerm);
				RasMapperLib.Utilities.Math.BitConverterSwapBytesToInteger(shpBytes, recordsTerm + 4);
				recordsTerm += 8;
				int bitConverterShpBytesTerm = BitConverter.ToInt32(shpBytes, recordsTerm);
				if (bitConverterShpBytesTerm == 1 || bitConverterShpBytesTerm == 11 || bitConverterShpBytesTerm == 21)
				{
					double x = BitConverter.ToDouble(shpBytes, recordsTerm + 4);
					double y = BitConverter.ToDouble(shpBytes, recordsTerm + 12);
					switch (bitConverterShpBytesTerm)
					{
					case 1:
						z = double.NaN;
						m = double.NaN;
						break;
					case 11:
						z = BitConverter.ToDouble(shpBytes, recordsTerm + 20);
						m = BitConverter.ToDouble(shpBytes, recordsTerm + 28);
						break;
					case 21:
						z = double.NaN;
						m = BitConverter.ToDouble(shpBytes, recordsTerm + 20);
						break;
					}
					pointMs.Add(x, y, z, m);
				}
			}
			if (destProjection != null)
			{
				pointMs.Project(destProjection, SharedData.SRSProjection);
			}
			foreach (PointM xComponent in pointMs)
			{
				points.Add(new Point(xComponent));
			}
			return points;
		}
	}

	private static List<Polyline> LoadPolylineFeatures(int[] records, byte[] shpBytes, Projection destProjection = null)
	{
		List<Polyline> polylines = new List<Polyline>();
		checked
		{
			double[,] XY = new double[1000 + 1, 2];
			double[] Z = new double[1000 + 1];
			double[] M = new double[1000 + 1];
			PointMs pointMs = new PointMs();
			bool reproject = destProjection != null;
			int count2 = records.Count() - 1;
			for (int i = 0; i <= count2; i++)
			{
				int recordsTerm = records[i];
				RasMapperLib.Utilities.Math.BitConverterSwapBytesToInteger(shpBytes, recordsTerm);
				RasMapperLib.Utilities.Math.BitConverterSwapBytesToInteger(shpBytes, recordsTerm + 4);
				recordsTerm += 8;
				int bitConverterShpBytesTerm = BitConverter.ToInt32(shpBytes, recordsTerm);
				if (bitConverterShpBytesTerm == 3 || bitConverterShpBytesTerm == 13 || bitConverterShpBytesTerm == 23)
				{
					int bitConverterShpBytesTerm2 = BitConverter.ToInt32(shpBytes, recordsTerm + 36);
					int count = BitConverter.ToInt32(shpBytes, recordsTerm + 40);
					recordsTerm += 44;
					int[] intValues = new int[bitConverterShpBytesTerm2 - 1 + 1];
					int count3 = bitConverterShpBytesTerm2 - 1;
					for (int j = 0; j <= count3; j++)
					{
						intValues[j] = BitConverter.ToInt32(shpBytes, recordsTerm);
						recordsTerm += 4;
					}
					bool getZ = bitConverterShpBytesTerm == 13;
					bool getM = (bitConverterShpBytesTerm == 13) | (bitConverterShpBytesTerm == 23);
					ReadFeatures_Points(ref recordsTerm, shpBytes, getZ, getM, count, ref XY, ref Z, ref M, pointMs, reproject, destProjection, SharedData.SRSProjection);
					polylines.Add(new Polyline(pointMs, intValues));
				}
			}
			return polylines;
		}
	}

	private static List<Polygon> LoadPolygonFeatures(int[] records, byte[] shpBytes, Projection destProjection = null)
	{
		List<Polygon> polygons = new List<Polygon>();
		checked
		{
			double[,] XY = new double[1000 + 1, 2];
			double[] Z = new double[1000 + 1];
			double[] M = new double[1000 + 1];
			PointMs pointMs = new PointMs();
			bool reproject = destProjection != null;
			int count2 = records.Count() - 1;
			for (int i = 0; i <= count2; i++)
			{
				int recordsTerm = records[i];
				RasMapperLib.Utilities.Math.BitConverterSwapBytesToInteger(shpBytes, recordsTerm);
				RasMapperLib.Utilities.Math.BitConverterSwapBytesToInteger(shpBytes, recordsTerm + 4);
				recordsTerm += 8;
				int bitConverterShpBytesTerm = BitConverter.ToInt32(shpBytes, recordsTerm);
				switch (bitConverterShpBytesTerm)
				{
				case 5:
				case 15:
				case 25:
				{
					int bitConverterShpBytesTerm2 = BitConverter.ToInt32(shpBytes, recordsTerm + 36);
					int count = BitConverter.ToInt32(shpBytes, recordsTerm + 40);
					recordsTerm += 44;
					int[] intValues = new int[bitConverterShpBytesTerm2 - 1 + 1];
					int count3 = bitConverterShpBytesTerm2 - 1;
					for (int m = 0; m <= count3; m++)
					{
						intValues[m] = BitConverter.ToInt32(shpBytes, recordsTerm);
						recordsTerm += 4;
					}
					bool condition = bitConverterShpBytesTerm == 15;
					bool condition2 = (bitConverterShpBytesTerm == 15) | (bitConverterShpBytesTerm == 25);
					Polygon polygon = null;
					if (!condition && !condition2)
					{
						polygon = Polygon.FromBuffersMutable(ReadFeatures_PointsXY(ref recordsTerm, shpBytes, condition, condition2, count, reproject, destProjection, SharedData.SRSProjection), intValues);
					}
					else
					{
						ReadFeatures_Points(ref recordsTerm, shpBytes, condition, condition2, count, ref XY, ref Z, ref M, pointMs, reproject, destProjection, SharedData.SRSProjection);
						polygon = new Polygon(pointMs, intValues);
					}
					polygons.Add(polygon);
					break;
				}
				case 31:
				{
					int bitConverterShpBytesTerm3 = BitConverter.ToInt32(shpBytes, recordsTerm + 36);
					int bitConverterShpBytesTerm4 = BitConverter.ToInt32(shpBytes, recordsTerm + 40);
					recordsTerm += 44;
					int[] intValuesArray = new int[bitConverterShpBytesTerm3 - 1 + 1];
					int count4 = bitConverterShpBytesTerm3 - 1;
					for (int j = 0; j <= count4; j++)
					{
						intValuesArray[j] = BitConverter.ToInt32(shpBytes, recordsTerm);
						recordsTerm += 4;
					}
					int[] intValues2 = new int[bitConverterShpBytesTerm3 - 1 + 1];
					int count5 = bitConverterShpBytesTerm3 - 2;
					for (int k = 0; k <= count5; k++)
					{
						intValues2[k] = intValuesArray[k + 1] - intValuesArray[k];
					}
					intValues2[bitConverterShpBytesTerm3 - 1] = bitConverterShpBytesTerm4 - intValuesArray[bitConverterShpBytesTerm3 - 1];
					int[] intValues3 = new int[bitConverterShpBytesTerm3 - 1 + 1];
					bool condition3 = false;
					int count6 = bitConverterShpBytesTerm3 - 1;
					for (int l = 0; l <= count6; l++)
					{
						intValues3[l] = BitConverter.ToInt32(shpBytes, recordsTerm);
						if ((intValues3[l] == 0) | (intValues3[l] == 1))
						{
							condition3 = true;
						}
						recordsTerm += 4;
					}
					ReadFeatures_Points(ref recordsTerm, shpBytes, getZ: true, getM: true, bitConverterShpBytesTerm4, ref XY, ref Z, ref M, pointMs, reproject, destProjection, SharedData.SRSProjection);
					if (!condition3)
					{
						polygons.Add(new Polygon(pointMs, intValuesArray));
					}
					break;
				}
				}
			}
			return polygons;
		}
	}

	private static List<MultiPoint> LoadMultiPointFeatures(int[] records, byte[] shpBytes, Projection destProjection = null)
	{
		List<MultiPoint> multiPoints = new List<MultiPoint>();
		checked
		{
			double[,] XY = new double[1000 + 1, 2];
			double[] Z = new double[1000 + 1];
			double[] M = new double[1000 + 1];
			PointMs pointMs = new PointMs();
			bool reproject = destProjection != null;
			int count2 = records.Count() - 1;
			for (int i = 0; i <= count2; i++)
			{
				int recordsTerm = records[i];
				RasMapperLib.Utilities.Math.BitConverterSwapBytesToInteger(shpBytes, recordsTerm);
				RasMapperLib.Utilities.Math.BitConverterSwapBytesToInteger(shpBytes, recordsTerm + 4);
				recordsTerm += 8;
				int bitConverterShpBytesTerm = BitConverter.ToInt32(shpBytes, recordsTerm);
				if (bitConverterShpBytesTerm == 8 || bitConverterShpBytesTerm == 18 || bitConverterShpBytesTerm == 28)
				{
					int count = BitConverter.ToInt32(shpBytes, recordsTerm + 36);
					recordsTerm += 40;
					bool getZ = bitConverterShpBytesTerm == 18;
					bool getM = (bitConverterShpBytesTerm == 18) | (bitConverterShpBytesTerm == 28);
					ReadFeatures_Points(ref recordsTerm, shpBytes, getZ, getM, count, ref XY, ref Z, ref M, pointMs, reproject, destProjection, SharedData.SRSProjection);
					multiPoints.Add(new MultiPoint(pointMs));
				}
			}
			return multiPoints;
		}
	}

	private static void ReadAttributes(string shpFilename, FeatureLayer ftl, dbfField[] fields)
	{
		string path = MakeDbfFilename(shpFilename);
		DataTable dataTable = ftl.FeatureTable();
		byte[] bytes = File.ReadAllBytes(path);
		int fieldLength = fields.Length;
		checked
		{
			int dbfField2LengthSum = fieldLength * 32 + 32 + 1;
			int[] intValues = new int[fieldLength - 1 + 1];
			int count = fieldLength - 1;
			for (int i = 0; i <= count; i++)
			{
				intValues[i] = dataTable.Columns.IndexOf(fields[i].Name);
				if (intValues[i] > -1 && dataTable.Columns[intValues[i]].ReadOnly)
				{
					intValues[i] = -1;
				}
			}
			int count2 = dataTable.Rows.Count - 1;
			for (int j = 0; j <= count2; j++)
			{
				dbfField2LengthSum++;
				int count3 = fields.Count() - 1;
				for (int k = 0; k <= count3; k++)
				{
					dbfField dbfField2 = fields[k];
					string getString = Encoding.UTF8.GetString(bytes, dbfField2LengthSum, dbfField2.Length);
					if (intValues[k] != -1)
					{
						if ((object)dbfField2.Type == typeof(int))
						{
							dataTable.Rows[j][intValues[k]] = RasMapperLib.Utilities.Math.String2Integer(getString);
						}
						else if ((object)dbfField2.Type == typeof(double))
						{
							double rows = RasMapperLib.Utilities.Math.String2Double(getString);
							if (!double.IsNaN(rows))
							{
								dataTable.Rows[j][intValues[k]] = rows;
							}
						}
						else if ((object)dbfField2.Type == typeof(float))
						{
							double rows2 = RasMapperLib.Utilities.Math.String2Double(getString);
							if (!double.IsNaN(rows2))
							{
								dataTable.Rows[j][intValues[k]] = (float)rows2;
							}
						}
						else
						{
							dataTable.Rows[j][intValues[k]] = getString.Trim();
						}
					}
					dbfField2LengthSum += dbfField2.Length;
				}
			}
		}
	}

	private static void ReadFeatures_Points(ref int rec, byte[] bytes, bool getZ, bool getM, int count, ref double[,] XY, ref double[] Z, ref double[] M, PointMs pmsBuffer, bool reproject, Projection projectFrom, Projection projectTo)
	{
		checked
		{
			if (count > Z.Count())
			{
				XY = new double[count + 1, 2];
				Z = new double[count + 1];
				M = new double[count + 1];
			}
			int scaledCount = count * 8;
			Buffer.BlockCopy(bytes, rec, XY, 0, scaledCount + scaledCount);
			rec += count * 16;
			if (getZ && rec + scaledCount < bytes.Count())
			{
				Buffer.BlockCopy(bytes, rec, Z, 0, scaledCount);
				rec += count * 8;
			}
			else
			{
				int count2 = count - 1;
				for (int i = 0; i <= count2; i++)
				{
					Z[i] = double.NaN;
				}
			}
			if (getM && rec + scaledCount < bytes.Count())
			{
				Buffer.BlockCopy(bytes, rec, M, 0, scaledCount);
				rec += count * 8;
			}
			else
			{
				int count3 = count - 1;
				for (int j = 0; j <= count3; j++)
				{
					M[j] = double.NaN;
				}
			}
			pmsBuffer.Clear();
			pmsBuffer.Capacity = count;
			int count4 = count - 1;
			for (int k = 0; k <= count4; k++)
			{
				pmsBuffer.Add(XY[k, 0], XY[k, 1], Z[k], M[k]);
			}
			if (reproject)
			{
				pmsBuffer.Project(projectFrom, projectTo);
			}
		}
	}

	private static PointMs ReadFeatures_PointsXY(ref int rec, byte[] bytes, bool bufHasZ, bool bufHasM, int count, bool reproject, Projection projectFrom, Projection projectTo)
	{
		PointMs pointMs = new PointMs();
		pointMs.Capacity = count;
		checked
		{
			int scaledCount = System.Math.Min(1048576, count * 16);
			double[,] doubleValues = new double[scaledCount - 1 + 1, 2];
			int systemCountTerm = (int)System.Math.Ceiling((double)count / (double)scaledCount);
			int count2 = systemCountTerm;
			for (int i = 1; i <= count2; i++)
			{
				int scaledCount2 = 0;
				scaledCount2 = ((i != systemCountTerm) ? scaledCount : (scaledCount - (systemCountTerm * scaledCount - count)));
				int ordinal = scaledCount2 * 16;
				Buffer.BlockCopy(bytes, rec, doubleValues, 0, ordinal);
				rec += ordinal;
				int count3 = scaledCount2 - 1;
				for (int j = 0; j <= count3; j++)
				{
					pointMs.Add(doubleValues[j, 0], doubleValues[j, 1], double.NaN, double.NaN);
				}
			}
			if (reproject)
			{
				pointMs.Project(projectFrom, projectTo);
			}
			int scaledCount3 = count * 8;
			if (bufHasZ && rec + scaledCount3 < bytes.Count())
			{
				rec += count * 8 + 16;
			}
			if (bufHasM && rec + scaledCount3 < bytes.Count())
			{
				rec += count * 8 + 16;
			}
			return pointMs;
		}
	}

	public static void Delete(string shpFilename)
	{
		try
		{
			if (File.Exists(shpFilename))
			{
				File.Delete(shpFilename);
			}
			string path = MakeShxFilename(shpFilename);
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			string path2 = MakeDbfFilename(shpFilename);
			if (File.Exists(path2))
			{
				File.Delete(path2);
			}
			string path3 = MakePrjFilename(shpFilename);
			if (File.Exists(path3))
			{
				File.Delete(path3);
			}
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ProjectData.ClearProjectError();
		}
	}

	public static bool CanExportFeatures(FeatureLayer layer)
	{
		bool hasZ = false;
		bool hasM = false;
		if (GetUniqueExportTypes(layer, ref hasZ, ref hasM).IsNullOrEmpty())
		{
			return false;
		}
		return true;
	}

	private static List<string> GetUniqueExportTypes(FeatureLayer ftLayer, ref bool hasZ, ref bool hasM)
	{
		hasZ = false;
		hasM = false;
		HashSet<string> stringSet = new HashSet<string>();
		foreach (Feature feature in ftLayer.Features())
		{
			if (feature != null)
			{
				if (feature is Tin)
				{
					stringSet.Add("MultiPatch");
				}
				else if (feature is Polygon || feature is Extent || feature is MeshFV2D)
				{
					stringSet.Add("Polygon");
				}
				else if (feature is Polyline)
				{
					stringSet.Add("Polyline");
				}
				else if (feature is MultiPoint)
				{
					stringSet.Add("MultiPoint");
				}
				else if (feature is Point)
				{
					stringSet.Add("Point");
				}
				if (feature.HasZ)
				{
					hasZ = true;
				}
				if (feature.HasM)
				{
					hasM = true;
				}
			}
		}
		return stringSet.ToList();
	}

	private static int GetFileShapeType(string exportTypeName, bool hasZ, bool hasM)
	{
		switch (exportTypeName)
		{
		case "Point":
			if (hasZ)
			{
				return 11;
			}
			if (hasM)
			{
				return 21;
			}
			return 1;
		case "MultiPoint":
			if (hasZ)
			{
				return 18;
			}
			if (hasM)
			{
				return 28;
			}
			return 8;
		case "Polyline":
			if (hasZ)
			{
				return 13;
			}
			if (hasM)
			{
				return 23;
			}
			return 3;
		case "Polygon":
			if (hasZ)
			{
				return 15;
			}
			if (hasM)
			{
				return 25;
			}
			return 5;
		case "MultiPatch":
			return 31;
		default:
			return -1;
		}
	}

	public static bool WriteShapefile(Feature feature, string shpFilename, [Optional][DefaultParameterValue("")] ref string summary)
	{
		FeatureLayer featureLayer = null;
		if (feature is Polygon)
		{
			featureLayer = new PolygonFeatureLayer();
		}
		else if (feature is Polyline)
		{
			featureLayer = new PolylineFeatureLayer();
		}
		else if (feature is MultiPoint)
		{
			featureLayer = new MultiPointFeatureLayer();
		}
		else if (feature is Point)
		{
			featureLayer = new PointFeatureLayer();
		}
		if (featureLayer == null)
		{
			return false;
		}
		featureLayer.AddFeature(feature);
		return WriteShapefile(featureLayer, shpFilename, ref summary);
	}

	public static bool WriteShapefile(FeatureLayer featureLayer, string shpFilename, [Optional][DefaultParameterValue("")] ref string summary)
	{
		if (featureLayer.Extent == null)
		{
			featureLayer.ComputeExtent();
			_ = featureLayer.Extent;
		}
		return WriteShapefile(featureLayer, featureLayer.Extent, shpFilename, ref summary);
	}

	private static bool WriteShapefile(FeatureLayer ftLayer, Extent extent, string outputShapeFilename, ref string summary)
	{
		bool hasZ = false;
		bool hasM = false;
		List<string> uniqueExportTypes = GetUniqueExportTypes(ftLayer, ref hasZ, ref hasM);
		checked
		{
			int count = uniqueExportTypes.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				string createIds = ((uniqueExportTypes.Count <= 1) ? outputShapeFilename : (System.IO.Path.GetFileNameWithoutExtension(outputShapeFilename) + "_" + uniqueExportTypes[i] + ".shp"));
				string directoryName = System.IO.Path.GetDirectoryName(createIds);
				if (!Directory.Exists(directoryName))
				{
					Directory.CreateDirectory(directoryName);
				}
				string createIds2 = MakeShxFilename(createIds);
				string dbfFilename = MakeDbfFilename(createIds);
				string prjFilename = MakePrjFilename(createIds);
				BinaryWriter binaryWriter = new BinaryWriter(new FileStream(createIds, FileMode.Create));
				BinaryWriter binaryWriter2 = new BinaryWriter(new FileStream(createIds2, FileMode.Create));
				int fileShapeType = GetFileShapeType(uniqueExportTypes[i], hasZ, hasM);
				byte[] buffer = RasMapperLib.Utilities.Math.BitConverterSwapBytesGetBytes(0);
				double computed = 0.0;
				binaryWriter.Write(RasMapperLib.Utilities.Math.BitConverterSwapBytesGetBytes(9994));
				int unity = 1;
				do
				{
					binaryWriter.Write(buffer);
					unity++;
				}
				while (unity <= 6);
				binaryWriter.Write(BitConverter.GetBytes(1000));
				binaryWriter.Write(BitConverter.GetBytes(fileShapeType));
				binaryWriter.Write(extent.MinX);
				binaryWriter.Write(extent.MinY);
				binaryWriter.Write(extent.MaxX);
				binaryWriter.Write(extent.MaxY);
				if (hasZ)
				{
					binaryWriter.Write(extent.MinZ);
					binaryWriter.Write(extent.MaxZ);
				}
				else
				{
					binaryWriter.Write(computed);
					binaryWriter.Write(computed);
				}
				if (hasM)
				{
					binaryWriter.Write(extent.MinM);
					binaryWriter.Write(extent.MaxM);
				}
				else
				{
					binaryWriter.Write(computed);
					binaryWriter.Write(computed);
				}
				byte[] byteValues = new byte[100];
				binaryWriter2.Write(byteValues);
				bool[] boolValues = new bool[ftLayer.FeatureCount() - 1 + 1];
				int ordinal = 0;
				int count2 = ftLayer.FeatureCount() - 1;
				for (int j = 0; j <= count2; j++)
				{
					Feature feature = ftLayer.Feature(j);
					int position = (int)binaryWriter.BaseStream.Position;
					switch (uniqueExportTypes[i])
					{
					case "Point":
						if (feature is Point)
						{
							Point point = (Point)feature;
							binaryWriter.Write(buffer);
							binaryWriter.Write(buffer);
							binaryWriter.Write(fileShapeType);
							binaryWriter.Write(point.X);
							binaryWriter.Write(point.Y);
							if (fileShapeType == 11)
							{
								binaryWriter.Write(point.Z);
							}
							if (fileShapeType == 11 || fileShapeType == 21)
							{
								binaryWriter.Write(point.M);
							}
						}
						break;
					case "MultiPoint":
					{
						if (!(feature is MultiPoint))
						{
							break;
						}
						binaryWriter.Write(buffer);
						binaryWriter.Write(buffer);
						binaryWriter.Write(fileShapeType);
						binaryWriter.Write(feature.MinX);
						binaryWriter.Write(feature.MinY);
						binaryWriter.Write(feature.MaxX);
						binaryWriter.Write(feature.MaxY);
						MultiPoint multiPoint = (MultiPoint)feature;
						binaryWriter.Write(multiPoint.Count);
						int count3 = multiPoint.Count - 1;
						for (int ordinal2 = 0; ordinal2 <= count3; ordinal2++)
						{
							binaryWriter.Write(multiPoint.get_X(ordinal2));
							binaryWriter.Write(multiPoint.get_Y(ordinal2));
						}
						if (fileShapeType == 18)
						{
							int count4 = multiPoint.Count - 1;
							for (int ordinal3 = 0; ordinal3 <= count4; ordinal3++)
							{
								binaryWriter.Write(multiPoint.get_Z(ordinal3));
							}
						}
						if ((fileShapeType == 18) | (fileShapeType == 28))
						{
							int count5 = multiPoint.Count - 1;
							for (int ordinal4 = 0; ordinal4 <= count5; ordinal4++)
							{
								binaryWriter.Write(multiPoint.get_M(ordinal4));
							}
						}
						break;
					}
					case "Polyline":
					{
						if (!(feature is Polyline))
						{
							break;
						}
						binaryWriter.Write(buffer);
						binaryWriter.Write(buffer);
						binaryWriter.Write(fileShapeType);
						binaryWriter.Write(feature.MinX);
						binaryWriter.Write(feature.MinY);
						binaryWriter.Write(feature.MaxX);
						binaryWriter.Write(feature.MaxY);
						Polyline polyline = (Polyline)feature;
						binaryWriter.Write(polyline.PartsCount());
						binaryWriter.Write(polyline.Count);
						int count6 = polyline.PartsCount() - 1;
						for (int ordinal5 = 0; ordinal5 <= count6; ordinal5++)
						{
							binaryWriter.Write(polyline.PartStartIndex(ordinal5));
						}
						int count7 = polyline.Count - 1;
						for (int ordinal6 = 0; ordinal6 <= count7; ordinal6++)
						{
							binaryWriter.Write(((MultiPoint)polyline).get_X(ordinal6));
							binaryWriter.Write(((MultiPoint)polyline).get_Y(ordinal6));
						}
						if (fileShapeType == 13)
						{
							int count8 = polyline.Count - 1;
							for (int ordinal7 = 0; ordinal7 <= count8; ordinal7++)
							{
								binaryWriter.Write(((MultiPoint)polyline).get_Z(ordinal7));
							}
						}
						if ((fileShapeType == 13) | (fileShapeType == 23))
						{
							int count9 = polyline.Count - 1;
							for (int ordinal8 = 0; ordinal8 <= count9; ordinal8++)
							{
								binaryWriter.Write(((MultiPoint)polyline).get_M(ordinal8));
							}
						}
						break;
					}
					case "Polygon":
						if (feature is Polygon)
						{
							binaryWriter.Write(buffer);
							binaryWriter.Write(buffer);
							binaryWriter.Write(fileShapeType);
							binaryWriter.Write(feature.MinX);
							binaryWriter.Write(feature.MinY);
							binaryWriter.Write(feature.MaxX);
							binaryWriter.Write(feature.MaxY);
							Polygon polygon = (Polygon)feature;
							binaryWriter.Write(polygon.PartsCount());
							binaryWriter.Write(polygon.Count);
							int count10 = polygon.PartsCount() - 1;
							for (int ordinal9 = 0; ordinal9 <= count10; ordinal9++)
							{
								binaryWriter.Write(polygon.PartStartIndex(ordinal9));
							}
							int count11 = polygon.Count - 1;
							for (int ordinal10 = 0; ordinal10 <= count11; ordinal10++)
							{
								binaryWriter.Write(((MultiPoint)polygon).get_X(ordinal10));
								binaryWriter.Write(((MultiPoint)polygon).get_Y(ordinal10));
							}
							if (fileShapeType == 15)
							{
								int count12 = polygon.Count - 1;
								for (int ordinal11 = 0; ordinal11 <= count12; ordinal11++)
								{
									binaryWriter.Write(((MultiPoint)polygon).get_Z(ordinal11));
								}
							}
							if ((fileShapeType == 15) | (fileShapeType == 25))
							{
								int count13 = polygon.Count - 1;
								for (int ordinal12 = 0; ordinal12 <= count13; ordinal12++)
								{
									binaryWriter.Write(((MultiPoint)polygon).get_M(ordinal12));
								}
							}
						}
						else if (feature is Extent)
						{
							binaryWriter.Write(buffer);
							binaryWriter.Write(buffer);
							binaryWriter.Write(fileShapeType);
							binaryWriter.Write(feature.MinX);
							binaryWriter.Write(feature.MinY);
							binaryWriter.Write(feature.MaxX);
							binaryWriter.Write(feature.MaxY);
							binaryWriter.Write(1);
							binaryWriter.Write(5);
							binaryWriter.Write(0);
							Extent extent2 = (Extent)feature;
							binaryWriter.Write(extent2.MinX);
							binaryWriter.Write(extent2.MinY);
							binaryWriter.Write(extent2.MinX);
							binaryWriter.Write(extent2.MaxY);
							binaryWriter.Write(extent2.MaxX);
							binaryWriter.Write(extent2.MaxY);
							binaryWriter.Write(extent2.MaxX);
							binaryWriter.Write(extent2.MinY);
							binaryWriter.Write(extent2.MinX);
							binaryWriter.Write(extent2.MinY);
							if (fileShapeType == 13)
							{
								int ordinal13 = 0;
								do
								{
									binaryWriter.Write(0f);
									ordinal13++;
								}
								while (ordinal13 <= 5);
							}
							if ((fileShapeType == 13) | (fileShapeType == 23))
							{
								int ordinal14 = 0;
								do
								{
									binaryWriter.Write(0f);
									ordinal14++;
								}
								while (ordinal14 <= 5);
							}
						}
						else
						{
							if (!(feature is MeshFV2D))
							{
								break;
							}
							binaryWriter.Write(buffer);
							binaryWriter.Write(buffer);
							binaryWriter.Write(fileShapeType);
							binaryWriter.Write(feature.MinX);
							binaryWriter.Write(feature.MinY);
							binaryWriter.Write(feature.MaxX);
							binaryWriter.Write(feature.MaxY);
							MeshFV2D meshFV2D = feature as MeshFV2D;
							binaryWriter.Write(meshFV2D.NonVirtualCellCount);
							binaryWriter.Write(meshFV2D.NonVirtualCellPolygonTotalPoints());
							int cellPolygonCountSum = 0;
							int count14 = meshFV2D.NonVirtualCellCount - 1;
							for (int ordinal15 = 0; ordinal15 <= count14; ordinal15++)
							{
								binaryWriter.Write(cellPolygonCountSum);
								cellPolygonCountSum += meshFV2D.CellPolygon(ordinal15).Count;
							}
							int count15 = meshFV2D.NonVirtualCellCount - 1;
							for (int ordinal16 = 0; ordinal16 <= count15; ordinal16++)
							{
								Polygon polygon2 = meshFV2D.CellPolygon(ordinal16);
								int count16 = polygon2.Count - 1;
								for (int ordinal17 = 0; ordinal17 <= count16; ordinal17++)
								{
									binaryWriter.Write(((MultiPoint)polygon2).get_X(ordinal17));
									binaryWriter.Write(((MultiPoint)polygon2).get_Y(ordinal17));
								}
							}
							if (fileShapeType == 13)
							{
								int count17 = meshFV2D.NonVirtualCellCount - 1;
								for (int ordinal18 = 0; ordinal18 <= count17; ordinal18++)
								{
									Polygon polygon3 = meshFV2D.CellPolygon(ordinal18);
									int count18 = polygon3.Count - 1;
									for (int ordinal19 = 0; ordinal19 <= count18; ordinal19++)
									{
										binaryWriter.Write(((MultiPoint)polygon3).get_Z(ordinal19));
									}
								}
							}
							if (!((fileShapeType == 13) | (fileShapeType == 23)))
							{
								break;
							}
							int count19 = meshFV2D.NonVirtualCellCount - 1;
							for (int ordinal20 = 0; ordinal20 <= count19; ordinal20++)
							{
								Polygon polygon4 = meshFV2D.CellPolygon(ordinal20);
								int count20 = polygon4.Count - 1;
								for (int ordinal21 = 0; ordinal21 <= count20; ordinal21++)
								{
									binaryWriter.Write(((MultiPoint)polygon4).get_M(ordinal21));
								}
							}
						}
						break;
					case "MultiPatch":
						if (feature is Tin)
						{
							binaryWriter.Write(buffer);
							binaryWriter.Write(buffer);
							binaryWriter.Write(fileShapeType);
							binaryWriter.Write(feature.MinX);
							binaryWriter.Write(feature.MinY);
							binaryWriter.Write(feature.MaxX);
							binaryWriter.Write(feature.MaxY);
							Tin tin = (Tin)feature;
							binaryWriter.Write(tin.TriangleCount());
							binaryWriter.Write(tin.TriangleCount() * 3);
							int count21 = tin.TriangleCount() - 1;
							for (int k = 0; k <= count21; k++)
							{
								int ordinal22 = k * 3;
								binaryWriter.Write(ordinal22);
							}
							int count22 = tin.TriangleCount() - 1;
							for (int l = 0; l <= count22; l++)
							{
								binaryWriter.Write(buffer);
							}
							int count23 = tin.TriangleCount() - 1;
							for (int m = 0; m <= count23; m++)
							{
								TriangleStruct triangleStruct = tin.Triangle(m);
								binaryWriter.Write(((MultiPoint)tin).get_X(triangleStruct.p0));
								binaryWriter.Write(((MultiPoint)tin).get_Y(triangleStruct.p0));
								binaryWriter.Write(((MultiPoint)tin).get_X(triangleStruct.p1));
								binaryWriter.Write(((MultiPoint)tin).get_Y(triangleStruct.p1));
								binaryWriter.Write(((MultiPoint)tin).get_X(triangleStruct.p2));
								binaryWriter.Write(((MultiPoint)tin).get_Y(triangleStruct.p2));
							}
							binaryWriter.Write(feature.MinZ);
							binaryWriter.Write(feature.MaxZ);
							int count24 = tin.TriangleCount() - 1;
							for (int n = 0; n <= count24; n++)
							{
								TriangleStruct triangleStruct = tin.Triangle(n);
								binaryWriter.Write(((MultiPoint)tin).get_Z(triangleStruct.p0));
								binaryWriter.Write(((MultiPoint)tin).get_Z(triangleStruct.p1));
								binaryWriter.Write(((MultiPoint)tin).get_Z(triangleStruct.p2));
							}
							binaryWriter.Write(feature.MinM);
							binaryWriter.Write(feature.MaxM);
							int count25 = tin.TriangleCount() - 1;
							for (int ordinal23 = 0; ordinal23 <= count25; ordinal23++)
							{
								TriangleStruct triangleStruct = tin.Triangle(ordinal23);
								binaryWriter.Write(((MultiPoint)tin).get_M(triangleStruct.p0));
								binaryWriter.Write(((MultiPoint)tin).get_M(triangleStruct.p1));
								binaryWriter.Write(((MultiPoint)tin).get_M(triangleStruct.p2));
							}
						}
						break;
					}
					int positionDifference = (int)(binaryWriter.BaseStream.Position - position);
					if (positionDifference != 0)
					{
						positionDifference -= 8;
						unchecked
						{
							positionDifference /= 2;
							boolValues[j] = true;
							binaryWriter2.Write(RasMapperLib.Utilities.Math.BitConverterSwapBytesGetBytes(position / 2));
							binaryWriter2.Write(RasMapperLib.Utilities.Math.BitConverterSwapBytesGetBytes(positionDifference));
						}
						ordinal++;
						binaryWriter.BaseStream.Position = position;
						binaryWriter.Write(RasMapperLib.Utilities.Math.BitConverterSwapBytesGetBytes(ordinal));
						binaryWriter.Write(RasMapperLib.Utilities.Math.BitConverterSwapBytesGetBytes(positionDifference));
						binaryWriter.BaseStream.Position = binaryWriter.BaseStream.Length;
					}
				}
				binaryWriter.Close();
				binaryWriter2.Close();
				byte[] bytes = BitConverter.GetBytes((int)unchecked(FileSystem.FileLen(createIds) / 2));
				using (FileStream fileStream = new FileStream(createIds, FileMode.Open))
				{
					fileStream.ReadExactly(byteValues, 0, 100);
					byteValues[24] = bytes[3];
					byteValues[25] = bytes[2];
					byteValues[26] = bytes[1];
					byteValues[27] = bytes[0];
					fileStream.Position = 0L;
					fileStream.Write(byteValues, 0, 100);
				}
				byte[] bytes2 = BitConverter.GetBytes((int)unchecked(FileSystem.FileLen(createIds2) / 2));
				using (FileStream fileStream2 = new FileStream(createIds2, FileMode.Open))
				{
					byteValues[24] = bytes2[3];
					byteValues[25] = bytes2[2];
					byteValues[26] = bytes2[1];
					byteValues[27] = bytes2[0];
					fileStream2.Write(byteValues, 0, 100);
				}
				WriteAttributes(ftLayer.FeatureTable(), dbfFilename, boolValues);
				if (File.Exists(SharedData.SRSFilename) && !File.Exists(prjFilename))
				{
					File.Copy(SharedData.SRSFilename, prjFilename);
				}
				try
				{
					DateTime now = DateAndTime.Now;
					File.SetCreationTime(createIds, now);
					File.SetLastWriteTime(createIds, now);
					File.SetCreationTime(createIds2, now);
					File.SetLastWriteTime(createIds2, now);
					File.SetCreationTime(dbfFilename, now);
					File.SetLastWriteTime(dbfFilename, now);
					if (File.Exists(prjFilename))
					{
						File.SetCreationTime(prjFilename, now);
						File.SetLastWriteTime(prjFilename, now);
					}
				}
				catch (Exception ex)
				{
					ProjectData.SetProjectError(ex);
					Exception ex2 = ex;
					ProjectData.ClearProjectError();
				}
			}
			return true;
		}
	}

	private static bool WriteAttributes(DataTable dt, string dbfFilename, bool[] featureExported)
	{
		checked
		{
			bool result;
			try
			{
				using FileStream fileStream = new FileStream(dbfFilename, FileMode.Create);
				byte[] byteValues = new byte[32];
				fileStream.Write(byteValues, 0, 32);
				byteValues[0] = 3;
				byteValues[1] = (byte)(DateAndTime.Now.Year - 1900);
				byteValues[2] = (byte)DateAndTime.Now.Month;
				byteValues[3] = (byte)DateAndTime.Now.Day;
				byteValues[29] = 87;
				byteValues[29] = 57;
				byteValues[29] = 0;
				dbfField[] dbfFields = new dbfField[dt.Columns.Count - 1 + 1];
				short dbfFieldsTerm = 1;
				HashSet<string> stringSet = new HashSet<string>();
				int count = dt.Columns.Count - 1;
				int bufferIndex;
				for (int i = 0; i <= count; i++)
				{
					DataColumn dataColumn = dt.Columns[i];
					byte[] byteValuesArray = new byte[32];
					string columnName = dataColumn.ColumnName;
					columnName = columnName.Replace(".", "");
					columnName = columnName.Replace("(", "");
					columnName = columnName.Replace(")", "");
					columnName = columnName.Replace("*", "");
					if (columnName.Length > 10)
					{
						columnName = Strings.Left(columnName, 10);
					}
					columnName = columnName.Trim();
					int ordinal = 0;
					while (true)
					{
						if (stringSet.Contains(columnName))
						{
							ordinal++;
							if (ordinal <= 9)
							{
								columnName = Strings.Left(columnName, 9);
								columnName += ordinal;
								continue;
							}
							if (ordinal > 99)
							{
								break;
							}
							columnName = Strings.Left(columnName, 8);
							columnName += ordinal;
							continue;
						}
						stringSet.Add(columnName);
						bufferIndex = 0;
						RasMapperLib.Utilities.Math.AddBytes(ref bufferIndex, byteValuesArray, Encoding.UTF8.GetBytes(columnName));
						dbfFields[i].Type = dataColumn.DataType;
						if (dataColumn.ReadOnly)
						{
							dbfFields[i].WriteField = false;
						}
						else if ((object)dataColumn.DataType == typeof(int))
						{
							dbfFields[i].TypeID = "N";
							dbfFields[i].Length = 11;
							dbfFields[i].NumDecimal = 0;
							dbfFields[i].WriteField = true;
						}
						else if ((object)dataColumn.DataType == typeof(double) || (object)dataColumn.DataType == typeof(float))
						{
							dbfFields[i].TypeID = "F";
							dbfFields[i].Length = 19;
							dbfFields[i].NumDecimal = 11;
							dbfFields[i].WriteField = true;
						}
						else if ((object)dataColumn.DataType == typeof(string))
						{
							dbfFields[i].TypeID = "C";
							dbfFields[i].Length = 0;
							foreach (DataRow row in dt.Rows)
							{
								if (!DBNull.Value.Equals(RuntimeHelpers.GetObjectValue(row[i])))
								{
									string s = Conversions.ToString(row[i]);
									int byteCount = Encoding.UTF8.GetByteCount(s);
									if (byteCount > dbfFields[i].Length)
									{
										dbfFields[i].Length = byteCount;
									}
								}
							}
							dbfFields[i].NumDecimal = 0;
							dbfFields[i].WriteField = dbfFields[i].Length <= 256;
						}
						else
						{
							dbfFields[i].WriteField = false;
						}
						if (dbfFields[i].WriteField)
						{
							byteValuesArray[11] = Encoding.UTF8.GetBytes(dbfFields[i].TypeID)[0];
							byteValuesArray[16] = (byte)dbfFields[i].Length;
							byteValuesArray[17] = (byte)dbfFields[i].NumDecimal;
							dbfFieldsTerm = (short)unchecked(dbfFieldsTerm + checked((short)dbfFields[i].Length));
							fileStream.Write(byteValuesArray, 0, 32);
						}
						break;
					}
				}
				fileStream.WriteByte(13);
				short position = (short)fileStream.Position;
				int ordinal2 = 0;
				bufferIndex = dt.Rows.Count - 1;
				for (int j = 0; j <= bufferIndex; j++)
				{
					if (!featureExported[j])
					{
						continue;
					}
					fileStream.WriteByte(32);
					DataRow dataRow2 = dt.Rows[j];
					int count2 = dt.Columns.Count - 1;
					for (int k = 0; k <= count2; k++)
					{
						if (dbfFields[k].WriteField)
						{
							string s2;
							if (dataRow2[k] is DBNull)
							{
								s2 = "";
							}
							else if (dataRow2[k] is double || dataRow2[k] is float)
							{
								s2 = Strings.Format(RuntimeHelpers.GetObjectValue(dataRow2[k]), "0.00000000000e+000");
							}
							else
							{
								s2 = ((!(dataRow2[k] is int)) ? Conversions.ToString(dataRow2[k]) : dataRow2[k].ToString());
							}
							byte[] bytes = Encoding.UTF8.GetBytes(s2);
							fileStream.Write(bytes, 0, bytes.Count());
							int lengthOffset = bytes.Length + 1;
							int length = dbfFields[k].Length;
							for (int l = lengthOffset; l <= length; l++)
							{
								fileStream.WriteByte(32);
							}
						}
					}
					ordinal2++;
				}
				fileStream.WriteByte(26);
				int bufferIndex2 = 4;
				RasMapperLib.Utilities.Math.AddBytes(ref bufferIndex2, byteValues, BitConverter.GetBytes(ordinal2));
				bufferIndex2 = 8;
				RasMapperLib.Utilities.Math.AddBytes(ref bufferIndex2, byteValues, BitConverter.GetBytes(position));
				bufferIndex2 = 10;
				RasMapperLib.Utilities.Math.AddBytes(ref bufferIndex2, byteValues, BitConverter.GetBytes(dbfFieldsTerm));
				fileStream.Position = 0L;
				fileStream.Write(byteValues, 0, 32);
				fileStream.Close();
				result = true;
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				Interaction.MsgBox("An error writing shapefile attributes file: " + dbfFilename, MsgBoxStyle.Exclamation);
				result = false;
				ProjectData.ClearProjectError();
			}
			return result;
		}
	}

	public static bool CopyPRJ(string prjFilename, string shpFilename)
	{
		if (File.Exists(prjFilename))
		{
			string changeExtension = System.IO.Path.ChangeExtension(shpFilename, ".prj");
			if (File.Exists(changeExtension))
			{
				File.Delete(changeExtension);
			}
			File.Copy(prjFilename, changeExtension);
			return true;
		}
		return false;
	}

	public static bool IsValid(string shpfilename, ref string errmsg)
	{
		if (Operators.CompareString(System.IO.Path.GetExtension(shpfilename).ToLower(), ".shp", TextCompare: false) != 0)
		{
			errmsg = "Filename extension is not 'shp'.";
			return false;
		}
		if (!File.Exists(shpfilename))
		{
			errmsg = "'shp' File does not exist.";
			return false;
		}
		if (!File.Exists(MakeDbfFilename(shpfilename)))
		{
			errmsg = "'dbf' File does not exist.";
			return false;
		}
		if (!File.Exists(MakeShxFilename(shpfilename)))
		{
			errmsg = "'shx' File does not exist.";
			return false;
		}
		errmsg = "";
		return true;
	}

	public static bool IsValidFilename(string fn)
	{
		if (System.IO.Path.GetExtension(fn).RoughlyEquals(".shp"))
		{
			return true;
		}
		return false;
	}

	public static List<string> GetMandatoryAssociatedFilenames(string shpFilename)
	{
		return new List<string>
		{
			System.IO.Path.ChangeExtension(shpFilename, "dbf"),
			System.IO.Path.ChangeExtension(shpFilename, "shx")
		};
	}

	public static List<string> GetAssociatedFilenamesIfExists(string shpfilename)
	{
		List<string> associatedFilenamesIfExists = new List<string>();
		string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(shpfilename);
		if (Directory.Exists(System.IO.Path.GetDirectoryName(shpfilename)))
		{
			string[] files = Directory.GetFiles(System.IO.Path.GetDirectoryName(shpfilename));
			foreach (string textValue in files)
			{
				if (!System.IO.Path.GetExtension(textValue).RoughlyEquals(".shp"))
				{
					string fileNameWithoutExtension2 = System.IO.Path.GetFileNameWithoutExtension(textValue);
					if (fileNameWithoutExtension.RoughlyEquals(fileNameWithoutExtension2))
					{
						associatedFilenamesIfExists.Add(textValue);
					}
				}
			}
		}
		return associatedFilenamesIfExists;
	}

	public static List<string> GetAllFiles(string shpfilename)
	{
		List<string> stringList = new List<string>();
		stringList.Add(shpfilename);
		stringList.AddRange(GetMandatoryAssociatedFilenames(shpfilename));
		stringList.AddRange(GetAssociatedFilenamesIfExists(shpfilename));
		return stringList.Distinct().ToList();
	}
}
