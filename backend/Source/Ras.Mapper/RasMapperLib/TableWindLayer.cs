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

public class TableWindLayer : InterpolatedLayer, IWindLayer, IVelocityLayer
{
	private const string ProfileDisplayName = "Constant Table";

	private float[] _vx;

	private float[] _vy;

	private RasterM _raster;

	public float[] VX => _vx;

	public float[] VY => _vy;

	public RasterM Raster => _raster;

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

	public string CurrentProfileName => "Constant Table";

	public IList<string> AvailableProfiles => new string[1] { "Constant Table" };

	public override bool CanZoomToLayer { get; }

	public override Extent Extent => Extent.Infinite;

	public Projection OverrideProjection { get; set; }

	public float Ratio { get; set; }

	public InterpolationMode RasterInterpMode { get; set; }

	public TableWindLayer()
		: base("Wind Data")
	{
		CanProvideMinMax = false;
		CanAnimate = false;
		IsProfile = true;
		IsMinProfile = false;
		IsMaxProfile = false;
		IsNullProfile = false;
		CanZoomToLayer = false;
		Ratio = 1f;
		RasterInterpMode = InterpolationMode.Nearest;
	}

	public TableWindLayer(string lyrName, float[] vx, float[] vy)
		: base(lyrName)
	{
		CanProvideMinMax = false;
		CanAnimate = false;
		IsProfile = true;
		IsMinProfile = false;
		IsMaxProfile = false;
		IsNullProfile = false;
		CanZoomToLayer = false;
		Ratio = 1f;
		RasterInterpMode = InterpolationMode.Nearest;
		_vx = vx;
		_vy = vy;
	}

	public TableWindLayer(string name, float[] vx, float[] vy, RasterM nativeRaster)
		: this(name, vx, vy)
	{
		SetNativeRaster(nativeRaster);
	}

	public void SetNativeRaster(RasterM nativeRaster)
	{
		if (nativeRaster == null) throw new ArgumentNullException(nameof(nativeRaster));
		if (_vx == null || _vy == null || _vx.Length != nativeRaster.CellCount || _vy.Length != nativeRaster.CellCount)
			throw new ArgumentException("Wind component arrays must match the native raster.", nameof(nativeRaster));
		_raster = nativeRaster;
	}

	public TableWindLayer(RASResults res)
		: base("Wind Data", res.SourceFilename)
	{
		CanProvideMinMax = false;
		CanAnimate = false;
		IsProfile = true;
		IsMinProfile = false;
		IsMaxProfile = false;
		IsNullProfile = false;
		CanZoomToLayer = false;
		Ratio = 1f;
		RasterInterpMode = InterpolationMode.Nearest;
	}

	public override bool Resample(RasterM raster, ref float[] values)
	{
		if (raster == null || _raster == null || _vx == null || _vy == null)
		{
			return false;
		}
		if (values == null || values.Length != raster.CellCount)
		{
			values = new float[checked(raster.CellCount - 1 + 1)];
		}
		float[] vx = null;
		float[] vy = null;
		ResampleVelocities(raster, ref vx, ref vy, ref values);
		return true;
	}

	public void ResampleVelocities(RasterM raster, ref float[] vx, ref float[] vy, [Optional][DefaultParameterValue(null)] ref float[] velMag)
	{
		if (raster == null || _raster == null)
		{
			return;
		}
		checked
		{
			if (vx == null || vx.Length != raster.CellCount)
			{
				vx = new float[raster.CellCount - 1 + 1];
			}
			if (vy == null || vy.Length != raster.CellCount)
			{
				vy = new float[raster.CellCount - 1 + 1];
			}
			if (velMag == null || velMag.Length != raster.CellCount)
			{
				velMag = new float[raster.CellCount - 1 + 1];
			}
			Buffers.ArraySetAllVB(vx, -9999f);
			Buffers.ArraySetAllVB(vy, -9999f);
			Buffers.ArraySetAllVB(velMag, -9999f);
			int count = raster.CellCount - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = raster.CellCenter(i);
				int col = _raster.Col(pointM.X);
				int row = _raster.Row(pointM.Y);
				if (col >= 0 && col < _raster.Cols && row >= 0 && row < _raster.Rows)
				{
					int cellIndex = _raster.CellIndex(row, col);
					float vx2 = _vx[cellIndex];
					float vy2 = _vy[cellIndex];
					vx[i] = vx2;
					vy[i] = vy2;
					velMag[i] = (float)Math.Sqrt(vx2 * vx2 + vy2 * vy2);
				}
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
			h5Writer.WriteArray(datasetName2, _vx);
			h5Writer.WriteArray(datasetName3, _vy);
			if (!geometry.SampleElementsAndSave(h5Writer, _raster, null, windFolder, prog))
			{
				result = false;
				goto IL_0102;
			}
			MultiBandInterpolatedLayer.SaveMetadata(h5Writer, windFolder, SharedData.VelocityUnits, "Wind", _raster, null, prog);
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			prog.ReportMessage("Error processing wind data: " + ex2.Message);
			result = false;
			ProjectData.ClearProjectError();
			goto IL_0102;
		}
		result = true;
		goto IL_0102;
		IL_0102:
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
