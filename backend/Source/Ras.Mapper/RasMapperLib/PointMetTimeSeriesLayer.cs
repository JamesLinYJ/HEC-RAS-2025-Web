using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Geospatial.GDALAssist;
using Geospatial.PairedData;
using Geospatial.Rasterizers;
using Geospatial.Rasters;
using Geospatial.Vectors;
using H5Assist.VB;
using RasMapperLib.Utilities;
using Utility.Progress;

namespace RasMapperLib;

public class PointMetTimeSeriesLayer : InterpolatedLayer, IMeteorologyLayer, IAnimatingLayer
{
	[Serializable]
	[CompilerGenerated]
private sealed class _Closure_0024__
	{
		public static readonly _Closure_0024__ _0024I;

		public static Func<float, double> _0024I52_002D0;

		static _Closure_0024__()
		{
			_0024I = new _Closure_0024__();
		}

		[SpecialName]
		internal double _Lambda_0024__52_002D0(float s)
		{
			return s;
		}
	}

	private int _pfIdx;

	private readonly List<string> _pfNames = new List<string>();

	public Projection OverrideProjection { get; set; }

	public InterpolationMode RasterInterpMode { get; set; }

	public override bool TreatZeroAsNoData { get; set; }

	public float Ratio { get; set; }

	public bool CanProvideMinMax { get; }

	public bool CanAnimate { get; }

	public bool IsProfile
	{
		get
		{
			if (_pfIdx >= 0)
			{
				return _pfIdx < ProfileCount;
			}
			return false;
		}
	}

	public bool IsMinProfile { get; }

	public bool IsMaxProfile { get; }

	public bool IsNullProfile => !IsProfile;

	public int CurrentProfileIndex
	{
		get
		{
			return _pfIdx;
		}
		set
		{
			_pfIdx = value;
		}
	}

	public string CurrentProfileName
	{
		get
		{
			if (IsNullProfile)
			{
				return "None";
			}
			return AvailableProfiles[CurrentProfileIndex];
		}
	}

	public IList<string> AvailableProfiles => _pfNames;

	public int ProfileCount => AvailableProfiles.Count;

	public override Extent Extent => _extent;

	public PointMetTimeSeriesLayer()
	{
		TreatZeroAsNoData = true;
		Ratio = 1f;
		CanProvideMinMax = false;
		CanAnimate = true;
		_pfIdx = 0;
		IsMinProfile = false;
		IsMaxProfile = false;
	}

	public override bool Resample(RasterM raster, ref float[] values)
	{
		if (IsNullProfile)
		{
			return false;
		}
		new ResampleArgs(Converter.Convert(raster));
		return false;
	}

	public void AlertTimeWindowChanged(DateTime startDt, DateTime endDt)
	{
	}

	void IMeteorologyLayer.AlertTimeWindowChanged(DateTime startDt,DateTime endDt)
	{
		//ILSpy generated this explicit interface implementation from .override directive in AlertTimeWindowChanged
		this.AlertTimeWindowChanged(startDt, endDt);
	}

	private static List<DateTime> SanitizeDateTimes(List<DateTime> dts, RASResults res)
	{
		if (dts.Count == 2 && DateTime.Compare(dts[0], DateTime.MinValue) == 0 && DateTime.Compare(dts[1], DateTime.MaxValue) == 0)
		{
			return new List<DateTime>
			{
				res.PlanAttributes.StartTime,
				res.PlanAttributes.EndTime
			};
		}
		return dts;
	}

	public bool RasterizeSaveData(string hdfFilename, string folder, RASResults res, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		_ = Name;
		prog.ReportMessage("Code Removed");
		return false;
	}

	bool IMeteorologyLayer.RasterizeSaveData(string hdfFilename,string folder,RASResults res,ProgressReporter prog )
	{
		//ILSpy generated this explicit interface implementation from .override directive in RasterizeSaveData
		return this.RasterizeSaveData(hdfFilename, folder, res, prog);
	}

	public bool GetTimeSeries(PointM pt, ref float[] values, ref DateTime[] times)
	{
		Converter.Convert(pt);
		return false;
	}

