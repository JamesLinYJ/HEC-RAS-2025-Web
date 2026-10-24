using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using H5Assist.VB;
using RasMapperLib.Mapping;
using Utility;

namespace RasMapperLib;

public class FinalNValueLayer : InterpolatedLayer, IGeometryLayer
{
	[CompilerGenerated]
private sealed class _Closure_0024__30_002D0
	{
		public LandCoverLayer landCover;

		public FinalNValueLayer finalNValueLayer;

		public _Closure_0024__30_002D0(_Closure_0024__30_002D0 other)
		{
			if (other != null)
			{
				landCover = other.landCover;
			}
		}

		[SpecialName]
		internal float[] _Lambda_0024__0(float[] mann, RasterM tile)
		{
			RASD2FlowArea d2FlowArea = finalNValueLayer.Geometry.D2FlowArea;
			checked
			{
				int count = d2FlowArea.FeatureCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					MeshFV2D meshFV2D = d2FlowArea.Mesh(i);
					if (meshFV2D.IsComplete())
					{
						float manningsN = d2FlowArea.GetManningsN(i);
						if (!(manningsN < 0f) && manningsN != float.MaxValue && !float.IsNaN(manningsN))
						{
							meshFV2D.Perimeter.RasterizeOnto(tile, mann, manningsN);
						}
					}
				}
				int[] values = null;
				landCover.Rasterize(tile, ref values);
				int[] rasterize = finalNValueLayer.Geometry.LandCoverRegions.Rasterize(tile);
				int lengthOffset = rasterize.Length - 1;
				for (int j = 0; j <= lengthOffset; j++)
				{
					int landCoverID = values[j];
					int regionFID = rasterize[j];
					float manningsFromTable = finalNValueLayer.GetManningsFromTable(landCoverID, regionFID);
					if (manningsFromTable != float.MaxValue)
					{
						mann[j] = manningsFromTable;
					}
				}
				return mann;
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__33_002D0
	{
		public BitArray mappedPixels;

		public int[] rfID;

		public int[] lcID;

		public float[] nv;

		public FinalNValueLayer finalNValueLayer;

		public _Closure_0024__33_002D0(_Closure_0024__33_002D0 other)
		{
			if (other != null)
			{
				mappedPixels = other.mappedPixels;
				rfID = other.rfID;
				lcID = other.lcID;
				nv = other.nv;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int fin)
		{
			for (int i = start; i <= fin; i = checked(i + 1))
			{
				if (mappedPixels != null && mappedPixels[i])
				{
					rfID[i] = -2;
					lcID[i] = 0;
					continue;
				}
				float manningsFromTable = finalNValueLayer.GetManningsFromTable(lcID[i], rfID[i]);
				if (manningsFromTable != float.MaxValue && manningsFromTable != -9999f && manningsFromTable >= 0f)
				{
					nv[i] = manningsFromTable;
				}
			}
		}
	}

	public const string HDFGroupName = "Geometry/Land Cover (Manning's n)";

	public const string RegionAttributeName = "Manning's n Region Data Date";

	public const string ValuesAttributeName = "Manning's n Values Data Date";

	private const int CrossSectionSentinal = -2;

	private DateTime _dataReadTime;

	private string[] _landCoverNames;

	private float[] _lcBaseManningsN;

	private float[,] _regionManningsN;

	private LandCoverLayer _landCoverLayer;

	protected Bitmap _bmp;

	private int[] _regionFIDRaster;

	private int[] _landCoverID;

	private int[] _landCoverIDtoRow;

	[CompilerGenerated]
	private RASGeometry _Geometry;

	public const string sLandCoverManningsN = "Final n Values";

	public RASGeometry Geometry
	{
		[CompilerGenerated]
		get
		{
			return _Geometry;
		}
	}

	public override bool ForwardEditRequestsToParent { get; }

	public override string DefaultLayerName => "Final n Values";

	public override Extent Extent => Geometry.Extent;
string IGeometryLayer.HDFGroupName=> "Geometry/Land Cover (Manning's n)";

	public FinalNValueLayer(RASGeometry geometry)
		: base("Final n Values", geometry.SourceFilename)
	{
		_dataReadTime = DateTime.MinValue;
		_landCoverNames = null;
		_lcBaseManningsN = null;
		_regionManningsN = null;
		_landCoverLayer = null;
		_bmp = null;
		_landCoverIDtoRow = new int[256];
		ForwardEditRequestsToParent = true;
		_Geometry = geometry;
	}

	public override bool LayerHasData(H5Reader hr = null)
	{
		return Geometry.NValueLayer?.SourceFileExists ?? false;
	}

	public void HDFLoadFeatureTable(H5Reader hr)
	{
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	public void HDFSaveFeatureTable(H5Writer hw)
	{
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	private void Export(string tiffFilename, double cellSize)
	{
		_Closure_0024__30_002D0 capturedLocals = default;
		_Closure_0024__30_002D0 capturedLocals2 = new _Closure_0024__30_002D0(capturedLocals);
		capturedLocals2.finalNValueLayer = this;
		capturedLocals2.landCover = Geometry.NValueLayer;
		if (capturedLocals2.landCover == null)
		{
			MessageBox.Show("No associated land-cover layer found");
			return;
		}
		if (!capturedLocals2.landCover.AllSourceFilesExist())
		{
			MessageBox.Show("Associated land-cover has missing files.");
			return;
		}
		EnsureCurrentData();
		Func<float[], RasterM, float[]> getTileValues = checked([SpecialName] (float[] mann, RasterM tile) =>
		{
			RASD2FlowArea d2FlowArea = capturedLocals2.finalNValueLayer.Geometry.D2FlowArea;
			int count = d2FlowArea.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				MeshFV2D meshFV2D = d2FlowArea.Mesh(i);
				if (meshFV2D.IsComplete())
				{
					float manningsN = d2FlowArea.GetManningsN(i);
					if (!(manningsN < 0f) && manningsN != float.MaxValue && !float.IsNaN(manningsN))
					{
						meshFV2D.Perimeter.RasterizeOnto(tile, mann, manningsN);
					}
				}
			}
			int[] values = null;
			capturedLocals2.landCover.Rasterize(tile, ref values);
			int[] rasterize = capturedLocals2.finalNValueLayer.Geometry.LandCoverRegions.Rasterize(tile);
			int count2 = rasterize.Length - 1;
			for (int j = 0; j <= count2; j++)
			{
				int landCoverID = values[j];
				int regionFID = rasterize[j];
				float manningsFromTable = capturedLocals2.finalNValueLayer.GetManningsFromTable(landCoverID, regionFID);
				if (manningsFromTable != float.MaxValue)
				{
					mann[j] = manningsFromTable;
				}
			}
			return mann;
		});
		capturedLocals2.landCover.Export(tiffFilename, cellSize, getTileValues);
	}

	public void EnsureCurrentData()
	{
		if (!File.Exists(SourceFilename))
		{
			return;
		}
		if (_landCoverLayer == Geometry.NValueLayer && DateTime.Compare(_dataReadTime, SourceFileDate) == 0)
		{
			bool? lcBaseManningsNSequenceEqualTerm = _lcBaseManningsN?.SequenceEqual(_Geometry.LandCoverRegions.GetOverriddenNValues());
			if (((!lcBaseManningsNSequenceEqualTerm) ?? lcBaseManningsNSequenceEqualTerm) != true)
			{
				return;
			}
		}
		_dataReadTime = SourceFileDate;
		_landCoverLayer = Geometry.NValueLayer;
		if (_landCoverLayer == null)
		{
			_landCoverNames = null;
			_lcBaseManningsN = null;
			_regionManningsN = null;
			return;
		}
		_landCoverNames = _landCoverLayer.GetNames();
		_lcBaseManningsN = _landCoverLayer.TryGetManningsOverrideValues();
		if (_landCoverNames == null)
		{
			return;
		}
		checked
		{
			if (_lcBaseManningsN == null || _lcBaseManningsN.Count() != _landCoverNames.Count())
			{
				_lcBaseManningsN = new float[_landCoverNames.Count() - 1 + 1];
			}
			Func<float, bool> func = [SpecialName] (float v) => (!(v <= 0f) && v != float.MaxValue && !float.IsNaN(v)) ? true : false;
			string[] lcNames = Geometry.LandCoverRegions.GetLCNames();
			float[] overriddenNValues = Geometry.LandCoverRegions.GetOverriddenNValues();
			int featureCount = Geometry.LandCoverRegions.FeatureCount();
			if (lcNames == null)
			{
				lcNames = new string[0];
			}
			if (overriddenNValues == null)
			{
				overriddenNValues = new float[0];
			}
			_regionManningsN = new float[_landCoverNames.Count() - 1 + 1, featureCount - 1 + 1];
			int count = _landCoverNames.Count() - 1;
			for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
			{
				int count2 = featureCount - 1;
				for (int intervalIndex2 = 0; intervalIndex2 <= count2; intervalIndex2++)
				{
					_regionManningsN[intervalIndex, intervalIndex2] = float.MaxValue;
				}
			}
			if (lcNames != null)
			{
				int count3 = lcNames.Count() - 1;
				for (int intervalIndex3 = 0; intervalIndex3 <= count3; intervalIndex3++)
				{
					string lcName = lcNames[intervalIndex3];
					float overriddenNValue = overriddenNValues[intervalIndex3];
					int count4 = _landCoverNames.Count() - 1;
					for (int intervalIndex4 = 0; intervalIndex4 <= count4; intervalIndex4++)
					{
						string b = _landCoverNames[intervalIndex4];
						if (!string.Equals(lcName, b, StringComparison.CurrentCultureIgnoreCase))
						{
							continue;
						}
						if (func(overriddenNValue))
						{
							_lcBaseManningsN[intervalIndex4] = overriddenNValue;
						}
						int count5 = featureCount - 1;
						for (int intervalIndex5 = 0; intervalIndex5 <= count5; intervalIndex5++)
						{
							Dictionary<string, float> landCoverMapping = Geometry.LandCoverRegions.GetLandCoverMapping(intervalIndex5);
							float regionManningsN = 0f;
							if (landCoverMapping.TryGetValue(lcName, out regionManningsN) && func(regionManningsN))
							{
								_regionManningsN[intervalIndex4, intervalIndex5] = regionManningsN;
							}
						}
						break;
					}
				}
			}
			LandCoverLayer nValueLayer = Geometry.NValueLayer;
			int[] iDs = nValueLayer.GetIDs();
			string[] names = nValueLayer.GetNames();
			Buffers.ArraySetAllVB(_landCoverIDtoRow, -1);
			int count6 = iDs.Count() - 1;
			for (int intervalIndex6 = 0; intervalIndex6 <= count6; intervalIndex6++)
			{
				int iDsIndex = iDs[intervalIndex6];
				string namesTerm = names[intervalIndex6];
				int landCoverIDtoRow = Array.IndexOf(_landCoverNames, namesTerm);
				_landCoverIDtoRow[iDsIndex] = landCoverIDtoRow;
			}
		}
	}

	public float GetManningsFromTable(int landCoverID, int regionFID)
	{
		if (landCoverID == 0 && regionFID == -1)
		{
			return -9999f;
		}
		int landCoverIDtoRow = _landCoverIDtoRow[landCoverID];
		if (_regionManningsN == null)
		{
			return _lcBaseManningsN[landCoverIDtoRow];
		}
		if (landCoverIDtoRow == -1)
		{
			return -9999f;
		}
		if (regionFID < 0)
		{
			return _lcBaseManningsN[landCoverIDtoRow];
		}
		float lcBaseManningsNLandCoverIDtoRowTerm = _regionManningsN[landCoverIDtoRow, regionFID];
		if (lcBaseManningsNLandCoverIDtoRowTerm == float.MaxValue || lcBaseManningsNLandCoverIDtoRowTerm <= 0f)
		{
			lcBaseManningsNLandCoverIDtoRowTerm = _lcBaseManningsN[landCoverIDtoRow];
		}
		return lcBaseManningsNLandCoverIDtoRowTerm;
	}

	private void ResampleInternal(LandCoverLayer landCover, RasterM raster, ref float[] nValues, ref int[] landCoverIDRaster, ref int[] regionFIDRaster)
	{
		_Closure_0024__33_002D0 capturedLocals = default;
		_Closure_0024__33_002D0 capturedLocals2 = new _Closure_0024__33_002D0(capturedLocals);
		capturedLocals2.finalNValueLayer = this;
		Buffers.EnsureArraySize(ref nValues, raster.CellCount);
		Buffers.ArraySetAllVB(nValues, -9999f);
		if (!landCover.Rasterize(raster, ref landCoverIDRaster))
		{
			ErrorMessage = "Error resampling associated Land Cover Layer: " + landCover.ErrorMessage;
			return;
		}
		regionFIDRaster = Geometry.LandCoverRegions.Rasterize(raster);
		RASD2FlowArea d2FlowArea = Geometry.D2FlowArea;
		checked
		{
			int count = d2FlowArea.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				MeshFV2D meshFV2D = d2FlowArea.Mesh(i);
				if (meshFV2D.IsComplete())
				{
					float manningsN = d2FlowArea.GetManningsN(i);
					if (!(manningsN < 0f) && manningsN != float.MaxValue && !float.IsNaN(manningsN))
					{
						meshFV2D.Perimeter.RasterizeOnto(raster, nValues, manningsN);
					}
				}
			}
			if (!Geometry.IsResultFile)
			{
				Geometry.XSInterpolationSurface.EnsureInterpolationSurface();
			}
			RASGeometryMapPoints rASGeometryMapPoints = new RASGeometryMapPoints(Geometry);
			rASGeometryMapPoints.ComputeFor1D(raster);
			capturedLocals2.mappedPixels = rASGeometryMapPoints.MappedPixels;
			capturedLocals2.lcID = landCoverIDRaster;
			capturedLocals2.rfID = regionFIDRaster;
			capturedLocals2.nv = nValues;
			Parallel.SmartFor(raster.CellMaxIndex(), [SpecialName] (int start, int fin) =>
			{
				for (int j = start; j <= fin; j++)
				{
					if (capturedLocals2.mappedPixels != null && capturedLocals2.mappedPixels[j])
					{
						capturedLocals2.rfID[j] = -2;
						capturedLocals2.lcID[j] = 0;
					}
					else
					{
						float manningsFromTable = capturedLocals2.finalNValueLayer.GetManningsFromTable(capturedLocals2.lcID[j], capturedLocals2.rfID[j]);
						if (manningsFromTable != float.MaxValue && manningsFromTable != -9999f && manningsFromTable >= 0f)
						{
							capturedLocals2.nv[j] = manningsFromTable;
						}
					}
				}
			});
			RASXS xS = Geometry.XS;
			RASXSInterpolationSurface xSInterpolationSurface = Geometry.XSInterpolationSurface;
			Point2DCollection[] point2DCollections = new Point2DCollection[xS.FeatureCount() - 1 + 1];
			int count2 = xS.FeatureCount() - 1;
			for (int intervalIndex = 0; intervalIndex <= count2; intervalIndex++)
			{
				Point2DCollection manningsN2 = xS.GetManningsN(intervalIndex);
				point2DCollections[intervalIndex] = new Point2DCollection(manningsN2.Count);
				int count3 = manningsN2.Count - 1;
				for (int intervalIndex2 = 0; intervalIndex2 <= count3; intervalIndex2++)
				{
					float x = xS.ComputeM(intervalIndex, manningsN2[intervalIndex2].X);
					point2DCollections[intervalIndex].Add(new Point2D(x, manningsN2[intervalIndex2].Y));
				}
				point2DCollections[intervalIndex].Add(new Point2D(float.MaxValue, 0f));
			}
			foreach (MappedXSIS mappedXSIS in rASGeometryMapPoints.MappedXSISs)
			{
				int iD = mappedXSIS.ID;
				int upstreamXSID = xSInterpolationSurface.GetUpstreamXSID(iD);
				int downstreamXSID = xSInterpolationSurface.GetDownstreamXSID(iD);
				int count4 = mappedXSIS.MapPoints.Count - 1;
				for (int intervalIndex3 = 0; intervalIndex3 <= count4; intervalIndex3++)
				{
					XSISMapPoint xSISMapPoint = mappedXSIS.MapPoints[intervalIndex3];
					if ((double)xSISMapPoint.Z > 0.5)
					{
						Point2DCollection point2DCollection = point2DCollections[upstreamXSID];
						if (point2DCollection.Count == 0)
						{
							continue;
						}
						int count5 = point2DCollection.Count - 1;
						for (int unity = 1; unity <= count5; unity++)
						{
							if (point2DCollection[unity].X > xSISMapPoint.M)
							{
								nValues[xSISMapPoint.Index] = point2DCollection[unity - 1].Y;
								break;
							}
						}
						continue;
					}
					Point2DCollection point2DCollection2 = point2DCollections[downstreamXSID];
					if (point2DCollection2.Count == 0)
					{
						continue;
					}
					int count6 = point2DCollection2.Count - 1;
					for (int unity2 = 1; unity2 <= count6; unity2++)
					{
						if (point2DCollection2[unity2].X > xSISMapPoint.M)
						{
							nValues[xSISMapPoint.Index] = point2DCollection2[unity2 - 1].Y;
							break;
						}
					}
				}
			}
		}
	}

	public override bool Resample(RasterM raster, ref float[] values)
	{
		if (!Geometry.NValueAssociation.HasAssociation)
		{
			ErrorMessage = "* Geometry not associated with a Manning's n Value layer";
			return false;
		}
		if (Geometry.IsEditingData)
		{
			ErrorMessage = "Cannot display Final n Values in Edit Mode";
			return false;
		}
		LandCoverLayer nValueLayer = Geometry.NValueLayer;
		if (nValueLayer == null)
		{
			ErrorMessage = "Error loading associated Manning's n layer";
			return false;
		}
		ErrorMessage = "";
		EnsureCurrentData();
		ResampleInternal(nValueLayer, raster, ref values, ref _landCoverID, ref _regionFIDRaster);
		return true;
	}
}
