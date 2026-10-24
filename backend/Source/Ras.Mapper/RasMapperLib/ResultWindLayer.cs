using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using Utility;
using Utility.Progress;

namespace RasMapperLib;

public class ResultWindLayer : MultiBandInterpolatedLayer, IWindLayer, IVelocityLayer
{
	private string _windFolder;

	private string _datasetTime;

	private string _datasetVX;

	private string _datasetVY;

	private RasterM _nativeRaster;

	protected DateTime[] _loadedTimes;

	private Extent _projectedLocalExtent;

	private Projection _nativeProjection;

	private Extent _nativeExtent;

	public override Extent Extent
	{
		get
		{
			EnsureMetadataLoaded();
			return _projectedLocalExtent;
		}
	}

	public override Projection NativeProjection
	{
		get
		{
			EnsureMetadataLoaded();
			return _nativeProjection;
		}
	}

	public override Extent NativeExtent
	{
		get
		{
			EnsureMetadataLoaded();
			return _nativeExtent;
		}
	}

	public RASResults Result { get; }

	public Projection OverrideProjection_IPrecip { get; set; }

	public float Ratio
	{
		get
		{
			return 1f;
		}
		set
		{
			throw new NotImplementedException();
		}
	}

	public InterpolationMode RasterInterpMode { get; set; }

	public ResultWindLayer(RASResults res)
		: base("Wind Layer", res.SourceFilename)
	{
		_windFolder = "Event Conditions/Meteorology/Wind";
		_datasetTime = _windFolder + "/Timestamp";
		_datasetVX = _windFolder + "/VX";
		_datasetVY = _windFolder + "/VY";
		RasterInterpMode = InterpolationMode.Nearest;
		Result = res;
	}

	public override List<PlotOption> GetStaticPlotOptions()
	{
		return base.GetStaticPlotOptions();
	}

	public override void EnsureMetadataLoaded()
	{
		if (_loadedBands != null)
		{
			return;
		}
		_loadedBands = new List<string>();
		if (!SourceFileExists)
		{
			ErrorMessage = "Source file does not exist.";
			return;
		}
		checked
		{
			try
			{
				using H5Reader h5Reader = new H5Reader(SourceFilename);
				if (!h5Reader.ExistsAsDataset(_datasetTime))
				{
					ErrorMessage = _datasetTime + "not found.";
					return;
				}
				if (!h5Reader.ExistsAsDataset(_datasetVX))
				{
					ErrorMessage = _datasetVX + "not found.";
					return;
				}
				if (!h5Reader.ExistsAsDataset(_datasetVY))
				{
					ErrorMessage = _datasetVY + "not found.";
					return;
				}
				string tryReadStringAttributeScalar = h5Reader.TryReadStringAttributeScalar(_windFolder, "Projection");
				string.IsNullOrEmpty(tryReadStringAttributeScalar);
				if (!string.IsNullOrEmpty(tryReadStringAttributeScalar))
				{
					_nativeProjection = new WKTProjection(tryReadStringAttributeScalar);
				}
				_nativeRaster = null;
				if (!HDF5Storage.TryReadRaster(h5Reader, _windFolder, ref _nativeRaster))
				{
					ErrorMessage = "Could not find spatial information for embedded Wind layer.";
					return;
				}
				_nativeExtent = _nativeRaster.ComputeExtent();
				if (SharedData.SRSProjection != null && _nativeProjection != null)
				{
					_projectedLocalExtent = Converter.Convert(Projector.GetBoundingBox(Converter.Convert(_nativeRaster.ComputeExtent()), _nativeProjection, SharedData.SRSProjection));
				}
				else
				{
					_projectedLocalExtent = _nativeExtent;
				}
				string[] values = null;
				h5Reader.ReadDataset(_datasetTime, ref values);
				_loadedBands.AddRange(values);
				_loadedTimes = new DateTime[values.Count() - 1 + 1];
				int count = values.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					_loadedTimes[i] = DateTime.Parse(values[i]);
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				_loadedBands = new List<string>();
				ErrorMessage = ex2.Message;
				ProjectData.ClearProjectError();
			}
		}
	}

	public override bool Resample(RasterM raster, ref float[] values)
	{
		EnsureMetadataLoaded();
		bool result;
		try
		{
			float[] vx = null;
			float[] vy = null;
			ResampleVelocities(raster, ref vx, ref vy, ref values);
			result = true;
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			result = false;
			ProjectData.ClearProjectError();
		}
		return result;
	}

