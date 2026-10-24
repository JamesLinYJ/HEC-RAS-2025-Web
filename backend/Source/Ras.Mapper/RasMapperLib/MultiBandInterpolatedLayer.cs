using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml;
using Geospatial.GDALAssist;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using Utility.Extensions;
using Utility.Progress;

namespace RasMapperLib;

public class MultiBandInterpolatedLayer : InterpolatedLayer, IAnimatingLayer
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__54_002D0
	{
		public string timeFormat;

		public _Closure_0024__54_002D0(_Closure_0024__54_002D0 other)
		{
			if (other != null)
			{
				timeFormat = other.timeFormat;
			}
		}

		[SpecialName]
		internal string _Lambda_0024__1(Tuple<int, DateTime> tup)
		{
			return tup.Item2.ToString(timeFormat);
		}
	}

	protected List<string> _loadedBands;

	protected int _currentBandIndex;

	protected int _renderedLastWithIndex;

	protected string _subDataset;

	protected string _fullyQualifiedPath;

	private Projection _overrideProjection;

	private List<float[]> _allData;

	private RasterM _dataRaster;

	private Projection _dataProjection;

	public string SubDataset => _subDataset;

	public bool CanProvideMinMax { get; }

	public bool IsMaxProfile { get; }

	public bool IsMinProfile { get; }

	public bool CanAnimate { get; }

	public bool IsNullProfile => _currentBandIndex == -1;

	public bool IsProfile => _currentBandIndex >= 0;

	public int CurrentProfileIndex
	{
		get
		{
			return _currentBandIndex;
		}
		set
		{
			_currentBandIndex = value;
			OnCurrentBandChanged();
		}
	}

	public string CurrentProfileName
	{
		get
		{
			EnsureMetadataLoaded();
			if (_currentBandIndex < 0 || _currentBandIndex >= _loadedBands.Count)
			{
				return "None";
			}
			return _loadedBands[_currentBandIndex];
		}
	}

	public IList<string> AvailableProfiles
	{
		get
		{
			EnsureMetadataLoaded();
			return _loadedBands.ToList();
		}
	}

	public int ProfileCount
	{
		get
		{
			EnsureMetadataLoaded();
			if (_loadedBands == null)
			{
				return 0;
			}
			return _loadedBands.Count;
		}
	}

	public Projection OverrideProjection
	{
		get
		{
			return _overrideProjection;
		}
		set
		{
			_overrideProjection = value;
		}
	}

	protected virtual void OnCurrentBandChanged()
	{
	}

	protected override bool NeedNewBitmap(RasterM raster)
	{
		if (_renderedLastWithIndex != _currentBandIndex)
		{
			return true;
		}
		return base.NeedNewBitmap(raster);
	}

	public MultiBandInterpolatedLayer()
		: base("Multi Band Raster Layer")
	{
		_currentBandIndex = 0;
		_renderedLastWithIndex = -1;
		_subDataset = "";
		CanProvideMinMax = false;
		IsMaxProfile = false;
		IsMinProfile = false;
		CanAnimate = true;
	}

	public MultiBandInterpolatedLayer(string srcFilename)
		: base("Multi Band Raster Layer", srcFilename)
	{
		_currentBandIndex = 0;
		_renderedLastWithIndex = -1;
		_subDataset = "";
		CanProvideMinMax = false;
		IsMaxProfile = false;
		IsMinProfile = false;
		CanAnimate = true;
	}

	public MultiBandInterpolatedLayer(string lyrName, string srcFilename)
		: base(lyrName, srcFilename)
	{
		_currentBandIndex = 0;
		_renderedLastWithIndex = -1;
		_subDataset = "";
		CanProvideMinMax = false;
		IsMaxProfile = false;
		IsMinProfile = false;
		CanAnimate = true;
	}

	public MultiBandInterpolatedLayer(string lyrName, string srcFilename, string subDatasetName)
		: base(lyrName, srcFilename)
	{
		_currentBandIndex = 0;
		_renderedLastWithIndex = -1;
		_subDataset = "";
		CanProvideMinMax = false;
		IsMaxProfile = false;
		IsMinProfile = false;
		CanAnimate = true;
		_subDataset = subDatasetName;
		if (string.IsNullOrEmpty(subDatasetName))
		{
			_fullyQualifiedPath = srcFilename;
		}
		else
		{
			_fullyQualifiedPath = TryGetGDRName(srcFilename, subDatasetName);
		}
	}

	public static string TryGetGDRName(string srcFilename, string subDsName)
	{
		string result;
		if (string.IsNullOrEmpty(subDsName))
		{
			result = srcFilename;
		}
		else if (subDsName.Contains("\""))
		{
			result = subDsName;
		}
		else if (!File.Exists(srcFilename))
		{
			result = srcFilename;
		}
		else
		{
			try
			{
				using GDALRaster gDALRaster = new GDALRaster(srcFilename);
				Dictionary<string, string> subDatasets = gDALRaster.GetSubDatasets();
				foreach (KeyValuePair<string, string> subDataset in subDatasets)
				{
					if (!subDataset.Key.RoughlyEndsWith(subDsName))
					{
						continue;
					}
					result = subDataset.Key;
					goto end_IL_0035;
				}
				result = subDsName;
				end_IL_0035:;
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				result = subDsName;
				ProjectData.ClearProjectError();
			}
		}
		return result;
	}

	public void ResetData()
	{
		_loadedBands = null;
		_allData = null;
		_dataRaster = null;
		_dataProjection = null;
	}

	public virtual void EnsureMetadataLoaded()
	{
		if (_loadedBands != null || !SourceFileExists)
		{
			return;
		}
		checked
		{
			using GDALRaster gDALRaster = new GDALRaster(GetGDRName());
			int bandCount = gDALRaster.GetBandCount();
			_loadedBands = new List<string>();
			string[] metadataItemsFromEachBand = gDALRaster.GetMetadataItemsFromEachBand("Name");
			int bandCount2 = bandCount;
			for (int i = 1; i <= bandCount2; i++)
			{
				string metadataItemsFromEachBand2 = metadataItemsFromEachBand[i - 1];
				if (string.IsNullOrEmpty(metadataItemsFromEachBand2))
				{
					_loadedBands.Add("Band " + i);
				}
				else
				{
					_loadedBands.Add(metadataItemsFromEachBand2);
				}
			}
		}
	}

	public override bool Resample(RasterM raster, ref float[] values)
	{
		return Resample(_currentBandIndex, raster, ref values);
	}

	public virtual bool Resample(int profileIndex, RasterM raster, ref float[] values)
	{
		if (profileIndex < 0 || profileIndex >= ProfileCount)
		{
			return false;
		}
		EnsureMetadataLoaded();
		using GDALRaster gDALRaster = new GDALRaster(GetGDRName());
		gDALRaster.SetOverrideProjection(_overrideProjection);
		_ = SharedData.SRSProjection;
		Converter.Convert(raster);
		throw new NotImplementedException();
	}

	private bool EnsureEVERYTHINGLoaded()
	{
		EnsureMetadataLoaded();
		if (_loadedBands == null)
		{
			return false;
		}
		if (_allData != null && _allData.Count == _loadedBands.Count) return true;
		using GDALRaster source = new GDALRaster(GetGDRName());
		RasterM nativeRaster = Converter.Convert(source.GetNativeRaster());
		Projection projection = _overrideProjection ?? source.GetProjection();
		List<float[]> bands = new List<float[]>(_loadedBands.Count);
		for (int band = 1; band <= _loadedBands.Count; band++) bands.Add(source.ReadBand<float>(band));
		_dataRaster = nativeRaster;
		_dataProjection = projection;
		_allData = bands;
		return true;
	}

	public bool GetTimeSeries(PointM pt, ref float[] values, ref DateTime[] times)
	{
		if (!EnsureEVERYTHINGLoaded())
		{
			return false;
		}
		times = TryGetDatetimes().ToArray();
		if (times.IsNullOrEmpty())
		{
			return false;
		}
		if (_dataRaster == null)
		{
			return false;
		}
		checked
		{
			values = new float[times.Count() - 1 + 1];
			if (_dataProjection == null)
			{
				if (!_dataRaster.Contains(pt))
				{
					return false;
				}
				int cellIndex = _dataRaster.CellIndex(pt);
				int count = values.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					values[i] = _allData[i][cellIndex];
				}
			}
			else
			{
				double[] doubleValues = new double[2] { pt.X, pt.Y };
				Projector.TransformPoints(SharedData.SRSProjection, _dataProjection, doubleValues);
				PointM pointM = new PointM(doubleValues[0], doubleValues[1]);
				if (!_dataRaster.Contains(pointM))
				{
					return false;
				}
				int cellIndex2 = _dataRaster.CellIndex(pointM);
				int count2 = values.Count() - 1;
				for (int j = 0; j <= count2; j++)
				{
					values[j] = _allData[j][cellIndex2];
				}
			}
			return true;
		}
	}

	protected bool TrySaveDateTimes(H5Writer h5w, string folder, ProgressReporter prog)
	{
		List<DateTime> datetimes = TryGetDatetimes();
		if (datetimes == null || datetimes.Count != AvailableProfiles.Count || datetimes.Any([SpecialName] (DateTime dt) => DateTime.Compare(dt, DateTime.MinValue) == 0))
		{
			prog.ReportError("Error processing layer - could not parse timestamps.");
			return false;
		}
		string datasetName = folder + "/Timestamp";
		string[] values = datetimes.Select([SpecialName] (DateTime dt) => dt.ToString("ddMMMyyyy HH:mm:ss.fff")).ToArray();
		h5w.WriteArray(datasetName, values);
		return true;
	}

	private static bool IsConstant(List<DateTime> datetimes)
	{
		if (datetimes == null)
		{
			return false;
		}
		if (datetimes.Count != 2)
		{
			return false;
		}
		if (DateTime.Compare(datetimes[0], DateTime.MinValue) != 0)
		{
			return false;
		}
		if (DateTime.Compare(datetimes[1], DateTime.MaxValue) != 0)
		{
			return false;
		}
		return true;
	}

	public static bool SaveInRangeDateTimes(H5Writer h5w, string folder, RASResults res, List<DateTime> dateTimes, ref List<Tuple<int, DateTime>> inRangeDTs, string timeFormat, ProgressReporter prog)
	{
		_Closure_0024__54_002D0 capturedLocals = default;
		_Closure_0024__54_002D0 capturedLocals2 = new _Closure_0024__54_002D0(capturedLocals);
		capturedLocals2.timeFormat = timeFormat;
		if ((dateTimes == null || dateTimes.Count == 0 || dateTimes.All([SpecialName] (DateTime dt) => DateTime.Compare(dt, DateTime.MinValue) == 0 || DateTime.Compare(dt, DateTime.MaxValue) == 0)) && !IsConstant(dateTimes))
		{
			prog.ReportError("Error processing layer - could not parse timestamps.");
			return false;
		}
		inRangeDTs = new List<Tuple<int, DateTime>>();
		DateTime startTime = res.PlanAttributes.StartTime;
		DateTime endTime = res.PlanAttributes.EndTime;
		SortedList<DateTime, int> sortedList = new SortedList<DateTime, int>();
		checked
		{
			int count = dateTimes.Count - 1;
			for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
			{
				sortedList.Add(dateTimes[intervalIndex], intervalIndex);
			}
			inRangeDTs = new List<Tuple<int, DateTime>>();
			int count2 = sortedList.Count - 1;
			for (int intervalIndex2 = 0; intervalIndex2 <= count2; intervalIndex2++)
			{
				DateTime dateTime = sortedList.Keys[intervalIndex2];
				int itemA = sortedList.Values[intervalIndex2];
				if (DateTime.Compare(dateTime, startTime) >= 0 && DateTime.Compare(dateTime, endTime) <= 0)
				{
					if (inRangeDTs.Count == 0 && intervalIndex2 > 0)
					{
						ListExtensionMethods.Add(inRangeDTs, sortedList.Values[intervalIndex2 - 1], sortedList.Keys[intervalIndex2 - 1]);
					}
					ListExtensionMethods.Add(inRangeDTs, itemA, dateTime);
					if (DateTime.Compare(dateTime, endTime) > 0)
					{
						break;
					}
				}
			}
			if (inRangeDTs.Count == 0)
			{
				prog.ReportError("Plan time range is " + startTime.ToString() + " to " + endTime.ToString() + " but data ranges from " + sortedList.First().ToString() + " to " + sortedList.Last().ToString() + " (Data does not overlap)");
				return false;
			}
			string datasetName = folder + "/Timestamp";
			h5w.WriteArray(datasetName, inRangeDTs.Select([SpecialName] (Tuple<int, DateTime> tup) => tup.Item2.ToString(capturedLocals2.timeFormat)).ToArray());
			return true;
		}
	}

	public static void SaveMetadata(H5Writer h5w, string objectName, string units, string dataTypeName, RasterM raster, Projection proj, ProgressReporter prog)
	{
		h5w.WriteAttribute(objectName, "Units", units);
		h5w.WriteAttribute(objectName, "Data Type", dataTypeName);
		if (proj != null)
		{
			h5w.WriteAttribute(objectName, "Projection", proj.GetWKT());
		}
		HDF5Storage.SaveAttribute(h5w, objectName, raster);
	}

	public bool ReadRaster(GDALRaster gdr, int profileIndex, ref float[] v)
	{
		return false;
	}

	public override string GetGDRName()
	{
		if (string.IsNullOrEmpty(_fullyQualifiedPath))
		{
			_fullyQualifiedPath = TryGetGDRName(SourceFilename, _subDataset);
		}
		return _fullyQualifiedPath;
	}

	public virtual List<DateTime> TryGetDatetimes()
	{
		using GDALRaster gDALRaster = new GDALRaster(GetGDRName());
		return gDALRaster.TryParseDatetimesPerBand().ToList();
	}

	public List<DateTime> GetDatetimesNETCDF()
	{
		List<DateTime> datetimesNETCDF = new List<DateTime>();
		checked
		{
			using (GDALRaster gDALRaster = new GDALRaster(GetGDRName()))
			{
				string metadataItem = gDALRaster.GetMetadataItem("NETCDF_DIM_time_VALUES");
				if (string.IsNullOrEmpty(metadataItem))
				{
					return datetimesNETCDF;
				}
				metadataItem = metadataItem.Replace("{", "");
				metadataItem = metadataItem.Replace("}", "");
				string[] parts = metadataItem.Split(',');
				if (parts.IsNullOrEmpty())
				{
					return datetimesNETCDF;
				}
				DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0);
				int count = parts.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					if (long.TryParse(parts[i], out var result))
					{
						datetimesNETCDF.Add(dateTime.AddMinutes(result));
					}
				}
			}
			return datetimesNETCDF;
		}
	}

	protected override void XMLLoadMetadata(XmlElement xmlLayer)
	{
		base.XMLLoadMetadata(xmlLayer);
		XmlNode xmlNode = xmlLayer.SelectSingleNode("Metadata");
		if (xmlNode != null)
		{
			_currentBandIndex = XML.IntegerAttribute(xmlNode, "BandIndex", -1);
			_subDataset = XML.StringAttribute(xmlNode, "SubDataset");
		}
	}

	protected override void XMLSaveMetadata(XmlDocument doc, XmlElement xmlLayer)
	{
		base.XMLSaveMetadata(doc, xmlLayer);
		XmlElement xmlElement = doc.CreateElement("Metadata");
		xmlLayer.AppendChild(xmlElement);
		xmlElement.SetAttribute("BandIndex", _currentBandIndex.ToString());
		xmlElement.SetAttribute("SubDataset", _subDataset);
	}

	public void TrySetProfile(string profile)
	{
		EnsureMetadataLoaded();
		_currentBandIndex = _loadedBands.IndexOf(profile);
	}

	void IAnimatingLayer.TrySetProfile(string profile)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TrySetProfile
		this.TrySetProfile(profile);
	}

	public void SetToMaxProfile()
	{
		_currentBandIndex = -1;
	}

	void IAnimatingLayer.SetToMaxProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetToMaxProfile
		this.SetToMaxProfile();
	}

	public void SetToMinProfile()
	{
		_currentBandIndex = -1;
	}

	void IAnimatingLayer.SetToMinProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetToMinProfile
		this.SetToMinProfile();
	}

	public void TryInitializeKnownTagsToVRTData()
	{
		if (!SourceFileExists || !SourceFilename.RoughlyEndsWith(".vrt"))
		{
			return;
		}
		checked
		{
			try
			{
				string[] vRTSources = GDALRaster.GetVRTSources(SourceFilename);
				Dictionary<string, DateTime[]> dateTimeValuesByString = new Dictionary<string, DateTime[]>();
				string[] vRTSourcesArray = vRTSources;
				foreach (string vRTSource in vRTSourcesArray)
				{
					if (vRTSource.RoughlyEquals(SourceFilename))
					{
						continue;
					}
					try
					{
						using GDALRaster gDALRaster = new GDALRaster(vRTSource);
						dateTimeValuesByString[vRTSource] = gDALRaster.TryParseDatetimesPerBand();
					}
					catch (Exception projectError)
					{
						ProjectData.SetProjectError(projectError);
						dateTimeValuesByString[vRTSource] = null;
						ProjectData.ClearProjectError();
					}
				}
				using GDALRaster gDALRaster2 = new GDALRaster(SourceFilename);
				int bandCount = gDALRaster2.GetBandCount();
				for (int j = 1; j <= bandCount; j++)
				{
					string subRasterFile = "";
					gDALRaster2.TryIdentifyVRTFileForBand(j, out subRasterFile, out var subRasterBand);
					if (!string.IsNullOrEmpty(subRasterFile) && subRasterBand > 0 && dateTimeValuesByString[subRasterFile] != null)
					{
						gDALRaster2.SetMetadataItem(j, "Timestamp", dateTimeValuesByString[subRasterFile][subRasterBand - 1].ToString("ddMMMyyyy HH:mm:ss"));
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

	private bool TryParseGRIBTimestamps(Dictionary<string, string> kvps, ref DateTime dt)
	{
		string textValue = "";
		if (kvps.TryGetValue("GRIB_VALID_TIME", out textValue))
		{
			if (textValue.RoughlyEndsWith("sec UTC"))
			{
				if (long.TryParse(textValue.Substring(0, checked(textValue.Length - "sec UTC".Length)).Trim(), out var result))
				{
					dt = DateTime.FromFileTimeUtc(result);
					return true;
				}
				return false;
			}
			return false;
		}
		return false;
	}
}
