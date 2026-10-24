using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using Hec.Dss;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using Utility;
using Utility.Extensions;

namespace RasMapperLib;

public class DSSRasterLayer : Layer, IAnimatingLayer
{
	protected List<KeyValuePair<DateTime, List<string>>> _layerInfo;

	protected static readonly Proj4Projection _albersProj = new Proj4Projection("+proj=aea +lat_0=23 +lat_1=29.5 +lat_2=42.5 +lon_0=-96");

	protected Projection _localProj;

	private string _dssPath;


	private int _currentProfileIndex;

	private List<string> _availableProfiles;

	public override Extent Extent
	{
		get
		{
			EnsureMetadataLoaded();
			return _extent;
		}
	}

	public virtual bool TreatZeroAsNoData { get; }

	public bool CanProvideMinMax { get; }

	public bool CanAnimate { get; }

	public bool IsProfile
	{
		get
		{
			if (_currentProfileIndex >= 0)
			{
				return _currentProfileIndex < ProfileCount;
			}
			return false;
		}
	}

	public int ProfileCount
	{
		get
		{
			if (AvailableProfiles == null)
			{
				return 0;
			}
			return AvailableProfiles.Count;
		}
	}

	public bool IsMinProfile { get; }

	public bool IsMaxProfile { get; }

	public bool IsNullProfile => !IsProfile;

