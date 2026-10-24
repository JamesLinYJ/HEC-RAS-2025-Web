using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.Rasters;
using H5Assist.Caching.VB;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Mapping;
using RasMapperLib.Render;
using TiffAssist;
using Utility;
using Utility.Extensions;
using Utility.Progress;

namespace RasMapperLib;

public class MapProcessingEngine
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__23_002D0
	{
		public ProgressReporter fileReporter;

		public int lambdaT;

		public string progressSuffix;

		public _Closure_0024__23_002D3 outerClosure;

		public _Closure_0024__23_002D0(_Closure_0024__23_002D0 other)
		{
			if (other != null)
			{
				fileReporter = other.fileReporter;
				lambdaT = other.lambdaT;
				progressSuffix = other.progressSuffix;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(ProgressReporter ob, int p)
		{
			fileReporter.ReportMessage("File " + checked(lambdaT + 1) + " of " + outerClosure.outerClosure.mapProcessingEngine._terrain.RasterFileCount + ": " + p + "% processed " + progressSuffix);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__23_002D1
	{
		public TileProcessingEngine tpe;

		public _Closure_0024__23_002D4 outerClosure;

		public _Closure_0024__23_002D1(_Closure_0024__23_002D1 other)
		{
			if (other != null)
			{
				tpe = other.tpe;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int n)
		{
			_Closure_0024__23_002D2 localValue = default;
			_Closure_0024__23_002D2 capturedLocals = new _Closure_0024__23_002D2(localValue);
			capturedLocals.outerClosure = this;
			if (outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._reporter.CancelRequested)
			{
				return;
			}
			tpe.WaitUntilBuffersDeplete();
			capturedLocals.tileRasterM = outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._terrain.RasterFilePlainTile(outerClosure.outerClosure.lambdaT, 0, n);
			if (!outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._terrain.TileHasData(outerClosure.outerClosure.lambdaT, 0, n) || !outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._geometry.Overlaps(capturedLocals.tileRasterM))
			{
				tpe.QueueNoDataTile(n);
				return;
			}
			capturedLocals.nLoc = n;
			capturedLocals.terrainValues = outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._getReusableBuffer();
			Task task = new Task([SpecialName] () =>
			{
				object fTRLock = capturedLocals.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._FTRLock;
				ObjectFlowControl.CheckForSyncLockOnValueType(fTRLock);
				bool lockTaken = false;
				try
				{
					Monitor.Enter(fTRLock, ref lockTaken);
					capturedLocals.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._terrain.ReadMaskedTile(capturedLocals.outerClosure.outerClosure.outerClosure.lambdaT, 0, capturedLocals.nLoc, ref capturedLocals.terrainValues, capturedLocals.outerClosure.outerClosure.outerClosure.outerClosure.h5r, capturedLocals.outerClosure.outerClosure.ftr, RuntimeHelpers.GetObjectValue(capturedLocals.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._h5Lock));
				}
				finally
				{
					if (lockTaken)
					{
						Monitor.Exit(fTRLock);
					}
				}
			});
			capturedLocals.mps = outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._tlMapPoints.Value;
			Task task2 = new Task([SpecialName] () =>
			{
				capturedLocals.mps.ComputeForRaster(capturedLocals.tileRasterM);
			});
			capturedLocals.floatValues = null;
			Task task3 = new Task([SpecialName] () =>
			{
				if (capturedLocals.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.computer == null)
				{
					object h5Lock = capturedLocals.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._h5Lock;
					ObjectFlowControl.CheckForSyncLockOnValueType(h5Lock);
					bool lockTaken = false;
					try
					{
						Monitor.Enter(h5Lock, ref lockTaken);
						capturedLocals.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._map.Results.ComputeSwitch(capturedLocals.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._map, capturedLocals.mps, capturedLocals.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._map.ProfileIndex, capturedLocals.terrainValues, capturedLocals.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._cache, ref capturedLocals.floatValues);
						return;
					}
					finally
					{
						if (lockTaken)
						{
							Monitor.Exit(h5Lock);
						}
					}
				}
				capturedLocals.floatValues = capturedLocals.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.computer(capturedLocals.mps, capturedLocals.terrainValues);
			});
			task.Start();
			task2.Start();
			task.Wait();
			task2.Wait();
			task3.Start();
			task3.Wait();
			outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._returnReusableBuffer(capturedLocals.terrainValues);
			tpe.QueueTileForWriting(n, capturedLocals.floatValues);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__23_002D2
	{
		public int nLoc;

		public float[] terrainValues;

		public RASGeometryMapPoints mps;

		public RasterM tileRasterM;

		public float[] floatValues;

		public _Closure_0024__23_002D1 outerClosure;

		public _Closure_0024__23_002D2(_Closure_0024__23_002D2 other)
		{
			if (other != null)
			{
				nLoc = other.nLoc;
				terrainValues = other.terrainValues;
				mps = other.mps;
				tileRasterM = other.tileRasterM;
				floatValues = other.floatValues;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__2()
		{
			object fTRLock = outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._FTRLock;
			ObjectFlowControl.CheckForSyncLockOnValueType(fTRLock);
			bool lockTaken = false;
			try
			{
				Monitor.Enter(fTRLock, ref lockTaken);
				outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._terrain.ReadMaskedTile(outerClosure.outerClosure.outerClosure.lambdaT, 0, nLoc, ref terrainValues, outerClosure.outerClosure.outerClosure.outerClosure.h5r, outerClosure.outerClosure.ftr, RuntimeHelpers.GetObjectValue(outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._h5Lock));
			}
			finally
			{
				if (lockTaken)
				{
					Monitor.Exit(fTRLock);
				}
			}
		}

		[SpecialName]
		internal void _Lambda_0024__3()
		{
			mps.ComputeForRaster(tileRasterM);
		}

		[SpecialName]
		internal void _Lambda_0024__4()
		{
			if (outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.computer == null)
			{
				object h5Lock = outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._h5Lock;
				ObjectFlowControl.CheckForSyncLockOnValueType(h5Lock);
				bool lockTaken = false;
				try
				{
					Monitor.Enter(h5Lock, ref lockTaken);
					outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._map.Results.ComputeSwitch(outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._map, mps, outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._map.ProfileIndex, terrainValues, outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._cache, ref floatValues);
					return;
				}
				finally
				{
					if (lockTaken)
					{
						Monitor.Exit(h5Lock);
					}
				}
			}
			floatValues = outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.computer(mps, terrainValues);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__23_002D3
	{
		public H5Reader h5r;

		public _Closure_0024__23_002D5 outerClosure;

		public _Closure_0024__23_002D3(_Closure_0024__23_002D3 other)
		{
			if (other != null)
			{
				h5r = other.h5r;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__23_002D4
	{
		public FloatTiffReader ftr;

		public _Closure_0024__23_002D0 outerClosure;

		public _Closure_0024__23_002D4(_Closure_0024__23_002D4 other)
		{
			if (other != null)
			{
				ftr = other.ftr;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__23_002D5
	{
		public Func<RASGeometryMapPoints, float[], float[]> computer;

		public MapProcessingEngine mapProcessingEngine;

		public _Closure_0024__23_002D5(_Closure_0024__23_002D5 other)
		{
			if (other != null)
			{
				computer = other.computer;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__24_002D0
	{
		public ProgressReporter fileReporter;

		public int lambdaT;

		public string progressSuffix;

		public bool mapHasData;

		public _Closure_0024__24_002D3 outerClosure;

		public _Closure_0024__24_002D0(_Closure_0024__24_002D0 other)
		{
			if (other != null)
			{
				fileReporter = other.fileReporter;
				lambdaT = other.lambdaT;
				progressSuffix = other.progressSuffix;
				mapHasData = other.mapHasData;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__2(ProgressReporter ob, int p)
		{
			fileReporter.ReportMessage("File " + checked(lambdaT + 1) + " of " + outerClosure.outerClosure.mapProcessingEngine._terrain.RasterFileCount + ": " + p + "% processed " + progressSuffix);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__24_002D1
	{
		public TileProcessingEngine scratchTPE;

		public _Closure_0024__24_002D5 outerClosure;

		public _Closure_0024__24_002D1(_Closure_0024__24_002D1 other)
		{
			if (other != null)
			{
				scratchTPE = other.scratchTPE;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__3(int n)
		{
			_Closure_0024__24_002D2 localValue = default;
			_Closure_0024__24_002D2 capturedLocals = new _Closure_0024__24_002D2(localValue);
			capturedLocals.outerClosure = this;
			if (outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._reporter.CancelRequested)
			{
				return;
			}
			scratchTPE.WaitUntilBuffersDeplete();
			capturedLocals.tileRasterM = outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._terrain.RasterFilePlainTile(outerClosure.outerClosure.outerClosure.lambdaT, 0, n);
			if (!outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._terrain.TileHasData(outerClosure.outerClosure.outerClosure.lambdaT, 0, n) || !outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._geometry.Overlaps(capturedLocals.tileRasterM))
			{
				scratchTPE.QueueNoDataTile(n);
				return;
			}
			capturedLocals.nLoc = n;
			capturedLocals.terrainValues = outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._getReusableBuffer();
			Task task = new Task([SpecialName] () =>
			{
				object fTRLock = capturedLocals.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._FTRLock;
				ObjectFlowControl.CheckForSyncLockOnValueType(fTRLock);
				bool lockTaken = false;
				try
				{
					Monitor.Enter(fTRLock, ref lockTaken);
					capturedLocals.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._terrain.ReadMaskedTile(capturedLocals.outerClosure.outerClosure.outerClosure.outerClosure.lambdaT, 0, capturedLocals.nLoc, ref capturedLocals.terrainValues, capturedLocals.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.h5r, capturedLocals.outerClosure.outerClosure.outerClosure.ftr, RuntimeHelpers.GetObjectValue(capturedLocals.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._h5Lock));
				}
				finally
				{
					if (lockTaken)
					{
						Monitor.Exit(fTRLock);
					}
				}
			});
			capturedLocals.mps = outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._tlMapPoints.Value;
			Task task2 = new Task([SpecialName] () =>
			{
				capturedLocals.mps.ComputeForRaster(capturedLocals.tileRasterM);
			});
			capturedLocals.substepOutput = null;
			Task task3 = new Task(checked([SpecialName] () =>
			{
				capturedLocals.substepOutput = capturedLocals.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._getReusableBuffer();
				Buffers.ArraySetAllVB(capturedLocals.substepOutput, -9999f);
				int countOffset = capturedLocals.outerClosure.outerClosure.computers.Count - 1;
				_Closure_0024__24_002D6 localValue2 = default;
				for (int i = 0; i <= countOffset; i++)
				{
					localValue2 = new _Closure_0024__24_002D6(localValue2);
					localValue2.outerClosure = capturedLocals;
					localValue2.floatValues = capturedLocals.outerClosure.outerClosure.computers[i](capturedLocals.mps, capturedLocals.terrainValues);
					Utility.Parallel.SmartFor(capturedLocals.substepOutput.Length, localValue2._Lambda_0024__7, 256);
					capturedLocals.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._returnReusableBuffer(localValue2.floatValues);
				}
			}));
			task.Start();
			task2.Start();
			task.Wait();
			task2.Wait();
			task3.Start();
			task3.Wait();
			outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._returnReusableBuffer(capturedLocals.terrainValues);
			if (!outerClosure.outerClosure.outerClosure.mapHasData)
			{
				bool determineIfMapHasData = outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine.DetermineIfMapHasData(capturedLocals.substepOutput);
				if (determineIfMapHasData)
				{
					outerClosure.outerClosure.outerClosure.mapHasData = determineIfMapHasData;
				}
			}
			scratchTPE.QueueTileForWriting(n, capturedLocals.substepOutput);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__24_002D2
	{
		public int nLoc;

		public float[] terrainValues;

		public RASGeometryMapPoints mps;

		public RasterM tileRasterM;

		public float[] substepOutput;

		public _Closure_0024__24_002D1 outerClosure;

		public _Closure_0024__24_002D2(_Closure_0024__24_002D2 other)
		{
			if (other != null)
			{
				nLoc = other.nLoc;
				terrainValues = other.terrainValues;
				mps = other.mps;
				tileRasterM = other.tileRasterM;
				substepOutput = other.substepOutput;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__4()
		{
			object fTRLock = outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._FTRLock;
			ObjectFlowControl.CheckForSyncLockOnValueType(fTRLock);
			bool lockTaken = false;
			try
			{
				Monitor.Enter(fTRLock, ref lockTaken);
				outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._terrain.ReadMaskedTile(outerClosure.outerClosure.outerClosure.outerClosure.lambdaT, 0, nLoc, ref terrainValues, outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.h5r, outerClosure.outerClosure.outerClosure.ftr, RuntimeHelpers.GetObjectValue(outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._h5Lock));
			}
			finally
			{
				if (lockTaken)
				{
					Monitor.Exit(fTRLock);
				}
			}
		}

		[SpecialName]
		internal void _Lambda_0024__5()
		{
			mps.ComputeForRaster(tileRasterM);
		}

		[SpecialName]
		internal void _Lambda_0024__6()
		{
			substepOutput = outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._getReusableBuffer();
			Buffers.ArraySetAllVB(substepOutput, -9999f);
			checked
			{
				int countOffset = outerClosure.outerClosure.computers.Count - 1;
				_Closure_0024__24_002D6 localValue = default;
				for (int i = 0; i <= countOffset; i++)
				{
					localValue = new _Closure_0024__24_002D6(localValue);
					localValue.outerClosure = this;
					localValue.floatValues = outerClosure.outerClosure.computers[i](mps, terrainValues);
					Utility.Parallel.SmartFor(substepOutput.Length, localValue._Lambda_0024__7, 256);
					outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.mapProcessingEngine._returnReusableBuffer(localValue.floatValues);
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__24_002D3
	{
		public H5Reader h5r;

		public _Closure_0024__24_002D7 outerClosure;

		public _Closure_0024__24_002D3(_Closure_0024__24_002D3 other)
		{
			if (other != null)
			{
				h5r = other.h5r;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__24_002D4
	{
		public FloatTiffReader ftr;

		public _Closure_0024__24_002D0 outerClosure;

		public _Closure_0024__24_002D4(_Closure_0024__24_002D4 other)
		{
			if (other != null)
			{
				ftr = other.ftr;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__24_002D5
	{
		public List<Func<RASGeometryMapPoints, float[], float[]>> computers;

		public _Closure_0024__24_002D4 outerClosure;

		public _Closure_0024__24_002D5(_Closure_0024__24_002D5 other)
		{
			if (other != null)
			{
				computers = other.computers;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__24_002D6
	{
		public float[] floatValues;

		public _Closure_0024__24_002D2 outerClosure;

		public _Closure_0024__24_002D6(_Closure_0024__24_002D6 other)
		{
			if (other != null)
			{
				floatValues = other.floatValues;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__7(int start, int fin)
		{
			for (int i = start; i <= fin; i = checked(i + 1))
			{
				float floatValue = floatValues[i];
				float substepOutput = outerClosure.substepOutput[i];
				if (floatValue != -9999f)
				{
					if (substepOutput == -9999f)
					{
						outerClosure.substepOutput[i] = floatValue;
					}
					else
					{
						outerClosure.substepOutput[i] = outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.maxMinFunc(floatValue, substepOutput);
					}
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__24_002D7
	{
		public Func<float, float, float> maxMinFunc;

		public MapProcessingEngine mapProcessingEngine;

		public _Closure_0024__24_002D7(_Closure_0024__24_002D7 other)
		{
			if (other != null)
			{
				maxMinFunc = other.maxMinFunc;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__24_002D8
	{
		public float[] floatValues;

		public _Closure_0024__24_002D9 outerClosure;

		public _Closure_0024__24_002D8(_Closure_0024__24_002D8 other)
		{
			if (other != null)
			{
				floatValues = other.floatValues;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__8(int start, int fin)
		{
			for (int i = start; i <= fin; i = checked(i + 1))
			{
				float floatValue = floatValues[i];
				float buf = outerClosure.buf[i];
				if (floatValue != -9999f)
				{
					if (buf == -9999f)
					{
						outerClosure.buf[i] = floatValue;
					}
					else
					{
						outerClosure.buf[i] = outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.maxMinFunc(floatValue, buf);
					}
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__24_002D9
	{
		public float[] buf;

		public _Closure_0024__24_002D4 outerClosure;

		public _Closure_0024__24_002D9(_Closure_0024__24_002D9 other)
		{
			if (other != null)
			{
				buf = other.buf;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__25_002D0
	{
		public float[] floatValues;

		public bool mhd;

		public _Closure_0024__25_002D0(_Closure_0024__25_002D0 other)
		{
			if (other != null)
			{
				floatValues = other.floatValues;
				mhd = other.mhd;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (floatValues[i] != -9999f)
				{
					mhd = true;
					break;
				}
			}
		}
	}

	private RASResultsMap _map;

	private RASGeometry _geometry;

	private RASResults _result;

	private TerrainLayer _terrain;

	private string _baseFilename;

	private ProgressReporter _reporter;

	private int _prevPixelCutoff;

	private ThreadLocal<RASGeometryMapPoints> _tlMapPoints;

	private object _bufLock;

	private object _h5Lock;

	private object _overlayLock;

	private Stack<float[]> _unusedBuffers;

	private Func<float[]> _getReusableBuffer;

	private Action<float[]> _returnReusableBuffer;

	private CacheCollection _cache;

	private TiffMetadata<float> _mapMetadata;

	private object _FTRLock;

	private int[] _tilesPerTerrainFile;

	private float[] _fileProgress;

	public static void StoreMap(RASResultsMap map, TerrainLayer terrain, string mapBaseFilename, ProgressReporter reporter)
	{
		MapProcessingEngine mapProcessingEngine = new MapProcessingEngine(map, terrain, mapBaseFilename, reporter);
		mapProcessingEngine.PreprocessData();
		if (map.MapType == MapTypes.DepthTimesVelocity || map.MapType == MapTypes.DepthTimesVelocitySquared || map.MapType == MapTypes.EnergyDepth || map.MapType == MapTypes.EnergyElevation || map.MapType == MapTypes.Froude)
		{
			if (map.ProfileIndex == int.MaxValue)
			{
				mapProcessingEngine.StoreMapBruteForceMaxMin(useMax: true);
			}
			else if (map.ProfileIndex == int.MinValue)
			{
				mapProcessingEngine.StoreMapBruteForceMaxMin(useMax: false);
			}
			else
			{
				mapProcessingEngine.StoreMap();
			}
		}
		else
		{
			mapProcessingEngine.StoreMap();
		}
		mapProcessingEngine.Dispose();
	}

	public MapProcessingEngine(RASResultsMap map, TerrainLayer terrain, string mapBaseFilename, ProgressReporter reporter)
	{
		_bufLock = RuntimeHelpers.GetObjectValue(new object());
		_h5Lock = RuntimeHelpers.GetObjectValue(new object());
		_overlayLock = RuntimeHelpers.GetObjectValue(new object());
		_FTRLock = RuntimeHelpers.GetObjectValue(new object());
		_map = map;
		_result = map.Results;
		_geometry = _result.Geometry;
		_terrain = terrain;
		_baseFilename = mapBaseFilename;
		_reporter = reporter;
	}

	private void PreprocessData()
	{
		_geometry.D2FlowArea.FeatureTable();
		_geometry.XSInterpolationSurface.FeatureTable();
		_geometry.StorageArea.FeatureTable();
		_prevPixelCutoff = MeshFV2D.PixelRenderingCutoff;
		MeshFV2D.PixelRenderingCutoff = 0;
		_tlMapPoints = new ThreadLocal<RASGeometryMapPoints>([SpecialName] () => new RASGeometryMapPoints(_geometry));
		_ = _tlMapPoints.Value;
		foreach (MeshFV2D meshe in _geometry.D2FlowArea.Meshes())
		{
			SharedData.GetMeshCellSpatialIndex(meshe);
			SharedData.GetMeshCellAreas(meshe);
			meshe.EnsureFaceInvLengths();
		}
		_terrain.Level0StitchTIN();
		_mapMetadata = new TiffMetadata<float>(-9999f, RoundTo.Sixteenths);
		_mapMetadata.Artist = "HEC-RAS";
		_mapMetadata.DataType = _map.MapType.Description;
		_mapMetadata.DataUnits = _map.MapUnits;
		_mapMetadata.ImageDescription = _map.Description;
		_mapMetadata.Rounding = _map.OutputModeRounding;
		int rasterFilesTileCount = _terrain.RasterFilesTileCount;
		_tilesPerTerrainFile = _terrain.RasterFilesIndividualTileCount;
		checked
		{
			_fileProgress = new float[_tilesPerTerrainFile.Count() - 1 + 1];
			int count = _fileProgress.Count() - 1;
			for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
			{
				_fileProgress[intervalIndex] = (float)((double)_tilesPerTerrainFile[intervalIndex] / (double)rasterFilesTileCount);
			}
			_unusedBuffers = new Stack<float[]>();
			_getReusableBuffer = [SpecialName] () =>
			{
				object bufLock = _bufLock;
				ObjectFlowControl.CheckForSyncLockOnValueType(bufLock);
				bool lockTaken = false;
				try
				{
					Monitor.Enter(bufLock, ref lockTaken);
					if (_unusedBuffers.Count > 0)
					{
						return _unusedBuffers.Pop();
					}
					return new float[65536];
				}
				finally
				{
					if (lockTaken)
					{
						Monitor.Exit(bufLock);
					}
				}
			};
			_returnReusableBuffer = [SpecialName] (float[] buf) =>
			{
				object bufLock = _bufLock;
				ObjectFlowControl.CheckForSyncLockOnValueType(bufLock);
				bool lockTaken = false;
				try
				{
					Monitor.Enter(bufLock, ref lockTaken);
					if (_unusedBuffers.Count <= 256)
					{
						_unusedBuffers.Push(buf);
					}
				}
				finally
				{
					if (lockTaken)
					{
						Monitor.Exit(bufLock);
					}
				}
			};
			_cache = SharedData.GetMapCache(_map);
		}
	}

	private void Dispose()
	{
		_tlMapPoints?.Dispose();
		_unusedBuffers = null;
		_getReusableBuffer = null;
		_returnReusableBuffer = null;
		_cache.Clear();
		_reporter.ReportProgress(100);
		MeshFV2D.PixelRenderingCutoff = _prevPixelCutoff;
	}

	public void StoreMap()
	{
		_Closure_0024__23_002D5 capturedLocals = new _Closure_0024__23_002D5(null);
		capturedLocals.mapProcessingEngine = this;
		Stopwatch stopwatch = Stopwatch.StartNew();
		List<string> stringList = new List<string>();
		capturedLocals.computer = Renderer.GetSimplifiedComputer(_cache, _map.ProfileIndex, _map.MapType, _map.Results, _getReusableBuffer, _returnReusableBuffer);
		_Closure_0024__23_002D3 capturedLocals2 = new _Closure_0024__23_002D3(null);
		capturedLocals2.outerClosure = capturedLocals;
		capturedLocals2.h5r = new H5Reader(_terrain.SourceFilename);
		checked
		{
			try
			{
				int count = _terrain.RasterFileCount - 1;
				_Closure_0024__23_002D0 capturedLocals3 = default;
				_Closure_0024__23_002D4 capturedLocals4 = default;
				_Closure_0024__23_002D1 capturedLocals5 = default;
				for (int i = 0; i <= count; i++)
				{
					capturedLocals3 = new _Closure_0024__23_002D0(capturedLocals3);
					capturedLocals3.outerClosure = capturedLocals2;
					if (_reporter.CancelRequested)
					{
						break;
					}
					string ensureMapFilename = EnsureMapFilename(i);
					_reporter.ReportMessage("Processing Terrain File " + (i + 1) + " of " + _terrain.RasterFileCount);
					int progressStartingPoint = GetProgressStartingPoint(i);
					capturedLocals3.fileReporter = _reporter.WithProgressModifier(progressStartingPoint, _fileProgress[i]);
					capturedLocals3.lambdaT = i;
					capturedLocals3.progressSuffix = "(Map Generation)";
					capturedLocals3.fileReporter.ProgressReported += capturedLocals3._Lambda_0024__0;
					bool rasterHasData = false;
					capturedLocals4 = new _Closure_0024__23_002D4(capturedLocals4);
					capturedLocals4.outerClosure = capturedLocals3;
					capturedLocals4.ftr = new FloatTiffReader(_terrain.get_RasterFilename(i));
					int imageWidth;
					int imageHeight;
					Extent extent;
					try
					{
						imageWidth = capturedLocals4.ftr.GetImageWidth(0);
						imageHeight = capturedLocals4.ftr.GetImageHeight(0);
						capturedLocals4.ftr.GetTileWidth(0);
						capturedLocals4.ftr.GetTileHeight(0);
						int numTiles = capturedLocals4.ftr.GetNumTiles(0);
						extent = _terrain.get_RasterFileExtent(i);
						using FloatTiffWriter floatTiffWriter = new FloatTiffWriter(ensureMapFilename, imageWidth, imageHeight, ComputeStats: true, _mapMetadata, "8");
						capturedLocals5 = new _Closure_0024__23_002D1(capturedLocals5);
						capturedLocals5.outerClosure = capturedLocals4;
						ProgressReporter prog = capturedLocals5.outerClosure.outerClosure.fileReporter.WithProgressModifier(0, 0.6f);
						capturedLocals5.tpe = new TileProcessingEngine(floatTiffWriter, _returnReusableBuffer, prog);
						capturedLocals5.tpe.Start();
						System.Threading.Tasks.Parallel.For(0, numTiles, GetDefaultParallelOptions(), capturedLocals5._Lambda_0024__1);
						capturedLocals5.tpe.WaitForExit();
						rasterHasData = floatTiffWriter.RasterHasData();
					}
					finally
					{
						if (capturedLocals4.ftr != null)
						{
							((IDisposable)capturedLocals4.ftr).Dispose();
						}
					}
					if (_reporter.CancelRequested)
					{
						stringList.Add(ensureMapFilename);
						break;
					}
					if (rasterHasData)
					{
						stringList.Add(ensureMapFilename);
						ProgressReporter reporter = capturedLocals3.fileReporter.WithProgressModifier(60, 0.2f);
						capturedLocals3.progressSuffix = "(Computing Histogram)";
						InterpolatedLayer.AddHistogram(ensureMapFilename, null, reporter);
						if (_reporter.CancelRequested)
						{
							break;
						}
						capturedLocals3.fileReporter.WithProgressModifier(80, 0.2f);
						capturedLocals3.progressSuffix = "(Adding Overlays)";
						object overlayLock = _overlayLock;
						ObjectFlowControl.CheckForSyncLockOnValueType(overlayLock);
						bool lockTaken = false;
						try
						{
							Monitor.Enter(overlayLock, ref lockTaken);
							InterpolatedLayer.AddSRSGeorefAddo(ensureMapFilename, imageWidth, imageHeight, extent.MinX, extent.MaxY, _terrain.get_RasterFileCellSize(i), UseNearest: false, null, null, null);
						}
						finally
						{
							if (lockTaken)
							{
								Monitor.Exit(overlayLock);
							}
						}
					}
					else
					{
						capturedLocals3.fileReporter.ReportMessage("File " + ensureMapFilename.InSingleQuotes() + " has zero data, removing...");
						File.Delete(ensureMapFilename);
					}
					capturedLocals3.fileReporter.ReportProgress(100);
				}
			}
			finally
			{
				if (capturedLocals2.h5r != null)
				{
					((IDisposable)capturedLocals2.h5r).Dispose();
				}
			}
			if (_reporter.CancelRequested)
			{
				_reporter.ReportMessage("Cancelling...");
				foreach (string textValue in stringList)
				{
					try
					{
						if (File.Exists(textValue))
						{
							File.Delete(textValue);
						}
					}
					catch (Exception ex)
					{
						ProjectData.SetProjectError(ex);
						Exception ex2 = ex;
						ProjectData.ClearProjectError();
					}
				}
				try
				{
					if (File.Exists(_map.VRTFilename))
					{
						File.Delete(_map.VRTFilename);
					}
					return;
				}
				catch (Exception ex3)
				{
					ProjectData.SetProjectError(ex3);
					Exception ex4 = ex3;
					ProjectData.ClearProjectError();
					return;
				}
			}
			if (stringList.Count > 0)
			{
				InterpolatedLayer.CreateVRT(stringList.ToArray(), _map.VRTFilename, null);
				stringList.Add(_map.VRTFilename);
			}
			else
			{
				_map.ErrorMessage = "No files were created (all cells were NoData)";
			}
			_reporter.ReportProgress(100);
			stopwatch.Stop();
		}
	}

	public void StoreMapBruteForceMaxMin(bool useMax)
	{
		_Closure_0024__24_002D7 capturedLocals = new _Closure_0024__24_002D7(null);
		capturedLocals.mapProcessingEngine = this;
		capturedLocals.maxMinFunc = null;
		if (useMax)
		{
			capturedLocals.maxMinFunc = [SpecialName] (float a, float b) => Math.Max(a, b);
		}
		else
		{
			capturedLocals.maxMinFunc = [SpecialName] (float a, float b) => Math.Min(a, b);
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		List<string> stringList = new List<string>();
		_Closure_0024__24_002D3 capturedLocals2 = new _Closure_0024__24_002D3(null);
		capturedLocals2.outerClosure = capturedLocals;
		capturedLocals2.h5r = new H5Reader(_terrain.SourceFilename);
		checked
		{
			try
			{
				int count = _terrain.RasterFileCount - 1;
				_Closure_0024__24_002D0 capturedLocals3 = default;
				_Closure_0024__24_002D4 capturedLocals4 = default;
				_Closure_0024__24_002D5 capturedLocals5 = default;
				_Closure_0024__24_002D1 capturedLocals6 = default;
				_Closure_0024__24_002D9 capturedLocals7 = default;
				_Closure_0024__24_002D8 capturedLocals8 = default;
				for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
				{
					capturedLocals3 = new _Closure_0024__24_002D0(capturedLocals3);
					capturedLocals3.outerClosure = capturedLocals2;
					if (_reporter.CancelRequested)
					{
						break;
					}
					string ensureMapFilename = EnsureMapFilename(intervalIndex);
					_reporter.ReportMessage("Processing Terrain File " + (intervalIndex + 1) + " of " + _terrain.RasterFileCount);
					int progressStartingPoint = GetProgressStartingPoint(intervalIndex);
					capturedLocals3.fileReporter = _reporter.WithProgressModifier(progressStartingPoint, _fileProgress[intervalIndex]);
					capturedLocals3.lambdaT = intervalIndex;
					capturedLocals3.progressSuffix = "(Map Generation)";
					capturedLocals3.fileReporter.ProgressReported += capturedLocals3._Lambda_0024__2;
					capturedLocals3.mapHasData = false;
					capturedLocals4 = new _Closure_0024__24_002D4(capturedLocals4);
					capturedLocals4.outerClosure = capturedLocals3;
					capturedLocals4.ftr = new FloatTiffReader(_terrain.get_RasterFilename(intervalIndex));
					int imageWidth;
					int imageHeight;
					Extent extent;
					try
					{
						imageWidth = capturedLocals4.ftr.GetImageWidth(0);
						imageHeight = capturedLocals4.ftr.GetImageHeight(0);
						int numTiles = capturedLocals4.ftr.GetNumTiles(0);
						extent = _terrain.get_RasterFileExtent(intervalIndex);
						List<string> stringListList = new List<string>();
						int ordinal = 50;
						if (!Environment.Is64BitProcess)
						{
							ordinal = 10;
						}
						int profileCountTerm = (int)Math.Ceiling((double)_result.ProfileCount / (double)ordinal);
						ProgressReporter progressReporter = capturedLocals4.outerClosure.fileReporter.WithProgressModifier(0, 0.7f);
						int count2 = profileCountTerm - 1;
						for (int ordinal2 = 0; ordinal2 <= count2; ordinal2++)
						{
							capturedLocals5 = new _Closure_0024__24_002D5(capturedLocals5);
							capturedLocals5.outerClosure = capturedLocals4;
							if (_reporter.CancelRequested)
							{
								break;
							}
							capturedLocals5.computers = new List<Func<RASGeometryMapPoints, float[], float[]>>();
							int ordinal3 = ordinal2 * ordinal;
							int profileCountTerm2 = Math.Min(ordinal3 + ordinal - 1, _result.ProfileCount - 1);
							int count3 = profileCountTerm2;
							for (int ordinal4 = ordinal3; ordinal4 <= count3; ordinal4++)
							{
								capturedLocals5.computers.Add(Renderer.GetSimplifiedComputer(_cache, ordinal4, _map.MapType, _result, _getReusableBuffer, _returnReusableBuffer));
							}
							if (_reporter.CancelRequested)
							{
								break;
							}
							string tempFileName = Path.GetTempFileName();
							stringListList.Add(tempFileName);
							ProgressReporter prog = progressReporter.WithProgressModifier((int)Math.Round(100.0 * ((double)ordinal2 / (double)profileCountTerm)), (float)(1.0 / (double)profileCountTerm));
							using FloatTiffWriter tw = new FloatTiffWriter(tempFileName, imageWidth, imageHeight, ComputeStats: true, _mapMetadata, "8");
							capturedLocals6 = new _Closure_0024__24_002D1(capturedLocals6);
							capturedLocals6.outerClosure = capturedLocals5;
							capturedLocals6.scratchTPE = new TileProcessingEngine(tw, _returnReusableBuffer, prog);
							capturedLocals6.scratchTPE.Start();
							System.Threading.Tasks.Parallel.For(0, numTiles, GetDefaultParallelOptions(), capturedLocals6._Lambda_0024__3);
							capturedLocals6.scratchTPE.WaitForExit();
						}
						if (_reporter.CancelRequested)
						{
							foreach (string textValue in stringListList)
							{
								try
								{
									if (File.Exists(textValue))
									{
										File.Delete(textValue);
									}
								}
								catch (Exception ex)
								{
									ProjectData.SetProjectError(ex);
									Exception ex2 = ex;
									ProjectData.ClearProjectError();
								}
							}
							break;
						}
						using FloatTiffWriter tw2 = new FloatTiffWriter(ensureMapFilename, imageWidth, imageHeight, ComputeStats: true, _mapMetadata, "8");
						List<FloatTiffReader> floatTiffReaders = new List<FloatTiffReader>();
						foreach (string textValue2 in stringListList)
						{
							floatTiffReaders.Add(new FloatTiffReader(textValue2));
						}
						ProgressReporter prog2 = capturedLocals4.outerClosure.fileReporter.WithProgressModifier(70, 0.2f);
						TileProcessingEngine tileProcessingEngine = new TileProcessingEngine(tw2, _returnReusableBuffer, prog2);
						tileProcessingEngine.Start();
						int count4 = numTiles - 1;
						for (int ordinal5 = 0; ordinal5 <= count4; ordinal5++)
						{
							capturedLocals7 = new _Closure_0024__24_002D9(capturedLocals7);
							capturedLocals7.outerClosure = capturedLocals4;
							if (_reporter.CancelRequested)
							{
								break;
							}
							RasterM raster = _terrain.RasterFilePlainTile(capturedLocals7.outerClosure.outerClosure.lambdaT, 0, ordinal5);
							if (!_terrain.TileHasData(capturedLocals7.outerClosure.outerClosure.lambdaT, 0, ordinal5) || !_geometry.Overlaps(raster))
							{
								tileProcessingEngine.QueueNoDataTile(ordinal5);
								continue;
							}
							capturedLocals7.buf = _getReusableBuffer();
							Buffers.ArraySetAllVB(capturedLocals7.buf, -9999f);
							int count5 = floatTiffReaders.Count - 1;
							for (int intervalIndex2 = 0; intervalIndex2 <= count5; intervalIndex2++)
							{
								capturedLocals8 = new _Closure_0024__24_002D8(capturedLocals8);
								capturedLocals8.outerClosure = capturedLocals7;
								capturedLocals8.floatValues = null;
								floatTiffReaders[intervalIndex2].GetTile(0, ordinal5, ref capturedLocals8.floatValues);
								Utility.Parallel.SmartFor(capturedLocals8.outerClosure.buf.Length, capturedLocals8._Lambda_0024__8, 256);
							}
							tileProcessingEngine.QueueTileForWriting(ordinal5, capturedLocals7.buf);
						}
						foreach (FloatTiffReader floatTiffReader in floatTiffReaders)
						{
							floatTiffReader.Dispose();
						}
						foreach (string textValue3 in stringListList)
						{
							try
							{
								File.Delete(textValue3);
							}
							catch (Exception ex3)
							{
								ProjectData.SetProjectError(ex3);
								Exception ex4 = ex3;
								ProjectData.ClearProjectError();
							}
						}
						tileProcessingEngine.WaitForExit();
					}
					finally
					{
						if (capturedLocals4.ftr != null)
						{
							((IDisposable)capturedLocals4.ftr).Dispose();
						}
					}
					if (_reporter.CancelRequested)
					{
						stringList.Add(ensureMapFilename);
						break;
					}
					if (capturedLocals3.mapHasData)
					{
						stringList.Add(ensureMapFilename);
						ProgressReporter reporter = capturedLocals3.fileReporter.WithProgressModifier(90, 0.05f);
						capturedLocals3.progressSuffix = "(Computing Histogram)";
						InterpolatedLayer.AddHistogram(ensureMapFilename, null, reporter);
						if (_reporter.CancelRequested)
						{
							break;
						}
						capturedLocals3.fileReporter.WithProgressModifier(95, 0.05f);
						capturedLocals3.progressSuffix = "(Adding Overlays)";
						object overlayLock = _overlayLock;
						ObjectFlowControl.CheckForSyncLockOnValueType(overlayLock);
						bool lockTaken = false;
						try
						{
							Monitor.Enter(overlayLock, ref lockTaken);
							InterpolatedLayer.AddSRSGeorefAddo(ensureMapFilename, imageWidth, imageHeight, extent.MinX, extent.MaxY, _terrain.get_RasterFileCellSize(intervalIndex), UseNearest: false, null, null, null);
						}
						finally
						{
							if (lockTaken)
							{
								Monitor.Exit(overlayLock);
							}
						}
					}
					else
					{
						File.Delete(ensureMapFilename);
					}
					capturedLocals3.fileReporter.ReportProgress(100);
				}
			}
			finally
			{
				if (capturedLocals2.h5r != null)
				{
					((IDisposable)capturedLocals2.h5r).Dispose();
				}
			}
			if (_reporter.CancelRequested)
			{
				_reporter.ReportMessage("Cancelling...");
				foreach (string textValue4 in stringList)
				{
					try
					{
						if (File.Exists(textValue4))
						{
							File.Delete(textValue4);
						}
					}
					catch (Exception ex5)
					{
						ProjectData.SetProjectError(ex5);
						Exception ex6 = ex5;
						ProjectData.ClearProjectError();
					}
				}
				try
				{
					if (File.Exists(_map.VRTFilename))
					{
						File.Delete(_map.VRTFilename);
					}
					return;
				}
				catch (Exception ex7)
				{
					ProjectData.SetProjectError(ex7);
					Exception ex8 = ex7;
					ProjectData.ClearProjectError();
					return;
				}
			}
			if (stringList.Count > 0)
			{
				InterpolatedLayer.CreateVRT(stringList.ToArray(), _map.VRTFilename, null);
				stringList.Add(_map.VRTFilename);
			}
			else
			{
				_map.ErrorMessage = "No files were created (all cells were NoData)";
			}
			_reporter.ReportProgress(100);
			stopwatch.Stop();
		}
	}

	private bool DetermineIfMapHasData(float[] values)
	{
		_Closure_0024__25_002D0 capturedLocals = default;
		_Closure_0024__25_002D0 capturedLocals2 = new _Closure_0024__25_002D0(capturedLocals);
		capturedLocals2.floatValues = values;
		capturedLocals2.mhd = false;
		Utility.Parallel.SmartFor(capturedLocals2.floatValues.Count(), [SpecialName] (int start, int finish) =>
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (capturedLocals2.floatValues[i] != -9999f)
				{
					capturedLocals2.mhd = true;
					break;
				}
			}
		}, 256);
		return capturedLocals2.mhd;
	}

	private bool DetermineIfMapHasData(float[] values, int tileWidth, int tileHeight, int validWidth, int validHeight)
	{
		checked
		{
			int count = validHeight - 1;
			for (int i = 0; i <= count; i++)
			{
				int tileWidthTerm = i * tileWidth;
				int count2 = validWidth - 1;
				for (int j = 0; j <= count2; j++)
				{
					if (values[tileWidthTerm + j] != -9999f)
					{
						return true;
					}
				}
			}
			return false;
		}
	}

	private string EnsureMapFilename(int t)
	{
		string baseFilenameGet_RasterBaseNameTerm = _baseFilename + "." + _terrain.get_RasterBaseName(t) + ".tif";
		string directoryName = Path.GetDirectoryName(baseFilenameGet_RasterBaseNameTerm);
		if (!Directory.Exists(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		if (File.Exists(baseFilenameGet_RasterBaseNameTerm))
		{
			File.Delete(baseFilenameGet_RasterBaseNameTerm);
		}
		return baseFilenameGet_RasterBaseNameTerm;
	}

	private int GetProgressStartingPoint(int t)
	{
		int progressStartingPoint = 0;
		checked
		{
			int count = t - 1;
			for (int i = 0; i <= count; i++)
			{
				progressStartingPoint += (int)Math.Round(_fileProgress[i] * 100f);
			}
			return progressStartingPoint;
		}
	}

	private static ParallelOptions GetDefaultParallelOptions()
	{
		return new ParallelOptions
		{
			MaxDegreeOfParallelism = 32
		};
	}
}
