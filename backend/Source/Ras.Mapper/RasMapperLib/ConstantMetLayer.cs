using System;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using Utility;
using Utility.Progress;

namespace RasMapperLib;

public class ConstantMetLayer : InterpolatedLayer, IMeteorologyLayer
{
	public float Value { get; set; }

	public Projection OverrideProjection { get; set; }

	public InterpolationMode RasterInterpMode { get; set; }

	public float Ratio { get; set; }

	public override Extent Extent => Extent.Infinite;

	public ConstantMetLayer(string layerName, float floatValue)
		: base(layerName)
	{
		Ratio = 1f;
		Value = floatValue;
	}

	public override bool Resample(RasterM raster, ref float[] values)
	{
		if (raster == null)
		{
			return false;
		}
		if (values == null || values.Length != raster.CellCount)
		{
			values = new float[checked(raster.CellCount - 1 + 1)];
		}
		Buffers.ArraySetAllVB(values, Value * Ratio);
		return true;
	}

	public bool RasterizeSaveData(string hdfFilename, string folder, RASResults res, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		RASGeometry geometry = res.Geometry;
		bool result;
		try
		{
			using H5Writer h5Writer = new H5Writer(hdfFilename);
			if (!h5Writer.ExistsAsGroup(folder))
			{
				h5Writer.CreateGroup(folder);
			}
			string[] strings = new string[1];
			DateTime minValue = DateTime.MinValue;
			strings[0] = minValue.ToString("ddMMMyyyy HH:mm:ss.fff");
			string[] values = strings;
			h5Writer.WriteArray(folder + "/Timestamp", values);
			h5Writer.WriteArray<float>(values: new float[1, 1] { { Value * Ratio } }, datasetName: folder + "/Values");
			RasterM rasterM = new RasterM(1, 1, Extent.PracticallyInfinite);
			if (!geometry.SampleElementsAndSave(h5Writer, rasterM, null, folder, prog))
			{
				result = false;
				goto IL_0100;
			}
			MultiBandInterpolatedLayer.SaveMetadata(h5Writer, folder, "Unknown", "Generic Met Data", rasterM, null, prog);
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			prog.ReportMessage("Error processing data: " + ex2.Message);
			result = false;
			ProjectData.ClearProjectError();
			goto IL_0100;
		}
		result = true;
		goto IL_0100;
		IL_0100:
		return result;
	}

	bool IMeteorologyLayer.RasterizeSaveData(string hdfFilename,string folder,RASResults res,ProgressReporter prog )
	{
		//ILSpy generated this explicit interface implementation from .override directive in RasterizeSaveData
		return this.RasterizeSaveData(hdfFilename, folder, res, prog);
	}

	public void AlertTimeWindowChanged(DateTime startDt, DateTime endDt)
	{
	}

	void IMeteorologyLayer.AlertTimeWindowChanged(DateTime startDt,DateTime endDt)
	{
		//ILSpy generated this explicit interface implementation from .override directive in AlertTimeWindowChanged
		this.AlertTimeWindowChanged(startDt, endDt);
	}

	public bool GetTimeSeries(PointM pt, ref float[] values, ref DateTime[] times)
	{
		return false;
	}

	bool IMeteorologyLayer.GetTimeSeries(PointM pt,ref float[] values,ref DateTime[] times)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetTimeSeries
		return this.GetTimeSeries(pt, ref values, ref times);
	}
}