	public int CurrentProfileIndex
	{
		get
		{
			return _currentProfileIndex;
		}
		set
		{
			_currentProfileIndex = value;
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

	public IList<string> AvailableProfiles
	{
		get
		{
			EnsureMetadataLoaded();
			return _availableProfiles;
		}
	}

	public DSSRasterLayer()
	{
		TreatZeroAsNoData = false;
		_currentProfileIndex = -1;
		CanProvideMinMax = false;
		CanAnimate = true;
		IsMinProfile = false;
		IsMaxProfile = false;
	}

	public DSSRasterLayer(string filename)
		: base("DSS Raster Layer", filename)
	{
		TreatZeroAsNoData = false;
		_currentProfileIndex = -1;
		CanProvideMinMax = false;
		CanAnimate = true;
		IsMinProfile = false;
		IsMaxProfile = false;
	}

	public DSSRasterLayer(string layername, string filename)
		: base(layername, filename)
	{
		TreatZeroAsNoData = false;
		_currentProfileIndex = -1;
		CanProvideMinMax = false;
		CanAnimate = true;
		IsMinProfile = false;
		IsMaxProfile = false;
	}

	public DSSRasterLayer(string lyrName, string srcFilename, string pathname)
		: base(lyrName, srcFilename)
	{
		TreatZeroAsNoData = false;
		_currentProfileIndex = -1;
		CanProvideMinMax = false;
		CanAnimate = true;
		IsMinProfile = false;
		IsMaxProfile = false;
		_dssPath = CleanOutTimeParts(pathname);
	}

	public void EnsureMetadataLoaded()
	{
		if (_availableProfiles != null)
		{
			return;
		}
		if (!SourceFileExists)
		{
			_availableProfiles = new List<string>();
			_extent = Extent.NullExtent;
			return;
		}
		try
		{
			_layerInfo = ProcessFileInfo();
			_availableProfiles = _layerInfo.Select([SpecialName] (KeyValuePair<DateTime, List<string>> lyr) => lyr.Key.ToString()).ToList();
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			_layerInfo = new List<KeyValuePair<DateTime, List<string>>>();
			_availableProfiles = new List<string>();
			ErrorMessage = ex2.Message;
			ProjectData.ClearProjectError();
		}
	}

	public static string CleanOutTimeParts(string dssPath)
	{
		string result;
		try
		{
			if (!dssPath.EndsWith("/"))
			{
				dssPath += "/";
			}
			result = new DssPath(dssPath)
			{
				Dpart = "",
				Epart = ""
			}.FullPath;
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			result = dssPath;
			ProjectData.ClearProjectError();
		}
		return result;
	}

	private List<KeyValuePair<DateTime, List<string>>> ProcessFileInfo()
	{
		if (!string.IsNullOrEmpty(_dssPath))
		{
			_dssPath = CleanOutTimeParts(_dssPath);
		}
		Extent extent = null;
		checked
		{
			using DssReader dssReader = new DssReader(SourceFilename);
			string[] pathList = dssReader.GetCatalog().Paths.Select([SpecialName] (DssPath dssPath2) => dssPath2.FullPath).ToArray();
			DssPath pathFilter = new DssPath(_dssPath);
			List<string> filterByPart = PathAssist.FilterByPart(pathList, pathFilter);
			Dictionary<DateTime, List<string>> stringListByDateTime = new Dictionary<DateTime, List<string>>();
			int count = filterByPart.Count - 1;
			for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
			{
				string filterByPart2 = filterByPart[intervalIndex];
				DssPath dssPath = new DssPath(filterByPart2);
				DSS.ConvertFromDSSDateTime(dssPath.Dpart);
				DateTime key = DSS.ConvertFromDSSDateTime(dssPath.Epart);
				List<string> localValue = null;
				if (!stringListByDateTime.TryGetValue(key, out localValue))
				{
					localValue = (stringListByDateTime[key] = new List<string>());
				}
				localValue.Add(filterByPart2);
				Grid grid = dssReader.GetGrid(filterByPart2, retrieveData: false);
				Extent nativeExtent = GetNativeExtent(grid);
				extent = ((extent != null) ? extent.UnionXY(nativeExtent) : nativeExtent);
				if (_localProj == null && !string.IsNullOrEmpty(grid.SRSDefinition))
				{
					string wellKnownText = SanitizeFauwazProjection(grid.SRSDefinition);
					_localProj = new WKTProjection(wellKnownText);
				}
			}
			if (_localProj == null)
			{
				_localProj = _albersProj;
			}
			if (Extent.IsValid(extent) && SharedData.SRSProjection != null)
			{
				_extent = Converter.Convert(Projector.GetBoundingBox(Converter.Convert(extent), _localProj, SharedData.SRSProjection));
			}
			else
			{
				_extent = extent;
			}
			if (stringListByDateTime.Count > 0)
			{
				_currentProfileIndex = 0;
			}
			else
			{
				_currentProfileIndex = -1;
			}
			return stringListByDateTime.OrderBy([SpecialName] (KeyValuePair<DateTime, List<string>> kvp) => kvp.Key).ToList();
		}
	}

	public static string SanitizeFauwazProjection(string srs)
	{
		return srs.Replace("UNIT[\"M\",0.0]", "UNIT[\"Meter\",1.0]");
	}

	public bool Resample(RasterM raster, int pfIdx, ref float[] values)
	{
		EnsureMetadataLoaded();
		if (pfIdx < 0 || pfIdx >= AvailableProfiles.Count)
		{
			return false;
		}
		if (!raster.Overlaps(Extent))
		{
			return false;
		}
		Buffers.ArraySetAllVB(ref values, -9999f, raster.CellCount);
		using (DssReader dssReader = new DssReader(SourceFilename))
		{
			List<string> layerInfoPfIdxTerm = _layerInfo[pfIdx].Value;
			if (layerInfoPfIdxTerm == null)
			{
				return false;
			}
			float[] values2 = null;
			foreach (string textValue in layerInfoPfIdxTerm)
			{
				Grid grid = dssReader.GetGrid(textValue, retrieveData: true);
				_ = grid.Units;
				RasterM nativeRaster = GetNativeRaster(grid);
				ReadInvertRatioDSSGrid(grid, ref values2, GetConversionFactor(grid));
				if (_localProj != null && SharedData.SRSProjection != null)
				{
					new ReprojectionTransformer(SharedData.SRSProjection, _localProj);
					Converter.Convert(nativeRaster);
					Converter.Convert(raster);
					throw new NotImplementedException();
				}
				RasterOperations.Resample(Converter.Convert(nativeRaster), args: new ResampleArgs(Converter.Convert(raster)), sourceData: values2, destData: values);
			}
		}
		if (TreatZeroAsNoData)
		{
			int count = values.Length;
			for (int i = 0; i <= count; i = checked(i + 1))
			{
				if (values[i] == 0f)
				{
					values[i] = -9999f;
				}
			}
		}
		return true;
	}

	public bool TryReadNativeTile(int pfIdx, ref float[] values)
	{
		EnsureMetadataLoaded();
		using DssReader dssr = new DssReader(SourceFilename);
		return TryReadNativeTile(dssr, pfIdx, ref values);
	}

	public bool TryReadNativeTile(DssReader dssr, int pfIdx, ref float[] values)
	{
		List<string> layerInfoPfIdxTerm = _layerInfo[pfIdx].Value;
		if (layerInfoPfIdxTerm.IsNullOrEmpty())
		{
			return false;
		}
		string firstOrDefault = layerInfoPfIdxTerm.FirstOrDefault();
		if (string.IsNullOrEmpty(firstOrDefault))
		{
			return false;
		}
		ReadInvertRatioDSSGrid(dssr.GetGrid(firstOrDefault, retrieveData: true), ref values, 1f);
		return true;
	}

	public bool TryReadRatioTile(DssReader dssr, int pfIdx, float ratio, ref float[] values)
	{
		List<string> layerInfoPfIdxTerm = _layerInfo[pfIdx].Value;
		if (layerInfoPfIdxTerm.IsNullOrEmpty())
		{
			return false;
		}
		string firstOrDefault = layerInfoPfIdxTerm.FirstOrDefault();
		if (string.IsNullOrEmpty(firstOrDefault))
		{
			return false;
		}
		ReadInvertRatioDSSGrid(dssr.GetGrid(firstOrDefault, retrieveData: true), ref values, ratio);
		return true;
	}

	public static void ReadInvertRatioDSSGrid(Grid grid, ref float[] values, float ratio)
	{
		float[] data = grid.Data;
		Buffers.EnsureArraySize(ref values, data.Length);
		checked
		{
			int count = grid.NumberOfCellsY - 1;
			for (int i = 0; i <= count; i++)
			{
				int numberOfCellsYGridTerm = grid.NumberOfCellsY - i - 1;
				int count2 = grid.NumberOfCellsX - 1;
				for (int j = 0; j <= count2; j++)
				{
					int numberOfCellsXGridTerm = numberOfCellsYGridTerm * grid.NumberOfCellsX + j;
					int numberOfCellsXGridTerm2 = i * grid.NumberOfCellsX + j;
					float computed = data[numberOfCellsXGridTerm];
					if (computed == -9999f || computed == float.MinValue)
					{
						values[numberOfCellsXGridTerm2] = -9999f;
					}
					else
					{
						values[numberOfCellsXGridTerm2] = computed * ratio;
					}
				}
			}
		}
	}

	protected virtual float GetConversionFactor(Grid sampleGrid)
	{
		return 1f;
	}

	public List<DateTime> TryGetDatetimes()
	{
		EnsureMetadataLoaded();
		return _layerInfo.Select([SpecialName] (KeyValuePair<DateTime, List<string>> lyr) => lyr.Key).ToList();
	}

	public static Extent GetNativeExtent(Grid grid)
	{
		float cellSize = grid.CellSize;
		int numberOfCellsX = grid.NumberOfCellsX;
		int numberOfCellsY = grid.NumberOfCellsY;
		float lowerLeftCellXTimesCellSize = (float)grid.LowerLeftCellX * cellSize;
		checked
		{
			float lowerLeftCellXGridTerm = (float)(grid.LowerLeftCellX + numberOfCellsX) * cellSize;
			float lowerLeftCellYTimesCellSize = (float)grid.LowerLeftCellY * cellSize;
			float lowerLeftCellYGridTerm = (float)(grid.LowerLeftCellY + numberOfCellsY) * cellSize;
			return new Extent(lowerLeftCellXGridTerm, lowerLeftCellXTimesCellSize, lowerLeftCellYGridTerm, lowerLeftCellYTimesCellSize);
		}
	}

	public static RasterM GetNativeRaster(Grid grid)
	{
		int numberOfCellsX = grid.NumberOfCellsX;
		int numberOfCellsY = grid.NumberOfCellsY;
		return new RasterM(numberOfCellsY, numberOfCellsX, GetNativeExtent(grid));
	}

	public void TrySetProfile(string profile)
	{
		checked
		{
			int count = AvailableProfiles.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (profile.RoughlyEquals(AvailableProfiles[i]))
				{
					CurrentProfileIndex = i;
					break;
				}
			}
		}
	}

	void IAnimatingLayer.TrySetProfile(string profile)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TrySetProfile
		this.TrySetProfile(profile);
	}

	public void SetToMaxProfile()
	{
		CurrentProfileIndex = -1;
	}

	void IAnimatingLayer.SetToMaxProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetToMaxProfile
		this.SetToMaxProfile();
	}

	public void SetToMinProfile()
	{
		CurrentProfileIndex = -1;
	}

	void IAnimatingLayer.SetToMinProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetToMinProfile
		this.SetToMinProfile();
	}
}
