using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using Utility;
using Utility.Progress;

namespace RasMapperLib;

public class ConstantWindLayer : InterpolatedLayer, IWindLayer, IVelocityLayer
{
	private const string ProfileDisplayName = "Constant Value";

	public const string ConstantValueAttribute = "Constant Value";

	public const string ConstantUnitsAttribute = "Constant Units";

	private readonly float _vx;

	private readonly float _vy;

	public float VX => _vx;

	public float VY => _vy;

	public bool CanProvideMinMax { get; }

	public bool CanAnimate { get; }

	public bool IsProfile { get; }

	public bool IsMinProfile { get; }

	public bool IsMaxProfile { get; }

	public bool IsNullProfile { get; }

	public int CurrentProfileIndex
	{
		get
		{
			return 0;
		}
		set
		{
		}
	}

	public string CurrentProfileName => "Constant Value";

	public IList<string> AvailableProfiles => new string[1] { "Constant Value" };

	public override bool CanZoomToLayer { get; }

	public override Extent Extent => Extent.Infinite;

	public InterpolationMode RasterInterpMode { get; set; }

	public Projection OverrideProjection { get; set; }

	public float Ratio { get; set; }

	public ConstantWindLayer()
		: base("Constant Wind Vector")
	{
		_vx = 0f;
		_vy = 0f;
		CanProvideMinMax = false;
		CanAnimate = false;
		IsProfile = true;
		IsMinProfile = false;
		IsMaxProfile = false;
		IsNullProfile = false;
		CanZoomToLayer = false;
		Ratio = 1f;
	}

	public ConstantWindLayer(string lyrName, float vxSpd, float vySpd)
		: base(lyrName)
	{
		_vx = 0f;
		_vy = 0f;
		CanProvideMinMax = false;
		CanAnimate = false;
		IsProfile = true;
		IsMinProfile = false;
		IsMaxProfile = false;
		IsNullProfile = false;
		CanZoomToLayer = false;
		Ratio = 1f;
		_vx = vxSpd;
		_vy = vySpd;
	}

	public ConstantWindLayer(RASResults res)
		: base("Constant Wind Vector", res.SourceFilename)
	{
		_vx = 0f;
		_vy = 0f;
		CanProvideMinMax = false;
		CanAnimate = false;
		IsProfile = true;
		IsMinProfile = false;
		IsMaxProfile = false;
		IsNullProfile = false;
		CanZoomToLayer = false;
		Ratio = 1f;
	}

	public override bool Resample(RasterM raster, ref float[] values)
	{
		if (raster == null)
		{
			return false;
		}
		float v = (float)Math.Sqrt(_vx * _vx + _vy * _vy) * Ratio;
		if (values == null || values.Length != raster.CellCount)
		{
			values = new float[checked(raster.CellCount - 1 + 1)];
		}
		Buffers.ArraySetAllVB(values, v);
		return true;
	}

	public void ResampleVelocities(RasterM raster, ref float[] vx, ref float[] vy, [Optional][DefaultParameterValue(null)] ref float[] velMag)
	{
		checked
		{
			if (raster != null)
			{
				if (vx == null || vx.Length != raster.CellCount)
				{
					vx = new float[raster.CellCount - 1 + 1];
				}
				if (vy == null || vy.Length != raster.CellCount)
				{
					vy = new float[raster.CellCount - 1 + 1];
				}
				Buffers.ArraySetAllVB(vx, _vx);
				Buffers.ArraySetAllVB(vy, _vx);
				float v = (float)Math.Sqrt(_vx * _vx + _vy * _vy) * Ratio;
				Buffers.ArraySetAllVB(velMag, v);
			}
		}
	}

	public bool RasterizeSaveData(string hdfFilename, string windFolder, RASResults res, ProgressReporter prog = null)
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
			if (!h5Writer.ExistsAsGroup(windFolder))
			{
				h5Writer.CreateGroup(windFolder);
			}
			string[] strings = new string[1];
			DateTime minValue = DateTime.MinValue;
			strings[0] = minValue.ToString("ddMMMyyyy HH:mm:ss.fff");
			string[] values = strings;
			string datasetName = windFolder + "/Timestamp";
			h5Writer.WriteArray(datasetName, values);
			string datasetName2 = windFolder + "/VX";
			string datasetName3 = windFolder + "/VY";
			float[,] floatValues = new float[1, 1];
			float[,] floatValues2 = new float[1, 1];
			floatValues[0, 0] = _vx;
			floatValues2[0, 0] = _vy;
			h5Writer.WriteArray(datasetName2, floatValues);
			h5Writer.WriteArray(datasetName3, floatValues2);
			RasterM rasterM = new RasterM(1, 1, Extent.PracticallyInfinite);
			if (!geometry.SampleElementsAndSave(h5Writer, rasterM, null, windFolder, prog))
			{
				result = false;
				goto IL_0130;
			}
			MultiBandInterpolatedLayer.SaveMetadata(h5Writer, windFolder, SharedData.VelocityUnits, "Wind", rasterM, null, prog);
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			prog.ReportMessage("Error processing wind data: " + ex2.Message);
			result = false;
			ProjectData.ClearProjectError();
			goto IL_0130;
		}
		result = true;
		goto IL_0130;
		IL_0130:
		return result;
	}

	bool IMeteorologyLayer.RasterizeSaveData(string hdfFilename,string windFolder,RASResults res,ProgressReporter prog )
	{
		//ILSpy generated this explicit interface implementation from .override directive in RasterizeSaveData
		return this.RasterizeSaveData(hdfFilename, windFolder, res, prog);
	}

	public void TrySetProfile(string profile)
	{
	}

	void IAnimatingLayer.TrySetProfile(string profile)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TrySetProfile
		this.TrySetProfile(profile);
	}

	public void SetToMaxProfile()
	{
	}

	void IAnimatingLayer.SetToMaxProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetToMaxProfile
		this.SetToMaxProfile();
	}

	public void SetToMinProfile()
	{
	}

	void IAnimatingLayer.SetToMinProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetToMinProfile
		this.SetToMinProfile();
	}

	public bool GetWindTimeSeries(PointM pt, ref float[] magnitudes, ref float[] directions, ref DateTime[] times)
	{
		return false;
	}

	bool IWindLayer.GetWindTimeSeries(PointM pt,ref float[] magnitudes,ref float[] directions,ref DateTime[] times)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetWindTimeSeries
		return this.GetWindTimeSeries(pt, ref magnitudes, ref directions, ref times);
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

	public void AlertTimeWindowChanged(DateTime startDt, DateTime endDt)
	{
	}

	void IMeteorologyLayer.AlertTimeWindowChanged(DateTime startDt,DateTime endDt)
	{
		//ILSpy generated this explicit interface implementation from .override directive in AlertTimeWindowChanged
		this.AlertTimeWindowChanged(startDt, endDt);
	}
}