	public void ResampleVelocities(RasterM raster, ref float[] vx, ref float[] vy, [Optional][DefaultParameterValue(null)] ref float[] velMag)
	{
		ResampleVelocities(_currentBandIndex, raster, ref vx, ref vy, ref velMag);
	}

	public void ResampleVelocities(int profileIndex, RasterM raster, ref float[] vx, ref float[] vy, [Optional][DefaultParameterValue(null)] ref float[] velMag)
	{
		if (raster == null)
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
			if (profileIndex < 0 || profileIndex >= _loadedBands.Count)
			{
				return;
			}
			float[] values = null;
			float[] values2 = null;
			using (H5Reader h5Reader = new H5Reader(SourceFilename))
			{
				h5Reader.ReadRow(_datasetVX, profileIndex, ref values);
				h5Reader.ReadRow(_datasetVY, profileIndex, ref values2);
			}
			if (SharedData.SRSProjection == null || _nativeProjection == null)
			{
				Converter.Convert(_nativeRaster);
				Converter.Convert(raster);
				throw new NotImplementedException();
			}
			ImageLayer.WarpData(_nativeProjection, _nativeRaster, values, SharedData.SRSProjection, raster, vx);
			ImageLayer.WarpData(_nativeProjection, _nativeRaster, values2, SharedData.SRSProjection, raster, vy);
			int count = vx.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				float vx2 = vx[i];
				float vy2 = vy[i];
				if (vx2 != -9999f && vy2 != -9999f)
				{
					velMag[i] = (float)System.Math.Sqrt(vx2 * vx2 + vy2 * vy2);
				}
			}
		}
	}

	public override List<DateTime> TryGetDatetimes()
	{
		return _loadedTimes.ToList();
	}

	public bool RasterizeSaveData(string hdfFilename, string folder, RASResults res, ProgressReporter prog = null)
	{
		throw new NotImplementedException();
	}

	bool IMeteorologyLayer.RasterizeSaveData(string hdfFilename,string folder,RASResults res,ProgressReporter prog )
	{
		//ILSpy generated this explicit interface implementation from .override directive in RasterizeSaveData
		return this.RasterizeSaveData(hdfFilename, folder, res, prog);
	}

	public void AlertTimeWindowChanged(DateTime startDt, DateTime endDt)
	{
		throw new NotImplementedException();
	}

	void IMeteorologyLayer.AlertTimeWindowChanged(DateTime startDt,DateTime endDt)
	{
		//ILSpy generated this explicit interface implementation from .override directive in AlertTimeWindowChanged
		this.AlertTimeWindowChanged(startDt, endDt);
	}

	private bool IMeteorologyLayer_GetTimeSeries(PointM pt, ref float[] values, ref DateTime[] times)
	{
		float[] directions = null;
		return GetWindTimeSeries(pt, ref values, ref directions, ref times);
	}

	bool IMeteorologyLayer.GetTimeSeries(PointM pt,ref float[] values,ref DateTime[] times)
	{
		//ILSpy generated this explicit interface implementation from .override directive in IMeteorologyLayer_GetTimeSeries
		return this.IMeteorologyLayer_GetTimeSeries(pt, ref values, ref times);
	}

	public bool GetWindTimeSeries(PointM pt, ref float[] magnitudes, ref float[] directions, ref DateTime[] times)
	{
		if (!Extent.Contains(pt))
		{
			return false;
		}
		RasterM raster = new RasterM(pt.Translate(-0.5, -0.5), 1, 1, 1.0);
		times = _loadedTimes.ToArray();
		checked
		{
			magnitudes = new float[times.Count() - 1 + 1];
			directions = new float[times.Count() - 1 + 1];
			float[] vx = null;
			float[] vy = null;
			int count = _loadedBands.Count - 1;
			float mag = default;
			float dir = default;
			for (int i = 0; i <= count; i++)
			{
				int profileIndex = i;
				float[] velMag = null;
				ResampleVelocities(profileIndex, raster, ref vx, ref vy, ref velMag);
				WindLayer.ConvertToMagDir(vx[0], vy[0], ref mag, ref dir);
				magnitudes[i] = mag;
				directions[i] = dir;
			}
			return true;
		}
	}

	bool IWindLayer.GetWindTimeSeries(PointM pt,ref float[] magnitudes,ref float[] directions,ref DateTime[] times)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetWindTimeSeries
		return this.GetWindTimeSeries(pt, ref magnitudes, ref directions, ref times);
	}
}
