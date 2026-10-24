using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using H5Assist.Caching.VB;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.ArrivalTime;
using RasMapperLib.Mapping;
using Utility;

namespace RasMapperLib.Render;

public class ArrivalTimeRenderer : Renderer
{
	[CompilerGenerated]
private sealed class _Closure_0024__12_002D0
	{
		public string oneD;

		public RASGeometryMapPoints mps;

		public float[] terrain;

		public float tol;

		public float timeRatio;

		public int startTimeIndex;

		public double startSimTime;

		public float[] lArrivalTimes;

		public float[] lRecessionTimes;

		public bool computeArrival;

		public bool computeRecession;

		public object cacheLock;

		public string sA;

		public List<string> twoD;

		public ArrivalTimeRenderer arrivalTimeRenderer;

		public _Closure_0024__12_002D0(_Closure_0024__12_002D0 other)
		{
			if (other != null)
			{
				oneD = other.oneD;
				mps = other.mps;
				terrain = other.terrain;
				tol = other.tol;
				timeRatio = other.timeRatio;
				startTimeIndex = other.startTimeIndex;
				startSimTime = other.startSimTime;
				lArrivalTimes = other.lArrivalTimes;
				lRecessionTimes = other.lRecessionTimes;
				computeArrival = other.computeArrival;
				computeRecession = other.computeRecession;
				cacheLock = other.cacheLock;
				sA = other.sA;
				twoD = other.twoD;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1()
		{
			arrivalTimeRenderer.ComputeArrivalTime1D(oneD, mps, terrain, tol, timeRatio, startTimeIndex, startSimTime, arrivalTimeRenderer._colCache, lArrivalTimes, lRecessionTimes, computeArrival, computeRecession, RuntimeHelpers.GetObjectValue(cacheLock));
		}

		[SpecialName]
		internal void _Lambda_0024__2()
		{
			arrivalTimeRenderer.ComputeArrivalTimeSAWINDOWED(sA, mps, terrain, tol, timeRatio, startTimeIndex, startSimTime, arrivalTimeRenderer._colCache, arrivalTimeRenderer._indexedCache, lArrivalTimes, lRecessionTimes, computeArrival, computeRecession, RuntimeHelpers.GetObjectValue(cacheLock));
		}

		[SpecialName]
		internal void _Lambda_0024__3()
		{
			arrivalTimeRenderer.ComputeArrivalTime2D(twoD, mps, terrain, tol, timeRatio, startTimeIndex, startSimTime, arrivalTimeRenderer._colCache, arrivalTimeRenderer._indexedCache, lArrivalTimes, lRecessionTimes, computeArrival, computeRecession, RuntimeHelpers.GetObjectValue(cacheLock));
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__13_002D0
	{
		public float[] lart;

		public double startSimTime;

		public float timeRatio;

		public _Closure_0024__13_002D0(_Closure_0024__13_002D0 other)
		{
			if (other != null)
			{
				lart = other.lart;
				startSimTime = other.startSimTime;
				timeRatio = other.timeRatio;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int fin)
		{
			for (int i = start; i <= fin; i = checked(i + 1))
			{
				if (lart[i] != -9999f)
				{
					lart[i] = (float)(Math.Max((double)lart[i] - startSimTime, 0.0) * (double)timeRatio);
				}
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__14_002D0
	{
		public float[] usxS_WaterSurfaceTimeSeries;

		public float[] dsxS_WaterSurfaceTimeSeries;

		public LeveeProperties usLevee;

		public LeveeProperties dsLevee;

		public _Closure_0024__14_002D1 outerClosure;

		public _Closure_0024__14_002D0(_Closure_0024__14_002D0 other)
		{
			if (other != null)
			{
				usxS_WaterSurfaceTimeSeries = other.usxS_WaterSurfaceTimeSeries;
				dsxS_WaterSurfaceTimeSeries = other.dsxS_WaterSurfaceTimeSeries;
				usLevee = other.usLevee;
				dsLevee = other.dsLevee;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(XSISMapPoint pixel, int time)
		{
			float usxS_WaterSurfaceTimeSery = usxS_WaterSurfaceTimeSeries[time];
			float dsxS_WaterSurfaceTimeSery = dsxS_WaterSurfaceTimeSeries[time];
			bool condition = false;
			bool condition2 = false;
			if (usLevee.LeftExists && dsLevee.LeftExists && usLevee.LeftElevation > usxS_WaterSurfaceTimeSery && dsLevee.LeftElevation > dsxS_WaterSurfaceTimeSery)
			{
				condition = true;
			}
			if (usLevee.RightExists && dsLevee.RightExists && usLevee.RightElevation > usxS_WaterSurfaceTimeSery && dsLevee.RightElevation > dsxS_WaterSurfaceTimeSery)
			{
				condition2 = true;
			}
			float pixelTerm = 1f - pixel.Z;
			float pixelTerm2 = usxS_WaterSurfaceTimeSery * pixel.Z + dsxS_WaterSurfaceTimeSery * pixelTerm;
			if (condition)
			{
				double leftMPixelTerm = usLevee.LeftM * pixel.Z + dsLevee.LeftM * pixelTerm;
				if ((double)pixel.M < leftMPixelTerm)
				{
					return false;
				}
			}
			if (condition2)
			{
				double rightMPixelTerm = usLevee.RightM * pixel.Z + dsLevee.RightM * pixelTerm;
				if ((double)pixel.M > rightMPixelTerm)
				{
					return false;
				}
			}
			if (outerClosure.arrivalTimeRenderer._computeAsDeltaFromStart)
			{
				float usxS_WaterSurfaceTimeSery2 = usxS_WaterSurfaceTimeSeries[outerClosure.startTimeIndex];
				float dsxS_WaterSurfaceTimeSery2 = dsxS_WaterSurfaceTimeSeries[outerClosure.startTimeIndex];
				float pixelIndexTerm = Math.Max(usxS_WaterSurfaceTimeSery2 * pixel.Z + dsxS_WaterSurfaceTimeSery2 * pixelTerm, outerClosure.terrain[pixel.Index]);
				if (pixelTerm2 > pixelIndexTerm + outerClosure.depthThreshold)
				{
					return true;
				}
				return false;
			}
			if (pixelTerm2 > outerClosure.terrain[pixel.Index] + outerClosure.depthThreshold)
			{
				return true;
			}
			return false;
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__14_002D1
	{
		public int startTimeIndex;

		public float[] terrain;

		public float depthThreshold;

		public float[] arrivalTimes;

		public double[] profileTimes;

		public double arrivalStart;

		public float timeRatio;

		public float[] recessionTimes;

		public ArrivalTimeRenderer arrivalTimeRenderer;

		public _Closure_0024__14_002D1(_Closure_0024__14_002D1 other)
		{
			if (other != null)
			{
				startTimeIndex = other.startTimeIndex;
				terrain = other.terrain;
				depthThreshold = other.depthThreshold;
				arrivalTimes = other.arrivalTimes;
				profileTimes = other.profileTimes;
				arrivalStart = other.arrivalStart;
				timeRatio = other.timeRatio;
				recessionTimes = other.recessionTimes;
			}
		}

		[SpecialName]
		internal float _Lambda_0024__1(XSISMapPoint pt)
		{
			return terrain[pt.Index];
		}

		[SpecialName]
		internal void _Lambda_0024__2(XSISMapPoint pt, int time)
		{
			arrivalTimes[pt.Index] = (float)((profileTimes[time] - arrivalStart) * (double)timeRatio);
		}

		[SpecialName]
		internal void _Lambda_0024__3(XSISMapPoint pt, int time)
		{
			recessionTimes[pt.Index] = (float)((profileTimes[time] - arrivalStart) * (double)timeRatio);
		}
	}

	[CompilerGenerated]
private new sealed class _Closure_0024__15_002D0
	{
		public float[] terrain;

		public int startTimeIndex;

		public float depthThreshold;

		public float[] arrivalTimes;

		public double[] profileTimes;

		public double arrivalStart;

		public float timeRatio;

		public float[] recessionTimes;

		public _Closure_0024__15_002D0(_Closure_0024__15_002D0 other)
		{
			if (other != null)
			{
				terrain = other.terrain;
				startTimeIndex = other.startTimeIndex;
				depthThreshold = other.depthThreshold;
				arrivalTimes = other.arrivalTimes;
				profileTimes = other.profileTimes;
				arrivalStart = other.arrivalStart;
				timeRatio = other.timeRatio;
				recessionTimes = other.recessionTimes;
			}
		}

		[SpecialName]
		internal float _Lambda_0024__0(int mp)
		{
			return terrain[mp];
		}

		[SpecialName]
		internal void _Lambda_0024__4(int pixelIdx, int time)
		{
			arrivalTimes[pixelIdx] = (float)((profileTimes[time] - arrivalStart) * (double)timeRatio);
		}

		[SpecialName]
		internal void _Lambda_0024__5(int pixelIdx, int time)
		{
			recessionTimes[pixelIdx] = (float)((profileTimes[time] - arrivalStart) * (double)timeRatio);
		}
	}

	[CompilerGenerated]
private new sealed class _Closure_0024__15_002D1
	{
		public float[] sATimeSeries;

		public float minPlotWS;

		public float sA_MaxWS;

		public _Closure_0024__15_002D0 outerClosure;

		public _Closure_0024__15_002D1(_Closure_0024__15_002D1 other)
		{
			if (other != null)
			{
				sATimeSeries = other.sATimeSeries;
				minPlotWS = other.minPlotWS;
				sA_MaxWS = other.sA_MaxWS;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__1(int pixelIdx, int time)
		{
			float sATimeSery = sATimeSeries[outerClosure.startTimeIndex];
			float sATimeSery2 = sATimeSeries[time];
			if (sATimeSery2 > minPlotWS && sATimeSery2 - Math.Max(outerClosure.terrain[pixelIdx], sATimeSery) > outerClosure.depthThreshold)
			{
				return true;
			}
			return false;
		}

		[SpecialName]
		internal bool _Lambda_0024__2(int pixelIdx, int time)
		{
			float sATimeSery = sATimeSeries[time];
			if (sATimeSery > minPlotWS && sATimeSery - outerClosure.terrain[pixelIdx] > outerClosure.depthThreshold)
			{
				return true;
			}
			return false;
		}

		[SpecialName]
		internal bool _Lambda_0024__3(int pixelIdx)
		{
			return outerClosure.terrain[pixelIdx] <= sA_MaxWS;
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__16_002D0
	{
		public float[] terrain;

		public float depthThreshold;

		public float[] arrivalTimes;

		public double[] profileTimes;

		public double arrivalStart;

		public float timeRatio;

		public float[] recessionTimes;

		public _Closure_0024__16_002D0(_Closure_0024__16_002D0 other)
		{
			if (other != null)
			{
				terrain = other.terrain;
				depthThreshold = other.depthThreshold;
				arrivalTimes = other.arrivalTimes;
				profileTimes = other.profileTimes;
				arrivalStart = other.arrivalStart;
				timeRatio = other.timeRatio;
				recessionTimes = other.recessionTimes;
			}
		}

		[SpecialName]
		internal float _Lambda_0024__0(int i)
		{
			return terrain[i];
		}

		[SpecialName]
		internal void _Lambda_0024__3(int pixel, int time)
		{
			arrivalTimes[pixel] = (float)((profileTimes[time] - arrivalStart) * (double)timeRatio);
		}

		[SpecialName]
		internal void _Lambda_0024__4(int pt, int time)
		{
			recessionTimes[pt] = (float)((profileTimes[time] - arrivalStart) * (double)timeRatio);
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__16_002D1
	{
		public float[] cellTimeSeries;

		public int cIdx;

		public float cellMinElev;

		public _Closure_0024__16_002D2 outerClosure;

		public _Closure_0024__16_002D1(_Closure_0024__16_002D1 other)
		{
			if (other != null)
			{
				cellTimeSeries = other.cellTimeSeries;
				cIdx = other.cIdx;
				cellMinElev = other.cellMinElev;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__1(int pixelIdx, int time)
		{
			float cellTimeSery = cellTimeSeries[time];
			if (cellTimeSery > outerClosure.overrideCellMinElev[cIdx] + outerClosure.outerClosure.depthThreshold && cellTimeSery - outerClosure.outerClosure.terrain[pixelIdx] > outerClosure.outerClosure.depthThreshold)
			{
				return true;
			}
			return false;
		}

		[SpecialName]
		internal bool _Lambda_0024__2(int pixelIdx, int time)
		{
			float cellTimeSery = cellTimeSeries[time];
			if (cellTimeSery > cellMinElev + outerClosure.outerClosure.depthThreshold && cellTimeSery - outerClosure.outerClosure.terrain[pixelIdx] > outerClosure.outerClosure.depthThreshold)
			{
				return true;
			}
			return false;
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__16_002D2
	{
		public float[] overrideCellMinElev;

		public _Closure_0024__16_002D0 outerClosure;

		public _Closure_0024__16_002D2(_Closure_0024__16_002D2 other)
		{
			if (other != null)
			{
				overrideCellMinElev = other.overrideCellMinElev;
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__23_002D0<_0024CLS0>
	{
		public Func<_0024CLS0, float> getElev;

		public WindowSeries series;

		public int startTimeIndex;

		public Func<_0024CLS0, int, bool> isPixelWet;

		public Action<_0024CLS0, int> writeValue;

		public float depthThreshold;

		public _Closure_0024__23_002D0(_Closure_0024__23_002D0<_0024CLS0> other)
		{
			if (other != null)
			{
				getElev = other.getElev;
				series = other.series;
				startTimeIndex = other.startTimeIndex;
				isPixelWet = other.isPixelWet;
				writeValue = other.writeValue;
				depthThreshold = other.depthThreshold;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(_0024CLS0 pixel)
		{
			float pixelTerm = getElev(pixel);
			if (pixelTerm == -9999f)
			{
				return;
			}
			checked
			{
				foreach (Window window in series.Windows)
				{
					if (window.StopTime < startTimeIndex)
					{
						continue;
					}
					int startTimeWindowTerm = Math.Max(window.StartTime, startTimeIndex);
					if (window.Slope == Slope.Decreasing)
					{
						if (isPixelWet(pixel, startTimeWindowTerm))
						{
							writeValue(pixel, startTimeWindowTerm);
							break;
						}
						continue;
					}
					if (window.Slope == Slope.Increasing)
					{
						if (window.MaxValue < pixelTerm + depthThreshold)
						{
							continue;
						}
						if (isPixelWet(pixel, startTimeWindowTerm))
						{
							writeValue(pixel, startTimeWindowTerm);
							break;
						}
						if (isPixelWet(pixel, startTimeWindowTerm + 1))
						{
							writeValue(pixel, startTimeWindowTerm + 1);
							break;
						}
						if (!isPixelWet(pixel, window.StopTime))
						{
							continue;
						}
						int ordinal = startTimeWindowTerm;
						int stopTime2 = window.StopTime;
						while (true)
						{
							int ordinal2 = (int)Math.Round((double)(ordinal + stopTime2) / 2.0);
							if (ordinal2 == ordinal || ordinal2 == stopTime2)
							{
								break;
							}
							bool pixelTerm2 = isPixelWet(pixel, ordinal2);
							bool pixelTerm3 = isPixelWet(pixel, ordinal2 + 1);
							if (!pixelTerm2 && pixelTerm3)
							{
								writeValue(pixel, ordinal2 + 1);
								return;
							}
							if (!pixelTerm2)
							{
								ordinal = ordinal2;
								continue;
							}
							if (pixelTerm2)
							{
								stopTime2 = ordinal2;
								continue;
							}
							throw new NotImplementedException();
						}
						continue;
					}
					if (window.Slope == Slope.Indeterminate)
					{
						if (window.MaxValue < pixelTerm + depthThreshold)
						{
							continue;
						}
						bool condition = false;
						int stopTime = window.StopTime;
						for (int i = startTimeWindowTerm; i <= stopTime; i++)
						{
							if (isPixelWet(pixel, i))
							{
								writeValue(pixel, i);
								condition = true;
								break;
							}
						}
						if (condition)
						{
							break;
						}
						continue;
					}
					throw new Exception("Slope type unrecognized.");
				}
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__24_002D0<_0024CLS0>
	{
		public Func<_0024CLS0, float> getElev;

		public WindowSeries series;

		public Func<_0024CLS0, int, bool> isPixelWet;

		public Action<_0024CLS0, int> writeValue;

		public int startTimeIndex;

		public float depthThreshold;

		public _Closure_0024__24_002D0(_Closure_0024__24_002D0<_0024CLS0> other)
		{
			if (other != null)
			{
				getElev = other.getElev;
				series = other.series;
				isPixelWet = other.isPixelWet;
				writeValue = other.writeValue;
				startTimeIndex = other.startTimeIndex;
				depthThreshold = other.depthThreshold;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(_0024CLS0 pixel)
		{
			float pixelTerm = getElev(pixel);
			if (pixelTerm == -9999f)
			{
				return;
			}
			checked
			{
				foreach (Window reverse in series.Windows.AsEnumerable().Reverse())
				{
					if (isPixelWet(pixel, reverse.StopTime))
					{
						writeValue(pixel, reverse.StopTime);
						break;
					}
					if (reverse.StopTime < startTimeIndex)
					{
						continue;
					}
					int startTimeTerm = Math.Max(reverse.StartTime, startTimeIndex);
					if (reverse.Slope == Slope.Increasing)
					{
						if (isPixelWet(pixel, reverse.StopTime))
						{
							writeValue(pixel, reverse.StopTime);
							break;
						}
						continue;
					}
					if (reverse.Slope == Slope.Decreasing)
					{
						if (reverse.MaxValue < pixelTerm + depthThreshold || !isPixelWet(pixel, startTimeTerm))
						{
							continue;
						}
						if (isPixelWet(pixel, reverse.StopTime))
						{
							writeValue(pixel, reverse.StopTime);
							break;
						}
						if (isPixelWet(pixel, reverse.StopTime - 1))
						{
							writeValue(pixel, reverse.StopTime - 1);
							break;
						}
						int ordinal = startTimeTerm;
						int stopTime2 = reverse.StopTime;
						while (true)
						{
							int ordinal2 = (int)Math.Round((double)(ordinal + stopTime2) / 2.0);
							if (ordinal2 == ordinal || ordinal2 == stopTime2)
							{
								break;
							}
							bool pixelTerm2 = isPixelWet(pixel, ordinal2);
							bool pixelTerm3 = isPixelWet(pixel, ordinal2 + 1);
							if (pixelTerm2 && !pixelTerm3)
							{
								writeValue(pixel, ordinal2 + 1);
								return;
							}
							if (!pixelTerm2)
							{
								stopTime2 = ordinal2;
								continue;
							}
							if (pixelTerm2)
							{
								ordinal = ordinal2;
								continue;
							}
							throw new NotImplementedException();
						}
						continue;
					}
					if (reverse.Slope == Slope.Indeterminate)
					{
						if (reverse.MaxValue < pixelTerm + depthThreshold)
						{
							continue;
						}
						bool condition = false;
						int stopTime = reverse.StopTime;
						int ordinal3 = startTimeTerm;
						for (int i = stopTime; i >= ordinal3; i += -1)
						{
							if (isPixelWet(pixel, i))
							{
								writeValue(pixel, i);
								condition = true;
								break;
							}
						}
						if (condition)
						{
							break;
						}
						continue;
					}
					throw new Exception("Slope type unrecognized.");
				}
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__9_002D0
	{
		public float[] lambdaArrival;

		public float[] lambdaRecession;

		public float[] lambdaDuration;

		public _Closure_0024__9_002D0(_Closure_0024__9_002D0 other)
		{
			if (other != null)
			{
				lambdaArrival = other.lambdaArrival;
				lambdaRecession = other.lambdaRecession;
				lambdaDuration = other.lambdaDuration;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (lambdaArrival[i] != -9999f && lambdaRecession[i] != -9999f)
				{
					lambdaDuration[i] = lambdaRecession[i] - lambdaArrival[i];
				}
			}
		}
	}

	private bool _computeAsDeltaFromStart;

	private Names _wsNames;

	private H5IndexedCache1D<int> _indexedCache;

	private H5ColumnCache<float> _colCache;

	public static void ComputeArrivalTime(RASResults res, RASGeometryMapPoints mps, float[] terrain, float depthThreshold, bool useHours, bool useDeltaFromStart, double startSimTime, ref float[] arrivalTimes, CacheCollection cache = null)
	{
		ArrivalTimeRenderer arrivalTimeRenderer = new ArrivalTimeRenderer(res, cache);
		arrivalTimeRenderer._computeAsDeltaFromStart = useDeltaFromStart;
		arrivalTimeRenderer.ComputeArrivalTime(mps, terrain, depthThreshold, useHours, startSimTime, ref arrivalTimes);
	}

	public static void ComputeArrivalTimeMax(RASResults res, RASGeometryMapPoints mps, float[] terrain, float depthThreshold, bool useHours, double startSimTime, ref float[] arrivalTimes, CacheCollection cache = null)
	{
		new ArrivalTimeRenderer(res, cache).ComputeArrivalTimeMax(mps, terrain, useHours, startSimTime, ref arrivalTimes);
	}

	public static void ComputeRecessionGrid(RASResults res, RASGeometryMapPoints mps, float[] terrain, float depthThreshold, bool useHours, bool useDeltaFromStart, ref float[] recessionTimes, CacheCollection cache = null)
	{
		new ArrivalTimeRenderer(res, cache).ComputeRecessionGrid(mps, terrain, depthThreshold, useHours, ref recessionTimes);
	}

	public static void ComputeDurationGrid(RASResults res, RASGeometryMapPoints mps, float[] terrain, float depthThreshold, bool useHours, double startSimTime, ref float[] durationTimes, CacheCollection cache = null)
	{
		ArrivalTimeRenderer arrivalTimeRenderer = new ArrivalTimeRenderer(res, cache);
		bool useHours2 = useHours;
		float[] arrivalTimes = null;
		float[] recessionTimes = null;
		arrivalTimeRenderer.ComputeDurationGrid(mps, terrain, depthThreshold, useHours2, startSimTime, ref durationTimes, ref arrivalTimes, ref recessionTimes);
	}

	public ArrivalTimeRenderer(RASResults res, CacheCollection cache = null)
		: base(res, cache, needsVelocityFlowData: false, needsPostProcessFile: true)
	{
		_computeAsDeltaFromStart = false;
		_wsNames = GetWaterSurfaceDatasetNames();
		_wsNames.Validate(_postProcessFilename);
		_postProcessor.EnsureXSRegionsLoaded();
		_cache = cache;
		_indexedCache = cache.GetIndexedCache<int>(_postProcessFilename);
		_colCache = cache.GetColumnCache<float>(_postProcessFilename);
		_colCache.CacheSizePerDataset = 50f;
	}

	public void ComputeDurationGrid(RASGeometryMapPoints mps, float[] terrain, float depthThreshold, bool useHours, double startSimTime, ref float[] durationTimes, [Optional][DefaultParameterValue(null)] ref float[] arrivalTimes, [Optional][DefaultParameterValue(null)] ref float[] recessionTimes)
	{
		_Closure_0024__9_002D0 capturedLocals = default;
		_Closure_0024__9_002D0 capturedLocals2 = new _Closure_0024__9_002D0(capturedLocals);
		Buffers.ArraySetAllVB(ref durationTimes, -9999f, mps.InputCount);
		ComputeArrivalTime(mps, terrain, depthThreshold, useHours, startSimTime, ref arrivalTimes, ref recessionTimes, computeArrival: true, computeRecession: true);
		capturedLocals2.lambdaDuration = durationTimes;
		capturedLocals2.lambdaArrival = arrivalTimes;
		capturedLocals2.lambdaRecession = recessionTimes;
		Utility.Parallel.SmartFor(durationTimes.Count(), [SpecialName] (int start, int finish) =>
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (capturedLocals2.lambdaArrival[i] != -9999f && capturedLocals2.lambdaRecession[i] != -9999f)
				{
					capturedLocals2.lambdaDuration[i] = capturedLocals2.lambdaRecession[i] - capturedLocals2.lambdaArrival[i];
				}
			}
		});
	}

	public void ComputeRecessionGrid(RASGeometryMapPoints mps, float[] terrain, float depthThreshold, bool useHours, ref float[] recessionTimes)
	{
		bool useHours2 = useHours;
		float[] arrivalTimes = null;
		ComputeArrivalTime(mps, terrain, depthThreshold, useHours2, 0.0, ref arrivalTimes, ref recessionTimes, computeArrival: false, computeRecession: true);
	}

	public void ComputeArrivalTime(RASGeometryMapPoints mps, float[] terrain, float depthThreshold, bool useHours, double startSimTime, ref float[] arrivalTimes)
	{
		bool useHours2 = useHours;
		float[] recessionTimes = null;
		ComputeArrivalTime(mps, terrain, depthThreshold, useHours2, startSimTime, ref arrivalTimes, ref recessionTimes, computeArrival: true, computeRecession: false);
	}

	private void ComputeArrivalTime(RASGeometryMapPoints mps, float[] terrain, float depthThreshold, bool useHours, double startSimTime, ref float[] arrivalTimes, ref float[] recessionTimes, bool computeArrival, bool computeRecession)
	{
		_Closure_0024__12_002D0 capturedLocals = default;
		_Closure_0024__12_002D0 capturedLocals2 = new _Closure_0024__12_002D0(capturedLocals);
		capturedLocals2.arrivalTimeRenderer = this;
		capturedLocals2.mps = mps;
		capturedLocals2.terrain = terrain;
		capturedLocals2.startSimTime = startSimTime;
		capturedLocals2.computeArrival = computeArrival;
		capturedLocals2.computeRecession = computeRecession;
		if (!capturedLocals2.computeArrival && !capturedLocals2.computeRecession)
		{
			throw new ArgumentException("Argument error to ComputeArrivalTime");
		}
		if (capturedLocals2.computeArrival)
		{
			Buffers.ArraySetAllVB(ref arrivalTimes, -9999f, capturedLocals2.mps.InputCount);
		}
		if (capturedLocals2.computeRecession)
		{
			Buffers.ArraySetAllVB(ref recessionTimes, -9999f, capturedLocals2.mps.InputCount);
		}
		capturedLocals2.tol = MinWSPlotTolerance;
		if (!float.IsNaN(depthThreshold) && depthThreshold > capturedLocals2.tol)
		{
			capturedLocals2.tol = depthThreshold;
		}
		capturedLocals2.oneD = null;
		capturedLocals2.sA = null;
		capturedLocals2.twoD = null;
		Names wsNames = _wsNames;
		ref string dataset = ref capturedLocals2.oneD;
		int row = 0;
		wsNames.Get1D(0, ref dataset, ref row);
		Names wsNames2 = _wsNames;
		ref string dataset2 = ref capturedLocals2.sA;
		row = 0;
		wsNames2.GetSA(0, ref dataset2, ref row);
		Names wsNames3 = _wsNames;
		ref List<string> datasets = ref capturedLocals2.twoD;
		row = 0;
		wsNames3.Get2D(0, ref datasets, ref row);
		if (useHours)
		{
			capturedLocals2.timeRatio = 24f;
		}
		else
		{
			capturedLocals2.timeRatio = 1f;
		}
		capturedLocals2.startTimeIndex = Array.BinarySearch(_result.ProfileSimulationTimes.Select([SpecialName] (double d) => Math.Round(d, 10)).ToArray(), capturedLocals2.startSimTime);
		if (capturedLocals2.startTimeIndex < 0)
		{
			capturedLocals2.startTimeIndex = ~capturedLocals2.startTimeIndex;
		}
		capturedLocals2.cacheLock = RuntimeHelpers.GetObjectValue(new object());
		capturedLocals2.lArrivalTimes = arrivalTimes;
		capturedLocals2.lRecessionTimes = recessionTimes;
		List<Task> tasks = new List<Task>();
		if (capturedLocals2.mps.MappedXSISs.Count > 0 && !string.IsNullOrEmpty(capturedLocals2.oneD))
		{
			tasks.Add(Task.Factory.StartNew([SpecialName] () =>
			{
				capturedLocals2.arrivalTimeRenderer.ComputeArrivalTime1D(capturedLocals2.oneD, capturedLocals2.mps, capturedLocals2.terrain, capturedLocals2.tol, capturedLocals2.timeRatio, capturedLocals2.startTimeIndex, capturedLocals2.startSimTime, capturedLocals2.arrivalTimeRenderer._colCache, capturedLocals2.lArrivalTimes, capturedLocals2.lRecessionTimes, capturedLocals2.computeArrival, capturedLocals2.computeRecession, RuntimeHelpers.GetObjectValue(capturedLocals2.cacheLock));
			}));
		}
		if (capturedLocals2.mps.MappedSAs.Count > 0 && !string.IsNullOrEmpty(capturedLocals2.sA))
		{
			tasks.Add(Task.Factory.StartNew([SpecialName] () =>
			{
				capturedLocals2.arrivalTimeRenderer.ComputeArrivalTimeSAWINDOWED(capturedLocals2.sA, capturedLocals2.mps, capturedLocals2.terrain, capturedLocals2.tol, capturedLocals2.timeRatio, capturedLocals2.startTimeIndex, capturedLocals2.startSimTime, capturedLocals2.arrivalTimeRenderer._colCache, capturedLocals2.arrivalTimeRenderer._indexedCache, capturedLocals2.lArrivalTimes, capturedLocals2.lRecessionTimes, capturedLocals2.computeArrival, capturedLocals2.computeRecession, RuntimeHelpers.GetObjectValue(capturedLocals2.cacheLock));
			}));
		}
		if (capturedLocals2.mps.AllMappedMeshIndexes().Count() > 0 && capturedLocals2.twoD != null && capturedLocals2.twoD.Count > 0)
		{
			tasks.Add(Task.Factory.StartNew([SpecialName] () =>
			{
				capturedLocals2.arrivalTimeRenderer.ComputeArrivalTime2D(capturedLocals2.twoD, capturedLocals2.mps, capturedLocals2.terrain, capturedLocals2.tol, capturedLocals2.timeRatio, capturedLocals2.startTimeIndex, capturedLocals2.startSimTime, capturedLocals2.arrivalTimeRenderer._colCache, capturedLocals2.arrivalTimeRenderer._indexedCache, capturedLocals2.lArrivalTimes, capturedLocals2.lRecessionTimes, capturedLocals2.computeArrival, capturedLocals2.computeRecession, RuntimeHelpers.GetObjectValue(capturedLocals2.cacheLock));
			}));
		}
		checked
		{
			if (tasks.Count != 0)
			{
				row = tasks.Count - 1;
				for (int intervalIndex = 0; intervalIndex <= row; intervalIndex++)
				{
					tasks[intervalIndex].Wait();
				}
			}
		}
	}

	private void ComputeArrivalTimeMax(RASGeometryMapPoints mps, float[] terrain, bool useHours, double startSimTime, ref float[] arrivalTimes)
	{
		_Closure_0024__13_002D0 capturedLocals = default;
		_Closure_0024__13_002D0 capturedLocals2 = new _Closure_0024__13_002D0(capturedLocals);
		capturedLocals2.startSimTime = startSimTime;
		Buffers.ArraySetAllVB(ref arrivalTimes, -9999f, mps.InputCount);
		_ = MinWSPlotTolerance;
		if (useHours)
		{
			capturedLocals2.timeRatio = 24f;
		}
		else
		{
			capturedLocals2.timeRatio = 1f;
		}
		float[] waterSurfaces = null;
		WaterSurfaceRenderer.ComputeTimeAtWaterSurface(_result, int.MaxValue, mps, ref waterSurfaces, ref arrivalTimes, terrain, _cache);
		capturedLocals2.lart = arrivalTimes;
		Utility.Parallel.SmartFor(arrivalTimes.Length, [SpecialName] (int start, int fin) =>
		{
			for (int i = start; i <= fin; i = checked(i + 1))
			{
				if (capturedLocals2.lart[i] != -9999f)
				{
					capturedLocals2.lart[i] = (float)(Math.Max((double)capturedLocals2.lart[i] - capturedLocals2.startSimTime, 0.0) * (double)capturedLocals2.timeRatio);
				}
			}
		});
	}

	private void ComputeArrivalTime1D(string crossSectionDataset, RASGeometryMapPoints mps, float[] terrain, float depthThreshold, float timeRatio, int startTimeIndex, double arrivalStart, H5ColumnCache<float> cache, float[] arrivalTimes, float[] recessionTimes, bool computeArrival, bool computeRecession, object cacheLock = null)
	{
		_Closure_0024__14_002D1 capturedLocals = new _Closure_0024__14_002D1(null);
		capturedLocals.arrivalTimeRenderer = this;
		capturedLocals.terrain = terrain;
		capturedLocals.depthThreshold = depthThreshold;
		capturedLocals.timeRatio = timeRatio;
		capturedLocals.startTimeIndex = startTimeIndex;
		capturedLocals.arrivalStart = arrivalStart;
		capturedLocals.arrivalTimes = arrivalTimes;
		capturedLocals.recessionTimes = recessionTimes;
		capturedLocals.profileTimes = _result.ProfileSimulationTimes;
		if (cacheLock == null)
		{
			cacheLock = RuntimeHelpers.GetObjectValue(new object());
		}
		object cacheLock2 = cacheLock;
		ObjectFlowControl.CheckForSyncLockOnValueType(cacheLock2);
		bool lockTaken = false;
		int index;
		try
		{
			Monitor.Enter(cacheLock2, ref lockTaken);
			if (!cache.Datasets.Contains(crossSectionDataset))
			{
				cache.EnsureDataset(crossSectionDataset);
			}
			index = cache.GetIndex(crossSectionDataset);
		}
		finally
		{
			if (lockTaken)
			{
				Monitor.Exit(cacheLock2);
			}
		}
		_Closure_0024__14_002D0 capturedLocals2 = default;
		foreach (MappedXSIS mappedXSIS in mps.MappedXSISs)
		{
			capturedLocals2 = new _Closure_0024__14_002D0(capturedLocals2);
			capturedLocals2.outerClosure = capturedLocals;
			int iD = mappedXSIS.ID;
			int upstreamXSID = _XSIS.GetUpstreamXSID(iD);
			int downstreamXSID = _XSIS.GetDownstreamXSID(iD);
			capturedLocals2.usLevee = _XS.GetLeveeProperties(upstreamXSID);
			capturedLocals2.dsLevee = _XS.GetLeveeProperties(downstreamXSID);
			object cacheLock3 = cacheLock;
			ObjectFlowControl.CheckForSyncLockOnValueType(cacheLock3);
			bool lockTaken2 = false;
			try
			{
				Monitor.Enter(cacheLock3, ref lockTaken2);
				capturedLocals2.usxS_WaterSurfaceTimeSeries = cache.Item(index, upstreamXSID);
				capturedLocals2.dsxS_WaterSurfaceTimeSeries = cache.Item(index, downstreamXSID);
			}
			finally
			{
				if (lockTaken2)
				{
					Monitor.Exit(cacheLock3);
				}
			}
			WindowSeries series = Get1DWindows(upstreamXSID, downstreamXSID, capturedLocals2.usxS_WaterSurfaceTimeSeries, capturedLocals2.dsxS_WaterSurfaceTimeSeries);
			Func<XSISMapPoint, int, bool> isPixelWet = capturedLocals2._Lambda_0024__0;
			Func<XSISMapPoint, float> getElev = [SpecialName] (XSISMapPoint pt) => capturedLocals2.outerClosure.terrain[pt.Index];
			if (computeArrival)
			{
				Action<XSISMapPoint, int> writeValue = [SpecialName] (XSISMapPoint pt, int time) =>
				{
					capturedLocals2.outerClosure.arrivalTimes[pt.Index] = (float)((capturedLocals2.outerClosure.profileTimes[time] - capturedLocals2.outerClosure.arrivalStart) * (double)capturedLocals2.outerClosure.timeRatio);
				};
				ParseWindowForward(series, capturedLocals2.outerClosure.startTimeIndex, capturedLocals2.outerClosure.depthThreshold, getElev, isPixelWet, writeValue, mappedXSIS.MapPoints);
			}
			if (computeRecession)
			{
				Action<XSISMapPoint, int> writeValue2 = [SpecialName] (XSISMapPoint pt, int time) =>
				{
					capturedLocals2.outerClosure.recessionTimes[pt.Index] = (float)((capturedLocals2.outerClosure.profileTimes[time] - capturedLocals2.outerClosure.arrivalStart) * (double)capturedLocals2.outerClosure.timeRatio);
				};
				ParseWindowBackward(series, capturedLocals2.outerClosure.startTimeIndex, capturedLocals2.outerClosure.depthThreshold, getElev, isPixelWet, writeValue2, mappedXSIS.MapPoints);
			}
		}
	}

	private void ComputeArrivalTimeSAWINDOWED(string sADataset, RASGeometryMapPoints mps, float[] terrain, float depthThreshold, float timeRatio, int startTimeIndex, double arrivalStart, H5ColumnCache<float> cache, H5IndexedCache1D<int> indexedCache, float[] arrivalTimes, float[] recessionTimes, bool computeArrival, bool computeRecession, object cacheLock = null)
	{
		_Closure_0024__15_002D0 capturedLocals = default;
		_Closure_0024__15_002D0 capturedLocals2 = new _Closure_0024__15_002D0(capturedLocals);
		capturedLocals2.terrain = terrain;
		capturedLocals2.depthThreshold = depthThreshold;
		capturedLocals2.timeRatio = timeRatio;
		capturedLocals2.startTimeIndex = startTimeIndex;
		capturedLocals2.arrivalStart = arrivalStart;
		capturedLocals2.arrivalTimes = arrivalTimes;
		capturedLocals2.recessionTimes = recessionTimes;
		capturedLocals2.profileTimes = _result.ProfileSimulationTimes;
		Func<int, float> getElev = [SpecialName] (int mp) => capturedLocals2.terrain[mp];
		if (cacheLock == null)
		{
			cacheLock = RuntimeHelpers.GetObjectValue(new object());
		}
		string sADatasetTerm = sADataset + " Increasing Regions Info";
		string sADatasetTerm2 = sADataset + " Increasing Regions Values";
		object cacheLock2 = cacheLock;
		ObjectFlowControl.CheckForSyncLockOnValueType(cacheLock2);
		bool lockTaken = false;
		int index;
		int valuesIndex;
		int infoIndex;
		try
		{
			Monitor.Enter(cacheLock2, ref lockTaken);
			if (!cache.Datasets.Contains(sADataset))
			{
				cache.EnsureDataset(sADataset);
			}
			index = cache.GetIndex(sADataset);
			if (!indexedCache.ValuesDatasets.Contains(sADatasetTerm2))
			{
				indexedCache.AddDataset(sADatasetTerm, sADatasetTerm2);
			}
			valuesIndex = indexedCache.GetValuesIndex(sADatasetTerm2);
			infoIndex = indexedCache.GetInfoIndex(sADatasetTerm);
		}
		finally
		{
			if (lockTaken)
			{
				Monitor.Exit(cacheLock2);
			}
		}
		RASStorageArea storageArea = _geometry.StorageArea;
		_Closure_0024__15_002D1 capturedLocals3 = default;
		foreach (SAMap mappedSA in mps.MappedSAs)
		{
			capturedLocals3 = new _Closure_0024__15_002D1(capturedLocals3);
			capturedLocals3.outerClosure = capturedLocals2;
			int iD = mappedSA.ID;
			capturedLocals3.minPlotWS = storageArea.MinElevation(iD) + capturedLocals3.outerClosure.depthThreshold;
			int[] infoRow = null;
			object cacheLock3 = cacheLock;
			ObjectFlowControl.CheckForSyncLockOnValueType(cacheLock3);
			bool lockTaken2 = false;
			int[] extrema;
			try
			{
				Monitor.Enter(cacheLock3, ref lockTaken2);
				extrema = indexedCache.Item(infoIndex, valuesIndex, iD, ref infoRow);
				capturedLocals3.sATimeSeries = cache.Item(index, iD);
			}
			finally
			{
				if (lockTaken2)
				{
					Monitor.Exit(cacheLock3);
				}
			}
			WindowSeries series = Get2DWindows(infoRow, extrema, capturedLocals3.sATimeSeries);
			Func<int, int, bool> func = null;
			func = ((!_computeAsDeltaFromStart) ? new Func<int, int, bool>(capturedLocals3._Lambda_0024__2) : new Func<int, int, bool>(capturedLocals3._Lambda_0024__1));
			capturedLocals3.sA_MaxWS = capturedLocals3.sATimeSeries.Max();
			IEnumerable<int> items = mappedSA.MapPoints.Where(capturedLocals3._Lambda_0024__3);
			if (computeArrival)
			{
				Action<int, int> writeValue = [SpecialName] (int pixelIdx, int time) =>
				{
					capturedLocals3.outerClosure.arrivalTimes[pixelIdx] = (float)((capturedLocals3.outerClosure.profileTimes[time] - capturedLocals3.outerClosure.arrivalStart) * (double)capturedLocals3.outerClosure.timeRatio);
				};
				ParseWindowForward(series, capturedLocals3.outerClosure.startTimeIndex, capturedLocals3.outerClosure.depthThreshold, getElev, func, writeValue, items);
			}
			if (computeRecession)
			{
				Action<int, int> writeValue2 = [SpecialName] (int pixelIdx, int time) =>
				{
					capturedLocals3.outerClosure.recessionTimes[pixelIdx] = (float)((capturedLocals3.outerClosure.profileTimes[time] - capturedLocals3.outerClosure.arrivalStart) * (double)capturedLocals3.outerClosure.timeRatio);
				};
				ParseWindowBackward(series, capturedLocals3.outerClosure.startTimeIndex, capturedLocals3.outerClosure.depthThreshold, getElev, func, writeValue2, items);
			}
		}
	}

	private void ComputeArrivalTime2D(List<string> twoD_Datasets, RASGeometryMapPoints mps, float[] terrain, float depthThreshold, float timeRatio, int startTimeIndex, double arrivalStart, H5ColumnCache<float> cache, H5IndexedCache1D<int> indexedCache, float[] arrivalTimes, float[] recessionTimes, bool computeArrival, bool computeRecession, object cacheLock = null)
	{
		_Closure_0024__16_002D0 capturedLocals = default;
		_Closure_0024__16_002D0 capturedLocals2 = new _Closure_0024__16_002D0(capturedLocals);
		capturedLocals2.terrain = terrain;
		capturedLocals2.depthThreshold = depthThreshold;
		capturedLocals2.timeRatio = timeRatio;
		capturedLocals2.arrivalStart = arrivalStart;
		capturedLocals2.arrivalTimes = arrivalTimes;
		capturedLocals2.recessionTimes = recessionTimes;
		if (cacheLock == null)
		{
			cacheLock = RuntimeHelpers.GetObjectValue(new object());
		}
		capturedLocals2.profileTimes = _result.ProfileSimulationTimes;
		Func<int, float> getElev = [SpecialName] (int i) => capturedLocals2.terrain[i];
		_Closure_0024__16_002D2 capturedLocals3 = default;
		_Closure_0024__16_002D1 capturedLocals4 = default;
		foreach (int allMappedMeshIndex in mps.AllMappedMeshIndexes())
		{
			capturedLocals3 = new _Closure_0024__16_002D2(capturedLocals3);
			capturedLocals3.outerClosure = capturedLocals2;
			MeshFV2D meshFV2D = _d2FlowArea.Meshes().ElementAtOrDefault(allMappedMeshIndex);
			string twoD_Dataset = twoD_Datasets[allMappedMeshIndex];
			string twoD_DatasetTerm = twoD_Dataset + " Increasing Regions Info";
			string twoD_DatasetTerm2 = twoD_Dataset + " Increasing Regions Values";
			capturedLocals3.overrideCellMinElev = null;
			object cacheLock2 = cacheLock;
			ObjectFlowControl.CheckForSyncLockOnValueType(cacheLock2);
			bool lockTaken = false;
			int index;
			int valuesIndex;
			int infoIndex;
			try
			{
				Monitor.Enter(cacheLock2, ref lockTaken);
				if (!cache.Datasets.Contains(twoD_Dataset))
				{
					cache.EnsureDataset(twoD_Dataset);
				}
				index = cache.GetIndex(twoD_Dataset);
				if (!indexedCache.ValuesDatasets.Contains(twoD_DatasetTerm2))
				{
					indexedCache.AddDataset(twoD_DatasetTerm, twoD_DatasetTerm2);
				}
				valuesIndex = indexedCache.GetValuesIndex(twoD_DatasetTerm2);
				infoIndex = indexedCache.GetInfoIndex(twoD_DatasetTerm);
				if (_computeAsDeltaFromStart)
				{
					capturedLocals3.overrideCellMinElev = _cache.GetRowCache<float>(_hdfFilename).EnsureGetRow(twoD_Dataset, startTimeIndex);
				}
			}
			finally
			{
				if (lockTaken)
				{
					Monitor.Exit(cacheLock2);
				}
			}
			foreach (FlatCellMap allMappedCell in mps.AllMappedCells(allMappedMeshIndex))
			{
				capturedLocals4 = new _Closure_0024__16_002D1(capturedLocals4);
				capturedLocals4.outerClosure = capturedLocals3;
				capturedLocals4.cIdx = allMappedCell.Index;
				int[] infoRow = null;
				object cacheLock3 = cacheLock;
				ObjectFlowControl.CheckForSyncLockOnValueType(cacheLock3);
				bool lockTaken2 = false;
				int[] extrema;
				try
				{
					Monitor.Enter(cacheLock3, ref lockTaken2);
					extrema = indexedCache.Item(infoIndex, valuesIndex, capturedLocals4.cIdx, ref infoRow);
					capturedLocals4.cellTimeSeries = cache.Item(index, capturedLocals4.cIdx);
				}
				finally
				{
					if (lockTaken2)
					{
						Monitor.Exit(cacheLock3);
					}
				}
				capturedLocals4.cellMinElev = meshFV2D.Cell(capturedLocals4.cIdx).MinimumElevation;
				WindowSeries series = Get2DWindows(infoRow, extrema, capturedLocals4.cellTimeSeries);
				Func<int, int, bool> func = null;
				func = ((capturedLocals4.outerClosure.overrideCellMinElev == null) ? new Func<int, int, bool>(capturedLocals4._Lambda_0024__2) : new Func<int, int, bool>(capturedLocals4._Lambda_0024__1));
				if (computeArrival)
				{
					Action<int, int> writeValue = [SpecialName] (int pixel, int time) =>
					{
						capturedLocals4.outerClosure.outerClosure.arrivalTimes[pixel] = (float)((capturedLocals4.outerClosure.outerClosure.profileTimes[time] - capturedLocals4.outerClosure.outerClosure.arrivalStart) * (double)capturedLocals4.outerClosure.outerClosure.timeRatio);
					};
					ParseWindowForward(series, startTimeIndex, capturedLocals4.outerClosure.outerClosure.depthThreshold, getElev, func, writeValue, allMappedCell.MapPoints);
				}
				if (computeRecession)
				{
					Action<int, int> writeValue2 = [SpecialName] (int pt, int time) =>
					{
						capturedLocals4.outerClosure.outerClosure.recessionTimes[pt] = (float)((capturedLocals4.outerClosure.outerClosure.profileTimes[time] - capturedLocals4.outerClosure.outerClosure.arrivalStart) * (double)capturedLocals4.outerClosure.outerClosure.timeRatio);
					};
					ParseWindowBackward(series, startTimeIndex, capturedLocals4.outerClosure.outerClosure.depthThreshold, getElev, func, writeValue2, allMappedCell.MapPoints);
				}
			}
		}
	}

	private List<LocalExtrema>[] ParseLocalExtrema(int[,] rgnInfo, int[] rgnValues)
	{
		checked
		{
			List<LocalExtrema>[] localExtremaListArray = new List<LocalExtrema>[rgnInfo.GetLength(0) - 1 + 1];
			int count = localExtremaListArray.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				localExtremaListArray[i] = ParseLocalExtremaRow(rgnInfo, rgnValues, i);
			}
			return localExtremaListArray;
		}
	}

	private List<LocalExtrema> ParseLocalExtremaRow(int[,] rgnInfo, int[] rgnValues, int row)
	{
		List<LocalExtrema> localExtremas = new List<LocalExtrema>();
		ExtremaType extremaType = (ExtremaType)rgnInfo[row, 2];
		int rgnInfoRowTerm = rgnInfo[row, 0];
		checked
		{
			int count = rgnInfo[row, 0] + rgnInfo[row, 1] - 1;
			for (int i = rgnInfoRowTerm; i <= count; i++)
			{
				localExtremas.Add(new LocalExtrema(extremaType, rgnValues[i]));
				extremaType = ArrivalTimeExtensions.SwapType(extremaType);
			}
			return localExtremas;
		}
	}

	private List<LocalExtrema> ParseLocalExtremaRow(int[] rgnInfo, int[] rgnValues)
	{
		List<LocalExtrema> localExtremas = new List<LocalExtrema>();
		ExtremaType extremaType = (ExtremaType)rgnInfo[2];
		int rgnInfo2 = rgnInfo[0];
		checked
		{
			int count = rgnInfo[0] + rgnInfo[1] - 1;
			for (int i = rgnInfo2; i <= count; i++)
			{
				localExtremas.Add(new LocalExtrema(extremaType, rgnValues[i]));
				extremaType = ArrivalTimeExtensions.SwapType(extremaType);
			}
			return localExtremas;
		}
	}

	private WindowSeries Get1DWindows(int USXS_ID, int DSXS_ID, float[] USXS_TimeSeries, float[] DSXS_TimeSeries)
	{
		List<LocalExtrema> xSExtrema = _postProcessor.GetXSExtrema(USXS_ID);
		List<LocalExtrema> xSExtrema2 = _postProcessor.GetXSExtrema(DSXS_ID);
		TimeSeriesExtrema timeSeriesExtrema = new TimeSeriesExtrema(xSExtrema.Count);
		TimeSeriesExtrema timeSeriesExtrema2 = new TimeSeriesExtrema(xSExtrema2.Count);
		foreach (LocalExtrema xSExtrema3 in xSExtrema)
		{
			timeSeriesExtrema.Add(new LocalExtremaValue(xSExtrema3, USXS_TimeSeries[xSExtrema3.Time]));
		}
		foreach (LocalExtrema xSExtrema22 in xSExtrema2)
		{
			timeSeriesExtrema2.Add(new LocalExtremaValue(xSExtrema22, DSXS_TimeSeries[xSExtrema22.Time]));
		}
		return timeSeriesExtrema.Combine(timeSeriesExtrema2);
	}

	private WindowSeries Get2DWindows(int[] info, int[] extrema, float[] cellTimeSeries)
	{
		info[0] = 0;
		List<LocalExtrema> parseLocalExtremaRow = ParseLocalExtremaRow(info, extrema);
		TimeSeriesExtrema timeSeriesExtrema = new TimeSeriesExtrema(parseLocalExtremaRow.Count);
		foreach (LocalExtrema parseLocalExtremaRow2 in parseLocalExtremaRow)
		{
			timeSeriesExtrema.Add(new LocalExtremaValue(parseLocalExtremaRow2, cellTimeSeries[parseLocalExtremaRow2.Time]));
		}
		return timeSeriesExtrema.ToWindowSeries();
	}

	private void ValidateNames(string filename, ref string name1, ref string name2, ref List<string> nameList)
	{
		using H5Reader h5Reader = new H5Reader(filename);
		if (name1 != null && !h5Reader.ExistsAsDataset(name1))
		{
			name1 = null;
		}
		if (name2 != null && !h5Reader.ExistsAsDataset(name2))
		{
			name2 = null;
		}
		if (nameList == null)
		{
			return;
		}
		bool condition = false;
		foreach (string name3 in nameList)
		{
			if (!h5Reader.ExistsAsDataset(name3))
			{
				condition = true;
			}
		}
		if (condition)
		{
			nameList.Clear();
		}
	}

	private void ParseWindowForward<T>(WindowSeries series, int startTimeIndex, float depthThreshold, Func<T, float> getElev, Func<T, int, bool> isPixelWet, Action<T, int> writeValue, IEnumerable<T> items)
	{
		_Closure_0024__23_002D0<T> capturedLocals = default;
		_Closure_0024__23_002D0<T> capturedLocals2 = new _Closure_0024__23_002D0<T>(capturedLocals);
		capturedLocals2.series = series;
		capturedLocals2.startTimeIndex = startTimeIndex;
		capturedLocals2.depthThreshold = depthThreshold;
		capturedLocals2.getElev = getElev;
		capturedLocals2.isPixelWet = isPixelWet;
		capturedLocals2.writeValue = writeValue;
		System.Threading.Tasks.Parallel.ForEach(items, checked([SpecialName] (T pixel) =>
		{
			float capturedLocals3 = capturedLocals2.getElev(pixel);
			if (capturedLocals3 != -9999f)
			{
				foreach (Window window in capturedLocals2.series.Windows)
				{
					if (window.StopTime >= capturedLocals2.startTimeIndex)
					{
						int startTimeWindowTerm = Math.Max(window.StartTime, capturedLocals2.startTimeIndex);
						if (window.Slope == Slope.Decreasing)
						{
							if (capturedLocals2.isPixelWet(pixel, startTimeWindowTerm))
							{
								capturedLocals2.writeValue(pixel, startTimeWindowTerm);
								break;
							}
						}
						else if (window.Slope == Slope.Increasing)
						{
							if (!(window.MaxValue < capturedLocals3 + capturedLocals2.depthThreshold))
							{
								if (capturedLocals2.isPixelWet(pixel, startTimeWindowTerm))
								{
									capturedLocals2.writeValue(pixel, startTimeWindowTerm);
									break;
								}
								if (capturedLocals2.isPixelWet(pixel, startTimeWindowTerm + 1))
								{
									capturedLocals2.writeValue(pixel, startTimeWindowTerm + 1);
									break;
								}
								if (capturedLocals2.isPixelWet(pixel, window.StopTime))
								{
									int ordinal = startTimeWindowTerm;
									int stopTime2 = window.StopTime;
									while (true)
									{
										int stopTime22 = (int)Math.Round((double)(ordinal + stopTime2) / 2.0);
										if (stopTime22 == ordinal || stopTime22 == stopTime2)
										{
											break;
										}
										bool capturedLocals4 = capturedLocals2.isPixelWet(pixel, stopTime22);
										bool capturedLocals5 = capturedLocals2.isPixelWet(pixel, stopTime22 + 1);
										if (!capturedLocals4 && capturedLocals5)
										{
											capturedLocals2.writeValue(pixel, stopTime22 + 1);
											return;
										}
										if (!capturedLocals4)
										{
											ordinal = stopTime22;
										}
										else
										{
											if (!capturedLocals4)
											{
												throw new NotImplementedException();
											}
											stopTime2 = stopTime22;
										}
									}
								}
							}
						}
						else
						{
							if (window.Slope != Slope.Indeterminate)
							{
								throw new Exception("Slope type unrecognized.");
							}
							if (!(window.MaxValue < capturedLocals3 + capturedLocals2.depthThreshold))
							{
								bool condition = false;
								int stopTime = window.StopTime;
								for (int i = startTimeWindowTerm; i <= stopTime; i++)
								{
									if (capturedLocals2.isPixelWet(pixel, i))
									{
										capturedLocals2.writeValue(pixel, i);
										condition = true;
										break;
									}
								}
								if (condition)
								{
									break;
								}
							}
						}
					}
				}
			}
		}));
	}

	private void ParseWindowBackward<T>(WindowSeries series, int startTimeIndex, float depthThreshold, Func<T, float> getElev, Func<T, int, bool> isPixelWet, Action<T, int> writeValue, IEnumerable<T> items)
	{
		_Closure_0024__24_002D0<T> capturedLocals = default;
		_Closure_0024__24_002D0<T> capturedLocals2 = new _Closure_0024__24_002D0<T>(capturedLocals);
		capturedLocals2.series = series;
		capturedLocals2.startTimeIndex = startTimeIndex;
		capturedLocals2.depthThreshold = depthThreshold;
		capturedLocals2.getElev = getElev;
		capturedLocals2.isPixelWet = isPixelWet;
		capturedLocals2.writeValue = writeValue;
		System.Threading.Tasks.Parallel.ForEach(items, checked([SpecialName] (T pixel) =>
		{
			float capturedLocals3 = capturedLocals2.getElev(pixel);
			if (capturedLocals3 != -9999f)
			{
				foreach (Window reverse in capturedLocals2.series.Windows.AsEnumerable().Reverse())
				{
					if (capturedLocals2.isPixelWet(pixel, reverse.StopTime))
					{
						capturedLocals2.writeValue(pixel, reverse.StopTime);
						break;
					}
					if (reverse.StopTime >= capturedLocals2.startTimeIndex)
					{
						int startTimeReverseTerm = Math.Max(reverse.StartTime, capturedLocals2.startTimeIndex);
						if (reverse.Slope == Slope.Increasing)
						{
							if (capturedLocals2.isPixelWet(pixel, reverse.StopTime))
							{
								capturedLocals2.writeValue(pixel, reverse.StopTime);
								break;
							}
						}
						else if (reverse.Slope == Slope.Decreasing)
						{
							if (!(reverse.MaxValue < capturedLocals3 + capturedLocals2.depthThreshold) && capturedLocals2.isPixelWet(pixel, startTimeReverseTerm))
							{
								if (capturedLocals2.isPixelWet(pixel, reverse.StopTime))
								{
									capturedLocals2.writeValue(pixel, reverse.StopTime);
									break;
								}
								if (capturedLocals2.isPixelWet(pixel, reverse.StopTime - 1))
								{
									capturedLocals2.writeValue(pixel, reverse.StopTime - 1);
									break;
								}
								int ordinal = startTimeReverseTerm;
								int stopTime2 = reverse.StopTime;
								while (true)
								{
									int stopTime22 = (int)Math.Round((double)(ordinal + stopTime2) / 2.0);
									if (stopTime22 == ordinal || stopTime22 == stopTime2)
									{
										break;
									}
									bool capturedLocals4 = capturedLocals2.isPixelWet(pixel, stopTime22);
									bool capturedLocals5 = capturedLocals2.isPixelWet(pixel, stopTime22 + 1);
									if (capturedLocals4 && !capturedLocals5)
									{
										capturedLocals2.writeValue(pixel, stopTime22 + 1);
										return;
									}
									if (!capturedLocals4)
									{
										stopTime2 = stopTime22;
									}
									else
									{
										if (!capturedLocals4)
										{
											throw new NotImplementedException();
										}
										ordinal = stopTime22;
									}
								}
							}
						}
						else
						{
							if (reverse.Slope != Slope.Indeterminate)
							{
								throw new Exception("Slope type unrecognized.");
							}
							if (!(reverse.MaxValue < capturedLocals3 + capturedLocals2.depthThreshold))
							{
								bool condition = false;
								int stopTime = reverse.StopTime;
								int ordinal2 = startTimeReverseTerm;
								for (int i = stopTime; i >= ordinal2; i += -1)
								{
									if (capturedLocals2.isPixelWet(pixel, i))
									{
										capturedLocals2.writeValue(pixel, i);
										condition = true;
										break;
									}
								}
								if (condition)
								{
									break;
								}
							}
						}
					}
				}
			}
		}));
	}
}