	bool IMeteorologyLayer.GetTimeSeries(PointM pt,ref float[] values,ref DateTime[] times)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetTimeSeries
		return this.GetTimeSeries(pt, ref values, ref times);
	}

	public bool ReadNativeRaster(int profileIndex, ref float[] values)
	{
		return false;
	}

	public static bool TryParse(H5Reader h5r, RASResults res, MeteorologicalStationLayer metLayer, string metTypeName, float ratio, ref PointMetTimeSeriesLayer lyr, ProgressReporter prog)
	{
		if (SharedData.DebugMode)
		{
			prog.ReportMessage("(DEBUG) Gauged " + metTypeName + " Wind data detected");
		}
		DateTime dtOffset = DateTime.MinValue;
		if (res != null)
		{
			dtOffset = res.PlanAttributes.StartTime;
		}
		if (metLayer == null)
		{
			prog.ReportError("Error loading Meteorological Stations");
			return false;
		}
		if (metLayer.FeatureCount() == 0)
		{
			prog.ReportError("Error: Gauged " + metTypeName + " selected, but no gauges have been added.");
			return false;
		}
		List<string> metDatasetNames = metLayer.GetMetDatasetNames(metTypeName);
		List<string> stringList = new List<string>();
		List<PointM> pointMs = new List<PointM>();
		List<string> stringListList = new List<string>();
		checked
		{
			int count = metLayer.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				string featureName = metLayer.GetFeatureName(i);
				string metDatasetName = metDatasetNames[i];
				if (h5r.ExistsAsDataset(metDatasetName))
				{
					stringList.Add(metDatasetName);
					pointMs.Add(metLayer.Point(i).PointM());
					stringListList.Add(featureName);
				}
			}
			List<MetTimeSeries> tryParsePointTimeSeries = MetParser.TryParsePointTimeSeries(h5r, stringList, prog);
			if (tryParsePointTimeSeries == null)
			{
				prog.ReportError("Error loading " + metTypeName + " time-series data.");
				return false;
			}
			List<TimeSeriesGauge> timeSeriesGauges = new List<TimeSeriesGauge>();
			List<IDiscreteTimeSeries> iDiscreteTimeSeries = new List<IDiscreteTimeSeries>();
			int count2 = tryParsePointTimeSeries.Count - 1;
			for (int j = 0; j <= count2; j++)
			{
				MetTimeSeries metTimeSeries = tryParsePointTimeSeries[j];
				if (metTimeSeries == null)
				{
					prog.ReportError("Could not load data for gauge " + stringListList[j]);
					return false;
				}
				List<float> values = null;
				List<DateTime> times = null;
				metTimeSeries.GetCompleteTimeSeries(dtOffset, ref values, ref times);
				if (values == null || times == null)
				{
					prog.ReportError("Could not load any data for gauge " + stringListList[j]);
					return false;
				}
				if (times.Count == 0)
				{
					prog.ReportError("Gauge " + stringListList[j] + " has no valid data.");
					return false;
				}
				TimeSeries timeSeries = new TimeSeries(times, values.Select((_Closure_0024__._0024I52_002D0 == null) ? (_Closure_0024__._0024I52_002D0 = [SpecialName] (float s) => s) : _Closure_0024__._0024I52_002D0));
				iDiscreteTimeSeries.Add(timeSeries);
				timeSeriesGauges.Add(new TimeSeriesGauge(timeSeries, Converter.Convert(pointMs[j])));
			}
			new SimpleTimeSeriesResampler(timeSeriesGauges, new TinInterpolator());
			if (metLayer.DefaultMetRasterization == null)
			{
				prog.ReportError("Met station layer does not have a valid default rasterization.");
				return false;
			}
			return false;
		}
	}

	public void TrySetProfile(string profile)
	{
		int currentProfileIndex = AvailableProfiles.IndexOf(profile);
		if (currentProfileIndex >= 0)
		{
			CurrentProfileIndex = currentProfileIndex;
		}
	}

	void IAnimatingLayer.TrySetProfile(string profile)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TrySetProfile
		this.TrySetProfile(profile);
	}

	public void SetToMaxProfile()
	{
		throw new NotImplementedException();
	}

	void IAnimatingLayer.SetToMaxProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetToMaxProfile
		this.SetToMaxProfile();
	}

	public void SetToMinProfile()
	{
		throw new NotImplementedException();
	}

	void IAnimatingLayer.SetToMinProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetToMinProfile
		this.SetToMinProfile();
	}
}
