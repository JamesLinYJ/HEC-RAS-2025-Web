using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Extensions;

namespace RasMapperLib;

public class NetCDFDescription
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__16_002D0
	{
		public H5Reader h5r;

		public NetCDFDescription netCDFDescription;

		public _Closure_0024__16_002D0(_Closure_0024__16_002D0 other)
		{
			if (other != null)
			{
				h5r = other.h5r;
			}
		}

		[SpecialName]
		internal double[] _Lambda_0024__1(H5Reader h5rRuntime)
		{
			double[] values = null;
			h5r.ReadDataset("/y", ref values);
			return values;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__16_002D1
	{
		public string dset;

		public _Closure_0024__16_002D0 outerClosure;

		public _Closure_0024__16_002D1(_Closure_0024__16_002D1 other)
		{
			if (other != null)
			{
				dset = other.dset;
			}
		}

		[SpecialName]
		internal double[,,] _Lambda_0024__3(H5Reader runtimeH5Reader)
		{
			return outerClosure.netCDFDescription.TryReadAsDouble(runtimeH5Reader, dset);
		}
	}

	public string SourceFilename;

	public double[] TimeInMinutes;

	public DateTime[] DateTimes;

	public string Dimensions;

	public string DataType;

	public Func<H5Reader, double[]> GetXLocations;

	public Func<H5Reader, double[]> GetYLocations;

	public Func<H5Reader, double[,]> GetZLocations;

	public Func<H5Reader, double[,,]> GetRawDataAsDouble;

	public double NoData;

	public double Offset;

	public double ScaleFactor;

	public List<double[,]> TransformedData;

	public string WKT;

	public Dictionary<string, Dictionary<string, object>> Attributes;

	public NetCDFDescription(string filename)
	{
		SourceFilename = filename;
		LoadFileInfo();
	}

	private void LoadFileInfo()
	{
		if (string.IsNullOrEmpty(SourceFilename) || !File.Exists(SourceFilename))
		{
			return;
		}
		checked
		{
			try
			{
				_Closure_0024__16_002D0 capturedLocals = default;
				_Closure_0024__16_002D0 capturedLocals2 = new _Closure_0024__16_002D0(capturedLocals);
				capturedLocals2.netCDFDescription = this;
				capturedLocals2.h5r = new H5Reader(SourceFilename);
				try
				{
					List<string> datasetNames = capturedLocals2.h5r.GetDatasetNames("/");
					if (!datasetNames.Contains("x") || !datasetNames.Contains("y") || !datasetNames.Contains("z") || !datasetNames.Contains("time"))
					{
						return;
					}
					GetXLocations = [SpecialName] (H5Reader h5rRuntime) =>
					{
						double[] values = null;
						h5rRuntime.ReadDataset("/x", ref values);
						return values;
					};
					GetYLocations = [SpecialName] (H5Reader h5rRuntime) =>
					{
						double[] values = null;
						capturedLocals2.h5r.ReadDataset("/y", ref values);
						return values;
					};
					GetZLocations = [SpecialName] (H5Reader h5rRuntime) =>
					{
						double[,] values = null;
						h5rRuntime.ReadDataset("/z", ref values);
						return values;
					};
					capturedLocals2.h5r.ReadDataset("/time", ref TimeInMinutes);
					DateTimes = new DateTime[TimeInMinutes.Length - 1 + 1];
					DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0);
					int count = TimeInMinutes.Length - 1;
					for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
					{
						DateTimes[intervalIndex] = dateTime.AddMinutes(TimeInMinutes[intervalIndex]);
					}
					Attributes = new Dictionary<string, Dictionary<string, object>>();
					Attributes.Add("X", capturedLocals2.h5r.ReadAllAttributes("/x"));
					Attributes.Add("Y", capturedLocals2.h5r.ReadAllAttributes("/y"));
					Attributes.Add("Z", capturedLocals2.h5r.ReadAllAttributes("/z"));
					Attributes.Add("Time", capturedLocals2.h5r.ReadAllAttributes("/time"));
					using (List<string>.Enumerator datasetNamesList = capturedLocals2.h5r.GetDatasetNames("/").GetEnumerator())
					{
						_Closure_0024__16_002D1 capturedLocals3 = default;
						while (datasetNamesList.MoveNext())
						{
							capturedLocals3 = new _Closure_0024__16_002D1(capturedLocals3);
							capturedLocals3.outerClosure = capturedLocals2;
							capturedLocals3.dset = datasetNamesList.Current;
							if (capturedLocals3.outerClosure.h5r.GetDatasetNDims(capturedLocals3.dset) != 3)
							{
								continue;
							}
							long[] datasetDimensions = capturedLocals3.outerClosure.h5r.GetDatasetDimensions(capturedLocals3.dset);
							Dimensions = "";
							int count2 = datasetDimensions.Length - 1;
							for (int intervalIndex2 = 0; intervalIndex2 <= count2; intervalIndex2++)
							{
								if (intervalIndex2 == datasetDimensions.Length - 1)
								{
									Dimensions += datasetDimensions[intervalIndex2];
									continue;
								}
								ref string dimensions = ref Dimensions;
								dimensions = dimensions + datasetDimensions[intervalIndex2] + " x ";
							}
							TryParseMetadata(capturedLocals3.outerClosure.h5r, capturedLocals3.dset, ref Offset, ref ScaleFactor, ref NoData);
							GetRawDataAsDouble = capturedLocals3._Lambda_0024__3;
							Attributes.Add(capturedLocals3.dset, capturedLocals3.outerClosure.h5r.ReadAllAttributes(capturedLocals3.dset));
							Type datasetType = capturedLocals3.outerClosure.h5r.GetDatasetType(capturedLocals3.dset);
							if (datasetType == typeof(float))
							{
								DataType = "Single";
							}
							else if (datasetType == typeof(byte))
							{
								DataType = "Byte";
							}
							else if (datasetType == typeof(short))
							{
								DataType = "Short";
							}
							else if (datasetType == typeof(int))
							{
								DataType = "Integer";
							}
							else if (datasetType == typeof(long))
							{
								DataType = "Long";
							}
							else if (datasetType == typeof(double))
							{
								DataType = "Double";
							}
							else
							{
								DataType = "Unknown";
							}
							break;
						}
					}
					if (datasetNames.Contains("crs"))
					{
						Dictionary<string, object> allAttributes = capturedLocals2.h5r.ReadAllAttributes("/crs");
						Attributes.Add("Spatial Reference System", allAttributes);
						object localValue = null;
						allAttributes.TryGetValue("crs_wkt", out localValue);
						WKT = localValue?.ToString();
					}
				}
				finally
				{
					if (capturedLocals2.h5r != null)
					{
						((IDisposable)capturedLocals2.h5r).Dispose();
					}
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

	private void TryParseMetadata(H5Reader h5r, string dset, ref double offset, ref double scaleFactor, ref double noData)
	{
		offset = double.NaN;
		scaleFactor = double.NaN;
		noData = double.NaN;
		h5r.GetDatasetDimensions(dset);
		Dictionary<string, object> allAttributes = h5r.ReadAllAttributes(dset);
		foreach (KeyValuePair<string, object> allAttribute in allAttributes)
		{
			try
			{
				if (allAttribute.Key.RoughlyEquals("add_offset"))
				{
					object objectValue = RuntimeHelpers.GetObjectValue(allAttribute.Value);
					if (objectValue is Array)
					{
						objectValue = RuntimeHelpers.GetObjectValue((objectValue as Array).GetValue(0));
					}
					if (objectValue is string)
					{
						continue;
					}
					offset = Conversions.ToDouble(objectValue);
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				offset = double.NaN;
				ProjectData.ClearProjectError();
			}
			try
			{
				if (allAttribute.Key.RoughlyEquals("_FillValue"))
				{
					object objectValue2 = RuntimeHelpers.GetObjectValue(allAttribute.Value);
					if (objectValue2 is Array)
					{
						objectValue2 = RuntimeHelpers.GetObjectValue((objectValue2 as Array).GetValue(0));
					}
					if (objectValue2 is string)
					{
						continue;
					}
					noData = Conversions.ToDouble(objectValue2);
				}
			}
			catch (Exception ex3)
			{
				ProjectData.SetProjectError(ex3);
				Exception ex4 = ex3;
				noData = double.NaN;
				ProjectData.ClearProjectError();
			}
			try
			{
				if (allAttribute.Key.RoughlyEquals("scale_factor"))
				{
					object objectValue3 = RuntimeHelpers.GetObjectValue(allAttribute.Value);
					if (objectValue3 is Array)
					{
						objectValue3 = RuntimeHelpers.GetObjectValue((objectValue3 as Array).GetValue(0));
					}
					if (!(objectValue3 is string))
					{
						scaleFactor = Conversions.ToDouble(objectValue3);
					}
				}
			}
			catch (Exception ex5)
			{
				ProjectData.SetProjectError(ex5);
				Exception ex6 = ex5;
				scaleFactor = double.NaN;
				ProjectData.ClearProjectError();
			}
		}
	}

	private double[,,] TryReadAsDouble(H5Reader h5r, string dset)
	{
		long[] datasetDimensions = h5r.GetDatasetDimensions(dset);
		if (datasetDimensions.Length != 3)
		{
			return null;
		}
		Dictionary<string, object> allAttributes = h5r.ReadAllAttributes(dset);
		foreach (KeyValuePair<string, object> allAttribute in allAttributes)
		{
			try
			{
				if (allAttribute.Key.RoughlyEquals("add_offset"))
				{
					Offset = Conversions.ToDouble(allAttribute.Value);
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				ProjectData.ClearProjectError();
			}
			try
			{
				if (allAttribute.Key.RoughlyEquals("_FillValue"))
				{
					NoData = Conversions.ToDouble(allAttribute.Value);
				}
			}
			catch (Exception ex3)
			{
				ProjectData.SetProjectError(ex3);
				Exception ex4 = ex3;
				ProjectData.ClearProjectError();
			}
			try
			{
				if (allAttribute.Key.RoughlyEquals("scale_factor"))
				{
					ScaleFactor = Conversions.ToDouble(allAttribute.Value);
				}
			}
			catch (Exception ex5)
			{
				ProjectData.SetProjectError(ex5);
				Exception ex6 = ex5;
				ProjectData.ClearProjectError();
			}
		}
		checked
		{
			double[,,] values = new double[(int)(datasetDimensions[0] - 1) + 1, (int)(datasetDimensions[1] - 1) + 1, (int)(datasetDimensions[2] - 1) + 1];
			Type datasetType = h5r.GetDatasetType(dset);
			if (datasetType == typeof(float))
			{
				DataType = "Single";
				float[,,] values2 = null;
				h5r.ReadDataset(dset, ref values2);
				int count = values2.GetLength(0) - 1;
				for (int i = 0; i <= count; i++)
				{
					int count2 = values2.GetLength(1) - 1;
					for (int j = 0; j <= count2; j++)
					{
						int count3 = values2.GetLength(2) - 1;
						for (int k = 0; k <= count3; k++)
						{
							values[i, j, k] = values2[i, j, k];
						}
					}
				}
			}
			else if (datasetType == typeof(byte))
			{
				DataType = "Byte";
				byte[,,] values3 = null;
				h5r.ReadDataset(dset, ref values3);
				int count4 = values3.GetLength(0) - 1;
				for (int l = 0; l <= count4; l++)
				{
					int count5 = values3.GetLength(1) - 1;
					for (int m = 0; m <= count5; m++)
					{
						int count6 = values3.GetLength(2) - 1;
						for (int n = 0; n <= count6; n++)
						{
							values[l, m, n] = unchecked((int)values3[l, m, n]);
						}
					}
				}
			}
			else if (datasetType == typeof(short))
			{
				DataType = "Short";
				short[,,] values4 = null;
				h5r.ReadDataset(dset, ref values4);
				int count7 = values4.GetLength(0) - 1;
				for (int intervalIndex = 0; intervalIndex <= count7; intervalIndex++)
				{
					int count8 = values4.GetLength(1) - 1;
					for (int intervalIndex2 = 0; intervalIndex2 <= count8; intervalIndex2++)
					{
						int count9 = values4.GetLength(2) - 1;
						for (int intervalIndex3 = 0; intervalIndex3 <= count9; intervalIndex3++)
						{
							values[intervalIndex, intervalIndex2, intervalIndex3] = values4[intervalIndex, intervalIndex2, intervalIndex3];
						}
					}
				}
			}
			else if (datasetType == typeof(int))
			{
				DataType = "Integer";
				int[,,] values5 = null;
				h5r.ReadDataset(dset, ref values5);
				int count10 = values5.GetLength(0) - 1;
				for (int intervalIndex4 = 0; intervalIndex4 <= count10; intervalIndex4++)
				{
					int count11 = values5.GetLength(1) - 1;
					for (int intervalIndex5 = 0; intervalIndex5 <= count11; intervalIndex5++)
					{
						int count12 = values5.GetLength(2) - 1;
						for (int intervalIndex6 = 0; intervalIndex6 <= count12; intervalIndex6++)
						{
							values[intervalIndex4, intervalIndex5, intervalIndex6] = values5[intervalIndex4, intervalIndex5, intervalIndex6];
						}
					}
				}
			}
			else if (datasetType == typeof(long))
			{
				DataType = "Long";
				long[,,] values6 = null;
				h5r.ReadDataset(dset, ref values6);
				int count13 = values6.GetLength(0) - 1;
				for (int intervalIndex7 = 0; intervalIndex7 <= count13; intervalIndex7++)
				{
					int count14 = values6.GetLength(1) - 1;
					for (int intervalIndex8 = 0; intervalIndex8 <= count14; intervalIndex8++)
					{
						int count15 = values6.GetLength(2) - 1;
						for (int intervalIndex9 = 0; intervalIndex9 <= count15; intervalIndex9++)
						{
							values[intervalIndex7, intervalIndex8, intervalIndex9] = values6[intervalIndex7, intervalIndex8, intervalIndex9];
						}
					}
				}
			}
			else if (datasetType == typeof(double))
			{
				DataType = "Double";
				h5r.ReadDataset(dset, ref values);
			}
			else
			{
				DataType = "Unknown";
			}
			return values;
		}
	}

	public double[,] GetTransformedData(H5Reader h5r, int pfIdx)
	{
		double[,,] rawDataAsDouble = GetRawDataAsDouble(h5r);
		checked
		{
			double[,] transformedData = new double[rawDataAsDouble.GetLength(1) - 1 + 1, rawDataAsDouble.GetLength(2) - 1 + 1];
			int count = rawDataAsDouble.GetLength(1) - 1;
			for (int i = 0; i <= count; i++)
			{
				int count2 = rawDataAsDouble.GetLength(2) - 1;
				for (int j = 0; j <= count2; j++)
				{
					transformedData[i, j] = rawDataAsDouble[pfIdx, i, j];
				}
			}
			bool notIsNaN = !double.IsNaN(NoData);
			bool notIsNaN2 = !double.IsNaN(Offset);
			bool notIsNaN3 = !double.IsNaN(ScaleFactor);
			if (double.IsNaN(NoData) && double.IsNaN(Offset) && double.IsNaN(ScaleFactor))
			{
				return transformedData;
			}
			int count3 = rawDataAsDouble.GetLength(1) - 1;
			for (int k = 0; k <= count3; k++)
			{
				int count4 = rawDataAsDouble.GetLength(2) - 1;
				for (int l = 0; l <= count4; l++)
				{
					double offsetSum = transformedData[k, l];
					if (!notIsNaN || offsetSum != NoData)
					{
						if (notIsNaN2)
						{
							offsetSum += Offset;
						}
						if (notIsNaN3)
						{
							offsetSum *= ScaleFactor;
						}
						transformedData[k, l] = offsetSum;
					}
				}
			}
			return transformedData;
		}
	}
}
