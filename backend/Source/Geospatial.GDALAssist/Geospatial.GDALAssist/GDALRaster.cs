using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using OSGeo.GDAL;
using OSGeo.OSR;
using Utility;
using Utility.Extensions;
using Utility.Logging;
using Utility.Memory;
using Utility.Progress;
using Utility.Units;

namespace Geospatial.GDALAssist;

public sealed class GDALRaster : IDisposable
{
	private static readonly ILogger _logger;

	public static string[] KnownUnitKeys;

	public static string[] KnownDataType;

	public static DateTime Epoch;

	private const string VrtTimestampTag = "Timestamp";

	private Dataset _srcDs;

	private Driver _srcDrv;

	private GCHandle _pinnedArray;

	private readonly bool _isOpenShared;

	private Projection _overwrittenProjection;

	private double[] _noDataCache;

	private bool[] _hasNoDataCache;

	private readonly object _noDataLock = new object();

	public static bool UseMarksWarpingMethod;

	private bool _isDisposed;

	private const string SourceFilenameNode = "SourceFilename";

	private const string SharedAttr = "shared";

	public int PixelsWide => _srcDs.RasterXSize;

	public int PixelsTall => _srcDs.RasterYSize;

	public string Filename { get; private set; }

	public bool IsOpenShared => _isOpenShared;

	public string DriverShortName => _srcDrv.ShortName;

	public string DriverLongName => _srcDrv.LongName;

	static GDALRaster()
	{
		_logger = LogHelper.GetLogger("GDALRaster");
		KnownUnitKeys = new string[4] { "Unit Type", "unit", "units", "GRIB_UNIT" };
		KnownDataType = new string[3] { "datatype", "long_name", "GRIB_ELEMENT" };
		Epoch = new DateTime(1970, 1, 1, 0, 0, 0);
		UseMarksWarpingMethod = false;
		if (!GDALSetup.IsInitialized)
		{
			throw new Exception("Must call InitializeMultiplatform before an other GDAL operations.");
		}
	}

	public static GDALRaster TryOpen(string connectionString)
	{
		try
		{
			Gdal.DontUseExceptions();
			Dataset dataset = Gdal.OpenShared(connectionString, Access.GA_ReadOnly);
			if (dataset == null)
			{
				return null;
			}
			return new GDALRaster(dataset, connectionString);
		}
		catch
		{
			return null;
		}
		finally
		{
			Gdal.UseExceptions();
		}
	}

	public static GDALRaster TryOpen(DatasetIdentifier id)
	{
		return TryOpen(id.ConnectionString);
	}

	public GDALRaster(string filename, bool useShared = true)
		: this(filename, Access.GA_ReadOnly, useShared)
	{
	}

	public GDALRaster(DatasetIdentifier subds)
		: this(subds.ConnectionString)
	{
	}

	public GDALRaster(string filename, Access fileAccess, bool useShared = true)
	{
		filename = NormalizeFilesystemPath(filename);
		Filename = filename;
		try
		{
			_isOpenShared = useShared;
			_srcDs = (useShared ? Gdal.OpenShared(filename, fileAccess) : Gdal.Open(filename, fileAccess));
		}
		catch (Exception ex)
		{
			throw new Exception("Dataset: " + filename + " could not be opened: " + ex.Message);
		}
		if (_srcDs == null)
		{
			throw new Exception("Dataset: " + filename + " could not be opened, GDAL may not support this format.");
		}
		_srcDrv = _srcDs.GetDriver();
		if (_srcDrv == null)
		{
			throw new Exception("Driver could not be opened, GDAL may not support this format.");
		}
	}

	public GDALRaster(string filename, string driverShortName)
	{
		filename = NormalizeFilesystemPath(filename);
		Filename = filename;
		try
		{
			_srcDs = Gdal.OpenEx(filename, 0u, new string[1] { driverShortName }, null, null);
		}
		catch (Exception ex)
		{
			throw new Exception("Dataset: " + filename + " could not be opened: " + ex.Message);
		}
		if (_srcDs == null)
		{
			throw new Exception("Dataset: " + filename + " could not be opened, GDAL may not support this format.");
		}
		_srcDrv = _srcDs.GetDriver();
		if (_srcDrv == null)
		{
			throw new Exception("Driver could not be opened, GDAL may not support this format.");
		}
	}

	private static string NormalizeFilesystemPath(string connectionString)
	{
		if (string.IsNullOrEmpty(connectionString))
		{
			return connectionString;
		}
		if (!connectionString.Contains(".."))
		{
			return connectionString;
		}
		if (connectionString.StartsWith("/vsi", StringComparison.OrdinalIgnoreCase))
		{
			return connectionString;
		}
		if (!Path.IsPathFullyQualified(connectionString))
		{
			return connectionString;
		}
		try
		{
			return Path.GetFullPath(connectionString);
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Could not normalize dataset path {Path}", connectionString);
			return connectionString;
		}
	}

	public GDALRaster(int[,] intValues, double minX, double maxY, double cellSize, Projection proj = null)
	{
		InitializeMemoryDataset(intValues, minX, maxY, cellSize, proj);
	}

	public GDALRaster(float[,] floatValues, double minX, double maxY, double cellSize, Projection proj = null)
	{
		InitializeMemoryDataset(floatValues, minX, maxY, cellSize, proj);
	}

	public GDALRaster(float[] floatValues, int width, int height, double minX, double maxY, double cellSize, Projection proj = null)
	{
		InitializeMemoryDataset(floatValues, width, height, minX, maxY, cellSize, proj);
	}

	internal GDALRaster(Dataset ds, string fn = "")
	{
		Filename = fn;
		_srcDs = ds;
		_srcDrv = _srcDs.GetDriver();
		if (_srcDrv == null)
		{
			throw new Exception("Driver could not be opened, GDAL may not support this format.");
		}
	}

	public string TryGetSubdatasetName()
	{
		int indexOf = Filename.IndexOf(":\"");
		int lastIndexOf = Filename.LastIndexOf("\":");
		string result = string.Empty;
		if (indexOf != -1 && lastIndexOf != -1 && indexOf != lastIndexOf)
		{
			result = Filename.Substring(lastIndexOf + 2);
		}
		return result;
	}

	public string TryGetPath()
	{
		int indexOf = Filename.IndexOf(":\"");
		int lastIndexOf = Filename.LastIndexOf("\":");
		string empty = string.Empty;
		if (indexOf != -1 && lastIndexOf != -1 && indexOf != lastIndexOf)
		{
			return Filename.Substring(indexOf + 2, lastIndexOf - indexOf - 2);
		}
		return Filename;
	}

	public static GDALRaster CreateNew(string filename, string driverName, int xSize, int ySize, int bandCount, DataType dataType)
	{
		if (File.Exists(filename))
		{
			File.Delete(filename);
		}
		Dataset ds = null;
		using (Driver driver = Gdal.GetDriverByName(driverName))
		{
			ds = driver.Create(filename, xSize, ySize, bandCount, dataType, null);
		}
		return new GDALRaster(ds, filename);
	}

	public static bool TryCreateCopy(string srcFilename, string destFilename, string driverName, string[] creationOptions = null)
	{
		using GDALRaster gDALRaster = TryOpen(srcFilename);
		if (gDALRaster == null)
		{
			return false;
		}
		using Driver driver = Gdal.GetDriverByName(driverName);
		if (driver == null)
		{
			return false;
		}
		using Dataset dataset = driver.CreateCopy(destFilename, gDALRaster._srcDs, 0, creationOptions ?? Array.Empty<string>(), null, null);
		if (dataset == null)
		{
			return false;
		}
		dataset.FlushCache();
		return true;
	}

	public static void Invoke(string filename, Action<GDALRaster> action)
	{
		using GDALRaster filename2 = new GDALRaster(filename);
		action(filename2);
	}

	public static bool IsValidRaster(string filename)
	{
		if (!GDALSetup.IsInitialized)
		{
			throw new Exception("GDALSetup not initialized.");
		}
		if (!File.Exists(filename))
		{
			return false;
		}
		if (filename.RoughlyEndsWith(".shp"))
		{
			return false;
		}
		try
		{
			Gdal.DontUseExceptions();
			Dataset dataset = Gdal.OpenShared(filename, Access.GA_ReadOnly);
			if (dataset == null)
			{
				return false;
			}
			Driver driver = dataset.GetDriver();
			if (driver == null)
			{
				dataset.Dispose();
				return false;
			}
			dataset.Dispose();
			driver.Dispose();
			return true;
		}
		catch
		{
			return false;
		}
		finally
		{
			Gdal.UseExceptions();
		}
	}

	public static bool TryRemoveBandFromVRT(string fn, int bandIndex)
	{
		try
		{
			XElement xElement = XElement.Load(fn);
			XElement xElement2 = xElement.Descendants().FirstOrDefault((XElement n) => n.Name.LocalName == "VRTRasterBand" && n.Attribute("band").Value == bandIndex.ToString());
			if (xElement2 == null)
			{
				return false;
			}
			xElement2.Remove();
			foreach (XElement xElement3 in from n in xElement.Descendants()
				where n.Name.LocalName == "VRTRasterBand" && int.Parse(n.Attribute("band").Value) > bandIndex
				select n)
			{
				xElement3.Attribute("band").SetValue((int.Parse(xElement3.Attribute("band").Value) - 1).ToString());
			}
			xElement.Save(fn);
			return true;
		}
		catch
		{
			return false;
		}
	}

	public string[] GetMetadataItems(string domain = "")
	{
		return _srcDs.GetMetadata(domain);
	}

	public Dictionary<string, string> GetMetadataDictionary(string domain = "")
	{
		Dictionary<string, string> ordinalIgnoreCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string[] metadataItems = GetMetadataItems(domain);
		for (int i = 0; i < metadataItems.Length; i++)
		{
			string[] parts = metadataItems[i].Split('=');
			if (parts.Length != 1)
			{
				ordinalIgnoreCase[parts[0]] = parts[1].Trim();
			}
		}
		return ordinalIgnoreCase;
	}

	public string GetMetadataItem(string metadataKey, string domain = "")
	{
		return _srcDs.GetMetadataItem(metadataKey, domain);
	}

	public void SetMetadataItem(string metadataKey, string metadataValue, string domain = "")
	{
		_srcDs.SetMetadataItem(metadataKey, metadataValue, domain);
	}

	public void SetMetadataItem(int bandIndex, string metadataKey, string metadataValue, string domain = "")
	{
		using Band band = _srcDs.GetRasterBand(bandIndex);
		band.SetMetadataItem(metadataKey, metadataValue, domain);
	}

	public void FlushCache()
	{
		_srcDs.FlushCache();
	}

	public string[] GetMetadataItems(int bandIndex, string domain = "")
	{
		using Band band = _srcDs.GetRasterBand(bandIndex);
		return band.GetMetadata(domain);
	}

	public Dictionary<string, string> GetMetadataDictionary(int bandIndex, string domain = "")
	{
		Dictionary<string, string> metadataDictionary = new Dictionary<string, string>();
		string[] metadataItems = GetMetadataItems(bandIndex, domain);
		for (int i = 0; i < metadataItems.Length; i++)
		{
			string[] parts = metadataItems[i].Split('=');
			if (parts.Length != 1)
			{
				metadataDictionary[parts[0]] = parts[1];
			}
		}
		return metadataDictionary;
	}

	public string GetMetadataItem(int bandIndex, string metadataKey, string domain = "")
	{
		using Band band = _srcDs.GetRasterBand(bandIndex);
		return band.GetMetadataItem(metadataKey, domain);
	}

	public string TryParseUnits()
	{
		return TryParseMetadata(KnownUnitKeys, fallbackToFirstBandValue: true);
	}

	public string TryParseDataType()
	{
		return TryParseMetadata(KnownDataType, fallbackToFirstBandValue: true);
	}

	private string TryParseMetadata(IList<string> keyOptions, bool fallbackToFirstBandValue)
	{
		Dictionary<string, string> metadataDictionary = GetMetadataDictionary();
		foreach (string keyOption in keyOptions)
		{
			if (metadataDictionary.TryGetValue(keyOption, out var localValue))
			{
				return localValue;
			}
		}
		if (fallbackToFirstBandValue)
		{
			foreach (string keyOption2 in keyOptions)
			{
				string[] metadataItemsFromEachBand = GetMetadataItemsFromEachBand(keyOption2);
				if (!metadataItemsFromEachBand.IsNullOrEmpty() && !string.IsNullOrEmpty(metadataItemsFromEachBand[0]))
				{
					return metadataItemsFromEachBand[0];
				}
			}
		}
		return string.Empty;
	}

	public void TryIdentifyVRTFileForBand(int bandIndex, out string subRasterFile, out int subRasterBand)
	{
		if (!string.Equals(_srcDrv.ShortName, "VRT", StringComparison.CurrentCultureIgnoreCase))
		{
			subRasterFile = "";
			subRasterBand = -1;
			return;
		}
		XElement xElement = XElement.Load(Filename);
		if (xElement == null)
		{
			subRasterFile = "";
			subRasterBand = -1;
			return;
		}
		IEnumerable<XElement> enumerable = from el in xElement.Descendants()
			where el.Name.LocalName.RoughlyEquals("VRTRasterBand") && el.Attribute("band").Value == bandIndex.ToString()
			select el;
		if (enumerable == null)
		{
			subRasterFile = "";
			subRasterBand = -1;
			return;
		}
		XElement xElement2 = enumerable.Descendants("SourceFilename").FirstOrDefault();
		if (xElement2 == null)
		{
			subRasterFile = "";
			subRasterBand = -1;
			return;
		}
		subRasterFile = xElement2.Value;
		XAttribute xAttribute = xElement2.Attribute("relativeToVRT");
		if (xAttribute != null && xAttribute.Value == "1")
		{
			subRasterFile = Path.Combine(Path.GetDirectoryName(Filename), subRasterFile);
		}
		XElement xElement3 = enumerable.Descendants("SourceBand").FirstOrDefault();
		if (xElement3 == null)
		{
			subRasterBand = 1;
		}
		else if (!int.TryParse(xElement3.Value, out subRasterBand))
		{
			subRasterBand = 1;
		}
	}

	public string[] GetMetadataItemsFromEachBand(string metadataKey, string domain = "")
	{
		string[] metadataItemsFromEachBand = new string[GetBandCount()];
		for (int i = 0; i < metadataItemsFromEachBand.Length; i++)
		{
			metadataItemsFromEachBand[i] = GetMetadataItem(i + 1, metadataKey, domain);
		}
		return metadataItemsFromEachBand;
	}

	public DateTime[] TryParseDatetimesPerBand()
	{
		int bandCount = GetBandCount();
		DateTime[] dateTimes = new DateTime[bandCount];
		for (int i = 0; i < bandCount; i++)
		{
			dateTimes[i] = DateTime.MinValue;
		}
		if (DriverShortName.RoughlyEquals("GRIB"))
		{
			string[] metadataItemsFromEachBand = GetMetadataItemsFromEachBand("GRIB_VALID_TIME");
			if (metadataItemsFromEachBand != null && metadataItemsFromEachBand.Length == bandCount)
			{
				for (int j = 0; j < bandCount; j++)
				{
					dateTimes[j] = TryParseGribTimestamp(metadataItemsFromEachBand[j]);
				}
				return dateTimes;
			}
		}
		else
		{
			if (!DriverShortName.RoughlyEquals("VRT"))
			{
				TryParseNetCDFTimesteps(bandCount, dateTimes);
				return dateTimes;
			}
			string[] metadataItemsFromEachBand2 = GetMetadataItemsFromEachBand("Timestamp");
			if (metadataItemsFromEachBand2 != null && metadataItemsFromEachBand2.Length == bandCount)
			{
				for (int k = 0; k < metadataItemsFromEachBand2.Length; k++)
				{
					if (DateTime.TryParseExact(metadataItemsFromEachBand2[k], "yyyy-MM-dd HH:mm:ss", null, DateTimeStyles.None, out var result))
					{
						dateTimes[k] = result;
					}
					else
					{
						dateTimes[k] = DateTime.MinValue;
					}
				}
			}
		}
		return dateTimes;
	}

	private bool TryParseNetCDFTimesteps(int bandCt, DateTime[] retn)
	{
		string[] metadataItemsFromEachBand = GetMetadataItemsFromEachBand("NETCDF_DIM_time");
		string metadataItem = GetMetadataItem("NETCDF_DIM_time");
		if (metadataItemsFromEachBand != null && !string.IsNullOrEmpty(metadataItem))
		{
			for (int i = 0; i < metadataItemsFromEachBand.Length; i++)
			{
				if (string.IsNullOrEmpty(metadataItemsFromEachBand[i]))
				{
					metadataItemsFromEachBand[i] = metadataItem;
				}
			}
		}
		if (metadataItemsFromEachBand != null && metadataItemsFromEachBand.Length == bandCt && metadataItemsFromEachBand.Any((string s) => !string.IsNullOrEmpty(s)))
		{
			DateTime input = Epoch;
			Time timeUnits = Time.Minutes;
			string getMetadataItemTrimTerm = GetMetadataItem("time#units")?.Trim();
			if (!string.IsNullOrEmpty(getMetadataItemTrimTerm))
			{
				int indexOf = getMetadataItemTrimTerm.IndexOf("since", StringComparison.OrdinalIgnoreCase);
				if (indexOf > 0)
				{
					Time time = Time.TryParse(getMetadataItemTrimTerm.Substring(0, indexOf).Trim());
					if (time != null)
					{
						timeUnits = time;
						if (DateTime.TryParse(getMetadataItemTrimTerm.Substring(indexOf + "since".Length).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var result))
						{
							input = result;
						}
					}
				}
			}
			for (int intervalIndex = 0; intervalIndex < bandCt; intervalIndex++)
			{
				retn[intervalIndex] = DateTime.MinValue;
				string metadataItemsFromEachBand2 = metadataItemsFromEachBand[intervalIndex];
				if (!string.IsNullOrWhiteSpace(metadataItemsFromEachBand2))
				{
					metadataItemsFromEachBand2 = metadataItemsFromEachBand2.Trim();
					if (long.TryParse(metadataItemsFromEachBand2, out var result2))
					{
						retn[intervalIndex] = input.AddUnits(timeUnits, result2);
					}
				}
			}
			return true;
		}
		if (bandCt == 1)
		{
			string metadataItem2 = GetMetadataItem("NC_GLOBAL#data_time");
			if (!string.IsNullOrWhiteSpace(metadataItem2))
			{
				if (DateTime.TryParse(metadataItem2, out var result3))
				{
					retn[0] = result3;
					return true;
				}
				if (DateTime.TryParseExact(metadataItem2, "yyyyMMdd", null, DateTimeStyles.None, out result3))
				{
					retn[0] = result3;
					return true;
				}
			}
		}
		return false;
	}

	public DateTime[] TryParseForecastIssueTimesPerBand()
	{
		int bandCount = GetBandCount();
		DateTime[] dateTimes = new DateTime[bandCount];
		for (int i = 0; i < bandCount; i++)
		{
			dateTimes[i] = DateTime.MinValue;
		}
		if (DriverShortName.RoughlyEquals("GRIB"))
		{
			string[] metadataItemsFromEachBand = GetMetadataItemsFromEachBand("GRIB_REF_TIME");
			if (metadataItemsFromEachBand != null && metadataItemsFromEachBand.Length == bandCount)
			{
				for (int j = 0; j < bandCount; j++)
				{
					dateTimes[j] = TryParseGribTimestamp(metadataItemsFromEachBand[j]);
				}
				return dateTimes;
			}
		}
		return dateTimes;
	}

	private static DateTime TryParseGribTimestamp(string time)
	{
		time = time.Trim();
		if (string.IsNullOrEmpty(time))
		{
			return DateTime.MinValue;
		}
		if (long.TryParse(time.Split(' ')[0], out var result))
		{
			return Epoch.AddSeconds(result);
		}
		return DateTime.MinValue;
	}

	public Dictionary<string, string> GetSubDatasets()
	{
		string[] metadataItems = GetMetadataItems("SUBDATASETS");
		Dictionary<string, string> subDatasets = new Dictionary<string, string>();
		if (metadataItems.Length == 0)
		{
			return subDatasets;
		}
		if (metadataItems.Length % 2 != 0)
		{
			return subDatasets;
		}
		for (int i = 0; i < metadataItems.Length; i += 2)
		{
			string key = metadataItems[i].Split('=').LastOrDefault().Trim();
			string trim = metadataItems[i + 1].Split('=').LastOrDefault().Trim();
			subDatasets[key] = trim;
		}
		return subDatasets;
	}

	public static Dictionary<string, string> GetSubDatasets(string filename)
	{
		using GDALRaster gDALRaster = new GDALRaster(filename);
		return gDALRaster.GetSubDatasets();
	}

	public double GetCellSizeX()
	{
		return GetFullNativeExtents().Width / (double)PixelsWide;
	}

	public double GetCellSizeY()
	{
		return GetFullNativeExtents().Height / (double)PixelsTall;
	}

	public string GetCellSizeLabel(bool shortForm = true)
	{
		double friendlyRound = FriendlyRound(GetCellSizeX());
		Projection projection = GetProjection();
		string friendlyRound2 = ((friendlyRound == Math.Floor(friendlyRound) && friendlyRound < 1000000000000000.0) ? ((long)friendlyRound).ToString() : friendlyRound.ToString("G"));
		if (projection == null || !projection.TryParseUnitSystem(out var units))
		{
			return friendlyRound2;
		}
		if (shortForm)
		{
			string unitsSiTerm = ((units == UnitSystem.SI) ? "m" : "ft");
			return friendlyRound2 + unitsSiTerm;
		}
		bool friendlyRound2Term = friendlyRound2 == "1";
		string textValue;
		if (units != UnitSystem.SI)
		{
			textValue = (friendlyRound2Term ? "foot" : "feet");
		}
		else
		{
			textValue = (friendlyRound2Term ? "meter" : "meters");
		}
		return friendlyRound2 + " " + textValue;
	}

	private static double FriendlyRound(double computed)
	{
		if (computed <= 0.0)
		{
			return computed;
		}
		double computed2 = Math.Round(computed);
		if (computed2 > 0.0 && Math.Abs(computed - computed2) / computed2 < 0.01)
		{
			return computed2;
		}
		computed2 = Math.Round(computed, 1);
		if (computed2 > 0.0 && Math.Abs(computed - computed2) / computed2 < 0.005)
		{
			return computed2;
		}
		return Math.Round(computed, 2);
	}

	public Type GetDataType()
	{
		if (GetBandCount() <= 0)
		{
			return null;
		}
		return BandDataType(1);
	}

	public string GetGDALInfo()
	{
		return Gdal.GDALInfo(_srcDs, new GDALInfoOptions(null));
	}

	public void ReadBand<T>(int band, Memory<T> buffer) where T : unmanaged, IEquatable<T>
	{
		ValidateBandIndex(band);
		using Band band2 = _srcDs.GetRasterBand(band);
		int xSize = band2.XSize;
		int ySize = band2.YSize;
		if (xSize * ySize != buffer.Length)
		{
			throw new ArgumentException("Buffer incorrectly sized - note that band size may not match the raster PixelsTall/Wide.");
		}
		ReadBandInternal(band2, 0, 0, xSize, ySize, xSize, ySize, buffer);
	}

	public T[] ReadBand<T>(int band) where T : unmanaged, IEquatable<T>
	{
		ValidateBandIndex(band);
		using Band band2 = _srcDs.GetRasterBand(band);
		int xSize = band2.XSize;
		int ySize = band2.YSize;
		T[] bandArray = new T[xSize * ySize];
		ReadBandInternal<T>(band2, 0, 0, xSize, ySize, xSize, ySize, bandArray);
		return bandArray;
	}

	public void ReadBand<T>(int band, ref T[,] buffer) where T : unmanaged, IEquatable<T>
	{
		ValidateBandIndex(band);
		using Band band2 = _srcDs.GetRasterBand(band);
		int xSize = band2.XSize;
		int ySize = band2.YSize;
		Buffers.EnsureArraySize(ref buffer, ySize, xSize);
		T[] values = new T[ySize * xSize];
		Memory<T> buf = new Memory<T>(values);
		ReadBandInternal(band2, 0, 0, xSize, ySize, xSize, ySize, buf);
		Buffer.BlockCopy(values, 0, buffer, 0, values.Length * Marshal.SizeOf<T>());
	}

	private void ReadBand<T>(int band, PixelRange readRange, int xOutSize, int yOutSize, Memory<T> buffer) where T : unmanaged, IEquatable<T>
	{
		ValidateBandIndex(band);
		using Band b = _srcDs.GetRasterBand(band);
		ValidateBandReadArgs(b, readRange);
		ValidateBufferSize(b, buffer);
		ReadBandInternal(b, readRange, xOutSize, yOutSize, buffer);
	}

	public void ReadBand<T>(int band, RasterDefinition readRaster, Memory<T> buffer, out PixelRange validDataRegion) where T : unmanaged, IEquatable<T>
	{
		if (!IsGeoReferenced())
		{
			throw new Exception("Raster " + Filename + " is not georeferenced.");
		}
		if (!readRaster.IsValid())
		{
			throw new ArgumentException("readRaster is malformed");
		}
		ValidateBandIndex(band);
		RasterDefinition nativeRaster = GetNativeRaster();
		validDataRegion = readRaster.GetPixelRange(nativeRaster.Extent, PixelRangeOptions.CenterPixelCovered);
		if (validDataRegion.IsNull())
		{
			return;
		}
		if (buffer.Length != readRaster.Rows * readRaster.Cols)
		{
			throw new ArgumentException("Provided buffer not correctly sized for the requested read range.");
		}
		PixelRange pixelRange = nativeRaster.GetPixelRange(readRaster.Extent, PixelRangeOptions.CenterPixelCovered);
		if (pixelRange.IsNull())
		{
			validDataRegion = PixelRange.NullRange;
			return;
		}
		PixelRange pixelRange2 = pixelRange.BufferConstrained(1, nativeRaster);
		if (nativeRaster.CellSize > readRaster.CellSize)
		{
			using (IMemoryOwner<T> memoryOwner = SharedMemoryPool<T>.Rent(pixelRange2.CellCount))
			{
				Memory<T> slice = memoryOwner.Memory.Slice(0, pixelRange2.CellCount);
				ReadBandInternal(band, pixelRange2, slice);
				RasterData<T> rasterData = new RasterData<T>(nativeRaster.GetSubRaster(pixelRange2), slice);
				if (TryGetNoData<T>(band, out var ndv))
				{
					rasterData.NoDataValue = ndv;
				}
				rasterData.Resample(new ResampleArgs(readRaster), buffer);
				return;
			}
		}
		if (validDataRegion == readRaster.FullRange())
		{
			ReadBandInternal(band, pixelRange, readRaster.Cols, readRaster.Rows, buffer);
			return;
		}
		using IMemoryOwner<T> memoryOwner2 = SharedMemoryPool<T>.Rent(validDataRegion.CellCount);
		Memory<T> sliceSpan = memoryOwner2.Memory.Slice(0, validDataRegion.CellCount);
		ReadBandInternal(band, pixelRange, validDataRegion.ColCount, validDataRegion.RowCount, sliceSpan);
		RasterData<T> rasterData2 = new RasterData<T>(new RasterDefinition(nativeRaster.RangeExtent(pixelRange), validDataRegion.RowCount, validDataRegion.ColCount), sliceSpan);
		if (TryGetNoData<T>(band, out var ndv2))
		{
			rasterData2.NoDataValue = ndv2;
		}
		rasterData2.Resample(new ResampleArgs(readRaster), buffer);
	}

	public void ReadBand<T>(int band, int xOffset, int yOffset, int xLength, int yLength, int xOutLength, int yOutLength, Memory<T> buffer, T fillValue = default(T)) where T : unmanaged, IEquatable<T>
	{
		ValidateBandIndex(band);
		using Band b = _srcDs.GetRasterBand(band);
		ValidateBandReadArgs(b, xOffset, yOffset, xLength, yLength);
		ValidateBufferSize(b, buffer);
		if (!fillValue.Equals(default))
		{
			buffer.Span.Fill(fillValue);
		}
		ReadBandInternal(b, xOffset, yOffset, xLength, yLength, xOutLength, yOutLength, buffer);
	}

	public void ReadBands<T>(RasterDefinition readRaster, ref T[,] buffer, out PixelRange validDataRegion) where T : unmanaged, IEquatable<T>
	{
		if (!IsGeoReferenced())
		{
			throw new Exception("Raster " + Filename + " is not georeferenced.");
		}
		if (!readRaster.IsValid())
		{
			throw new ArgumentException("readRaster is malformed");
		}
		RasterDefinition nativeRaster = GetNativeRaster();
		validDataRegion = readRaster.GetPixelRange(nativeRaster.Extent, PixelRangeOptions.CenterPixelCovered);
		if (validDataRegion.IsNull())
		{
			return;
		}
		int bandCount = GetBandCount();
		Buffers.EnsureArraySize(ref buffer, bandCount, readRaster.CellCount);
		Span<T> createSpan = MemoryMarshal.CreateSpan(ref buffer[0, 0], buffer.Length);
		Memory<T> mem;
		using (SharedMemoryPool<T>.Rent(readRaster.CellCount, out mem))
		{
			PixelRange pixelRange = nativeRaster.GetPixelRange(readRaster.Extent, PixelRangeOptions.CenterPixelCovered);
			PixelRange pixelRange2 = pixelRange.BufferConstrained(1, nativeRaster);
			Memory<T> mem2;
			if (nativeRaster.CellSize > readRaster.CellSize)
			{
				using (SharedMemoryPool<T>.Rent(pixelRange2.CellCount, out mem2))
				{
					for (int i = 1; i <= bandCount; i++)
					{
						lock (this)
						{
							ReadBandInternal(i, pixelRange2, mem2);
						}
						RasterData<T> rasterData = new RasterData<T>(nativeRaster.GetSubRaster(pixelRange2), mem2);
						if (TryGetNoData<T>(i, out var ndv))
						{
							rasterData.NoDataValue = ndv;
						}
						rasterData.Resample(new ResampleArgs(readRaster), mem);
						mem.Span.CopyTo(createSpan.Slice((i - 1) * readRaster.CellCount, readRaster.CellCount));
					}
					return;
				}
			}
			if (validDataRegion == readRaster.FullRange())
			{
				for (int j = 1; j <= bandCount; j++)
				{
					lock (this)
					{
						ReadBandInternal(j, pixelRange, readRaster.Cols, readRaster.Rows, mem);
					}
					mem.Span.CopyTo(createSpan.Slice((j - 1) * readRaster.CellCount, readRaster.CellCount));
				}
				return;
			}
			Memory<T> mem3;
			using (SharedMemoryPool<T>.Rent(validDataRegion.CellCount, out mem3))
			{
				RasterData<T> rasterData2 = new RasterData<T>(new RasterDefinition(nativeRaster.RangeExtent(pixelRange), validDataRegion.RowCount, validDataRegion.ColCount), mem3);
				for (int k = 1; k <= bandCount; k++)
				{
					lock (this)
					{
						ReadBandInternal(k, pixelRange, validDataRegion.ColCount, validDataRegion.RowCount, mem3);
					}
					if (TryGetNoData<T>(k, out var ndv2))
					{
						rasterData2.NoDataValue = ndv2;
						mem.Span.Fill(ndv2);
					}
					else
					{
						mem.Span.Fill(default);
					}
					rasterData2.Resample(new ResampleArgs(readRaster), mem);
					mem.Span.CopyTo(createSpan.Slice((k - 1) * readRaster.CellCount, readRaster.CellCount));
				}
			}
		}
	}

	public unsafe void ReadRaster<T>(Memory<T> buffer) where T : unmanaged, IEquatable<T>
	{
		int bandCount = GetBandCount();
		if (buffer.Length < PixelsWide * PixelsTall * bandCount)
		{
			throw new ArgumentException();
		}
		int[] bandMap = null;
		using MemoryHandle memoryHandle = buffer.Pin();
		_srcDs.ReadRaster(0, 0, PixelsWide, PixelsTall, (nint)memoryHandle.Pointer, PixelsWide, PixelsTall, DataType.GDT_Byte, bandCount, bandMap, 0, 0, 0);
	}

	public BandedRasterData<T> ReadRaster<T>(RasterDefinition readRaster, IList<int> bandList = null, T fillValue = default(T)) where T : unmanaged, IEquatable<T>
	{
		if (!readRaster.IsValid())
		{
			throw new ArgumentException("readRaster is malformed, bad width/height.");
		}
		int bandCount = GetBandCount();
		List<T[]> valuesList = new List<T[]>(bandCount);
		PixelRange validDataRegion = PixelRange.NullRange;
		for (int i = 1; i <= bandCount; i++)
		{
			if (bandList != null && !bandList.Contains(i))
			{
				valuesList.Add(null);
				continue;
			}
			T[] values = new T[readRaster.CellCount];
			ReadBand<T>(i, readRaster, values, out validDataRegion);
			valuesList.Add(values);
		}
		return new BandedRasterData<T>(readRaster, valuesList, validDataRegion);
	}

	public BandedRasterData<T> ReadEntireRaster<T>(IList<int> bandList = null, T fillValue = default(T)) where T : unmanaged, IEquatable<T>
	{
		int bandCount = GetBandCount();
		List<T[]> valuesList = new List<T[]>(bandCount);
		for (int i = 1; i <= bandCount; i++)
		{
			if (bandList != null && !bandList.Contains(i))
			{
				valuesList.Add(null);
				continue;
			}
			T[] values = new T[PixelsTall * PixelsWide];
			ReadBand<T>(i, values);
			valuesList.Add(values);
		}
		return new BandedRasterData<T>(GetNativeRaster(), valuesList);
	}

	private void ValidateBandIndex(int band)
	{
		int bandCount = GetBandCount();
		if (band < 1 || band > bandCount)
		{
			throw new Exception("Band (" + band + ") specified is out of range in " + Filename);
		}
	}

	private static void ValidateBandReadArgs(Band b, int srcxOffset, int srcYOffset, int srcXLength, int srcYLength)
	{
		int xSize = b.XSize;
		int ySize = b.YSize;
		if (srcxOffset + srcXLength > xSize)
		{
			throw new Exception("X Dimensions specified are out of range");
		}
		if (srcYOffset + srcYLength > ySize)
		{
			throw new Exception("Y Dimensions specified are out of range");
		}
		if (srcXLength < 0 || srcYLength < 0)
		{
			throw new ArgumentException("srcXLength/srcYLength are malformed, can't be negative.");
		}
	}

	private static void ValidateBandReadArgs(Band b, PixelRange range)
	{
		int xSize = b.XSize;
		int ySize = b.YSize;
		if (range.Left + range.Width > xSize)
		{
			throw new Exception("X Dimensions specified are out of range");
		}
		if (range.Top + range.Height > ySize)
		{
			throw new Exception("Y Dimensions specified are out of range");
		}
		if (range.Left < 0 || range.Top < 0)
		{
			throw new ArgumentException("Range is malformed, can't be negative.");
		}
	}

	private static void ValidateBufferSize<T>(Band b, Memory<T> buf)
	{
		if (buf.Length != b.XSize * b.YSize)
		{
			throw new ArgumentException("Buffer incorrectly sized for the given band. Note that band size may be different than the base raster size, which is not fully supported in GDALAssist.");
		}
	}

	private void ReadBandInternal<T>(int bandIndex, PixelRange range, Memory<T> buf) where T : unmanaged, IEquatable<T>
	{
		using Band b = _srcDs.GetRasterBand(bandIndex);
		ReadBandInternal(b, range, range.Width, range.Height, buf);
	}

	private void ReadBandInternal<T>(int bandIndex, PixelRange range, int xOutsize, int yOutsize, Memory<T> buf) where T : unmanaged, IEquatable<T>
	{
		using Band b = _srcDs.GetRasterBand(bandIndex);
		ReadBandInternal(b, range, xOutsize, yOutsize, buf);
	}

	private static void ReadBandInternal<T>(Band b, PixelRange range, int xOutsize, int yOutsize, Memory<T> buf) where T : unmanaged, IEquatable<T>
	{
		ReadBandInternal(b, range.Left, range.Top, range.Width, range.Height, xOutsize, yOutsize, buf);
	}

	private unsafe static void ReadBandInternal<T>(Band b, int srcXOffset, int srcYOffset, int srcXLength, int srcYLength, int xOutsize, int yOutsize, Memory<T> buf) where T : unmanaged, IEquatable<T>
	{
		DataType gdalDataType = GetGdalDataType<T>();
		using MemoryHandle memoryHandle = buf.Pin();
		b.ReadRaster(srcXOffset, srcYOffset, srcXLength, srcYLength, (nint)memoryHandle.Pointer, xOutsize, yOutsize, gdalDataType, 0, 0);
	}

	private static DataType GetGdalDataType<T>() where T : unmanaged
	{
		if (typeof(T) == typeof(byte))
		{
			return DataType.GDT_Byte;
		}
		if (typeof(T) == typeof(short))
		{
			return DataType.GDT_Int16;
		}
		if (typeof(T) == typeof(ushort))
		{
			return DataType.GDT_UInt16;
		}
		if (typeof(T) == typeof(int))
		{
			return DataType.GDT_Int32;
		}
		if (typeof(T) == typeof(uint))
		{
			return DataType.GDT_UInt32;
		}
		if (typeof(T) == typeof(float))
		{
			return DataType.GDT_Float32;
		}
		if (typeof(T) == typeof(double))
		{
			return DataType.GDT_Float64;
		}
		throw new NotImplementedException();
	}

	public BandedRasterData<T> GetWarped<T>(Projection destProjection, RasterDefinition destRaster, IList<int> bandList = null) where T : unmanaged, IEquatable<T>
	{
		if (UseMarksWarpingMethod && typeof(T) == typeof(float))
		{
			BandedRasterData<T> bandedRasterData = ReadEntireRaster<T>(bandList);
			RasterDefinition nativeRaster = GetNativeRaster();
			List<float[]> floatValuesList = new List<float[]>();
			using ReprojectionTransformer targetToSourceTransformer = new ReprojectionTransformer(GetProjection(), destProjection);
			float[] floatValues = new float[destRaster.CellCount];
			for (int i = 1; i <= GetBandCount(); i++)
			{
				if (bandList != null && !bandList.Contains(i))
				{
					floatValuesList.Add(null);
				}
				float[] band = bandedRasterData.Bands[i - 1] as float[];
				RasterOperations.Warp<float>(nativeRaster, band, destRaster, floatValues, targetToSourceTransformer);
				floatValuesList.Add(floatValues.ToArray());
			}
			return new BandedRasterData<float>(destRaster, floatValuesList) as BandedRasterData<T>;
		}
		Dataset dataset = null;
		try
		{
			dataset = Gdal.AutoCreateWarpedVRT(_srcDs, GetProjection().GetWKT(), destProjection.GetWKT(), ResampleAlg.GRA_NearestNeighbour, 0.125);
		}
		catch
		{
			try
			{
				dataset?.Dispose();
			}
			catch
			{
			}
			throw;
		}
		using GDALRaster gDALRaster = new GDALRaster(dataset);
		return gDALRaster.ReadRaster<T>(destRaster, bandList);
	}

	public T ReadPixel<T>(int bandIndex, int pxIdx) where T : unmanaged, IEquatable<T>
	{
		Pixel pixel = GetNativeRaster().CellIndex2D(pxIdx);
		using Band b = _srcDs.GetRasterBand(bandIndex);
		T[] values = new T[1];
		ReadBandInternal<T>(b, pixel.X, pixel.Y, 1, 1, 1, 1, values);
		return values[0];
	}

	public T ReadPixel<T>(int bandIndex, Pixel px) where T : unmanaged, IEquatable<T>
	{
		using Band b = _srcDs.GetRasterBand(bandIndex);
		T[] values = new T[1];
		ReadBandInternal<T>(b, px.X, px.Y, 1, 1, 1, 1, values);
		return values[0];
	}

	public void ReadPixels<T>(int bandIndex, Memory<int> pixels, Memory<T> result) where T : unmanaged, IEquatable<T>
	{
		RasterDefinition nativeRaster = GetNativeRaster();
		using Band b = _srcDs.GetRasterBand(bandIndex);
		T[] values = new T[1];
		Span<int> pixelsSpan = pixels.Span;
		Span<T> buffer = result.Span;
		for (int i = 0; i < pixelsSpan.Length; i++)
		{
			int idx1D = pixelsSpan[i];
			Pixel pixel = nativeRaster.CellIndex2D(idx1D);
			ReadBandInternal<T>(b, pixel.X, pixel.Y, 1, 1, 1, 1, values);
			buffer[i] = values[0];
		}
	}

	public T[] ReadPixel<T>(int pxIdx) where T : unmanaged, IEquatable<T>
	{
		int bandCount = GetBandCount();
		T[] pixelArray = new T[bandCount];
		Pixel pixel = GetNativeRaster().CellIndex2D(pxIdx);
		T[] values = new T[1];
		for (int i = 0; i < bandCount; i++)
		{
			using Band b = _srcDs.GetRasterBand(i);
			ReadBandInternal<T>(b, pixel.X, pixel.Y, 1, 1, 1, 1, values);
			pixelArray[i] = values[0];
		}
		return pixelArray;
	}

	public void ReadPixels<T>(Memory<int> pixels, Memory<T>[] result) where T : unmanaged, IEquatable<T>
	{
		int bandCount = GetBandCount();
		RasterDefinition nativeRaster = GetNativeRaster();
		Span<int> pixelsSpan = pixels.Span;
		T[] values = new T[1];
		for (int i = 0; i < bandCount; i++)
		{
			Span<T> buffer = result[i].Span;
			using Band b = _srcDs.GetRasterBand(i);
			for (int j = 0; j < pixelsSpan.Length; j++)
			{
				int idx1D = pixelsSpan[j];
				Pixel pixel = nativeRaster.CellIndex2D(idx1D);
				ReadBandInternal<T>(b, pixel.X, pixel.Y, 1, 1, 1, 1, values);
				buffer[j] = values[0];
			}
		}
	}

	public void ReadPoints<T>(Memory<Point> points, int startBand, int bandCount, Memory<T>[] result) where T : unmanaged, IEquatable<T>
	{
		int bandCount2 = GetBandCount();
		if (startBand + bandCount - 1 > bandCount2)
		{
			throw new ArgumentOutOfRangeException();
		}
		RasterDefinition nativeRaster = GetNativeRaster();
		Span<Point> pointsSpan = points.Span;
		T[] values = new T[1];
		for (int i = startBand; i < startBand + bandCount; i++)
		{
			Span<T> buffer = result[i - startBand].Span;
			using Band b = _srcDs.GetRasterBand(i);
			TryGetNoData<T>(i, out var ndv);
			for (int j = 0; j < pointsSpan.Length; j++)
			{
				Point pt = pointsSpan[j];
				if (nativeRaster.Contains(pt))
				{
					Pixel pixel = nativeRaster.CellIndex2D(pt);
					ReadBandInternal<T>(b, pixel.X, pixel.Y, 1, 1, 1, 1, values);
					buffer[j] = values[0];
				}
				else
				{
					buffer[j] = ndv;
				}
			}
		}
	}

	public T ReadPoint<T>(Point point) where T : unmanaged, IEquatable<T>
	{
		return ReadPoint<T>(point, 1);
	}

	public T ReadPoint<T>(Point point, int band) where T : unmanaged, IEquatable<T>
	{
		Point[] points = new Point[1] { point };
		Memory<T>[] bufferArray = new Memory<T>[1] { new T[1] };
		ReadPoints(points, band, 1, bufferArray);
		return bufferArray[0].Span[0];
	}

	public void ReadStats(int bandIndex, out double Min, out double Max, out double Mean, out double Stddev)
	{
		ValidateBandIndex(bandIndex);
		_srcDs.GetRasterBand(bandIndex).GetStatistics(0, 0, out Min, out Max, out Mean, out Stddev);
	}

	public void ComputeStats(int bandIndex, out double min, out double max, out double Mean, out double Stddev)
	{
		ValidateBandIndex(bandIndex);
		_srcDs.GetRasterBand(bandIndex).GetStatistics(0, 1, out min, out max, out Mean, out Stddev);
	}

	public int GetBandCount()
	{
		return _srcDs.RasterCount;
	}

	public bool IsGeoReferenced()
	{
		Extent fullNativeExtents = GetFullNativeExtents();
		if (fullNativeExtents.LowerLeft.Y >= fullNativeExtents.UpperLeft.Y)
		{
			return false;
		}
		return true;
	}

	public ColorInterp GetBandColorInterpretation(int bandIndex)
	{
		ValidateBandIndex(bandIndex);
		using Band band = _srcDs.GetRasterBand(bandIndex);
		return band.GetRasterColorInterpretation();
	}

	public string[] BandDataType_Names()
	{
		int rasterCount = _srcDs.RasterCount;
		string[] strings = new string[rasterCount];
		for (int i = 0; i < rasterCount; i++)
		{
			using Band band = _srcDs.GetRasterBand(i + 1);
			strings[i] = band.DataType.ToString();
		}
		return strings;
	}

	public Type BandDataType(int bandIndex)
	{
		using Band band = _srcDs.GetRasterBand(bandIndex);
		if (band == null)
		{
			return null;
		}
		switch (band.DataType)
		{
		case DataType.GDT_Float32:
		case DataType.GDT_CFloat32:
			return typeof(float);
		case DataType.GDT_Float64:
		case DataType.GDT_CFloat64:
			return typeof(double);
		case DataType.GDT_Byte:
		case DataType.GDT_Int8:
			return typeof(byte);
		case DataType.GDT_Int16:
		case DataType.GDT_CInt16:
			return typeof(short);
		case DataType.GDT_Int32:
		case DataType.GDT_CInt32:
			return typeof(int);
		case DataType.GDT_Int64:
			return typeof(long);
		case DataType.GDT_UInt16:
			return typeof(ushort);
		case DataType.GDT_UInt32:
			return typeof(uint);
		case DataType.GDT_UInt64:
			return typeof(ulong);
		default:
			return null;
		}
	}

	public string[] BandDataType_FriendlyName()
	{
		string[] bandDataType_Names = BandDataType_Names();
		for (int i = 0; i < bandDataType_Names.Length; i++)
		{
			bandDataType_Names[i] = bandDataType_Names[i].Replace("gdt_", "").Replace("GDT_", "");
		}
		return bandDataType_Names;
	}

	public ReadOnlyMemory<double> GetNoDataMemory()
	{
		return GetNoDataCache();
	}

	public double[] GetNoDataVB()
	{
		return GetNoDataMemory().ToArray();
	}

	public int GetNoData(Span<double> destination)
	{
		double[] noDataCache = GetNoDataCache();
		noDataCache.AsSpan().CopyTo(destination);
		return noDataCache.Length;
	}

	private double[] GetNoDataCache()
	{
		return LazyThreadsafe<double[]>.AllocateOnce(ref _noDataCache, _noDataLock, this, (GDALRaster self) => self.ReadAllNoData());
	}

	private double[] ReadAllNoData()
	{
		int rasterCount = _srcDs.RasterCount;
		double[] allNoData = new double[rasterCount];
		bool[] hasNoData = new bool[rasterCount];
		for (int i = 0; i < rasterCount; i++)
		{
			using Band band = _srcDs.GetRasterBand(i + 1);
			band.GetNoDataValue(out allNoData[i], out var hasValue);
			hasNoData[i] = hasValue != 0;
			if (!hasNoData[i])
			{
				allNoData[i] = double.NaN;
			}
		}
		_hasNoDataCache = hasNoData;
		return allNoData;
	}

	public bool TryGetNoData<T>(int bandIndex, out T ndv) where T : unmanaged
	{
		ValidateBandIndex(bandIndex);
		double noDataCache = GetNoDataCache()[bandIndex - 1];
		if (!_hasNoDataCache[bandIndex - 1])
		{
			ndv = default;
			return false;
		}
		if (TryConvertNoData(noDataCache, typeof(T), out object converted))
		{
			ndv = (T)converted;
			return true;
		}

		throw new InvalidDataException(
			$"NoData value {noDataCache.ToString("R", CultureInfo.InvariantCulture)} cannot be represented exactly as {typeof(T).Name} for raster band {bandIndex}.");
	}

	private static bool TryConvertNoData(double value, Type targetType, out object converted)
	{
		if (targetType == typeof(double))
		{
			converted = value;
			return true;
		}
		if (targetType == typeof(float))
		{
			if (double.IsNaN(value))
			{
				converted = float.NaN;
				return true;
			}
			float candidate = (float)value;
			if ((double)candidate == value)
			{
				converted = candidate;
				return true;
			}
		}
		else if (!double.IsFinite(value) || Math.Truncate(value) != value)
		{
			converted = null;
			return false;
		}
		else if (targetType == typeof(byte) && value >= byte.MinValue && value <= byte.MaxValue)
		{
			converted = (byte)value;
			return true;
		}
		else if (targetType == typeof(short) && value >= short.MinValue && value <= short.MaxValue)
		{
			converted = (short)value;
			return true;
		}
		else if (targetType == typeof(ushort) && value >= ushort.MinValue && value <= ushort.MaxValue)
		{
			converted = (ushort)value;
			return true;
		}
		else if (targetType == typeof(int) && value >= int.MinValue && value <= int.MaxValue)
		{
			converted = (int)value;
			return true;
		}
		else if (targetType == typeof(uint) && value >= uint.MinValue && value <= uint.MaxValue)
		{
			converted = (uint)value;
			return true;
		}
		else if (targetType == typeof(long) && value >= long.MinValue && value < 9223372036854775808d)
		{
			long candidate = (long)value;
			if ((double)candidate == value)
			{
				converted = candidate;
				return true;
			}
		}

		converted = null;
		return false;
	}

	public string[] GetUnitType()
	{
		int rasterCount = _srcDs.RasterCount;
		string[] unitType = new string[rasterCount];
		for (int i = 0; i < rasterCount; i++)
		{
			using Band band = _srcDs.GetRasterBand(i + 1);
			unitType[i] = band.GetUnitType();
		}
		return unitType;
	}

	public Tuple<byte, byte, byte, byte>[] GetColorMap(int band)
	{
		ValidateBandIndex(band);
		using Band band2 = _srcDs.GetRasterBand(band);
		ColorTable rasterColorTable = band2.GetRasterColorTable();
		if (rasterColorTable == null)
		{
			return null;
		}
		int count = rasterColorTable.GetCount();
		if (count > 10485760)
		{
			return null;
		}
		bool condition = true;
		Tuple<byte, byte, byte, byte>[] tupleArray = new Tuple<byte, byte, byte, byte>[count];
		for (int i = 0; i < count; i++)
		{
			ColorEntry colorEntry = rasterColorTable.GetColorEntry(i);
			if (colorEntry.c1 < 0 || colorEntry.c1 > 255 || colorEntry.c2 < 0 || colorEntry.c2 > 255 || colorEntry.c3 < 0 || colorEntry.c3 > 255 || colorEntry.c4 < 0 || colorEntry.c4 > 255)
			{
				condition = false;
				break;
			}
			byte c1 = (byte)colorEntry.c1;
			byte c2 = (byte)colorEntry.c2;
			byte c3 = (byte)colorEntry.c3;
			byte c4 = (byte)colorEntry.c4;
			tupleArray[i] = new Tuple<byte, byte, byte, byte>(c1, c2, c3, c4);
		}
		if (condition)
		{
			return tupleArray;
		}
		float computed = 65535f;
		for (int j = 0; j < count; j++)
		{
			ColorEntry colorEntry2 = rasterColorTable.GetColorEntry(j);
			byte c1ColorEntry2Term = (byte)Math.Round((float)(255 * (ushort)colorEntry2.c1) / computed);
			byte c2ColorEntry2Term = (byte)Math.Round((float)(255 * (ushort)colorEntry2.c2) / computed);
			byte c3ColorEntry2Term = (byte)Math.Round((float)(255 * (ushort)colorEntry2.c3) / computed);
			byte c4ColorEntry2Term = (byte)Math.Round((float)(255 * (ushort)colorEntry2.c4) / computed);
			tupleArray[j] = new Tuple<byte, byte, byte, byte>(c1ColorEntry2Term, c2ColorEntry2Term, c3ColorEntry2Term, c4ColorEntry2Term);
		}
		return tupleArray;
	}

	public void GetBlockSize(int bandIndex, out int blockXSize, out int blockYSize)
	{
		using Band band = _srcDs.GetRasterBand(bandIndex);
		band.GetBlockSize(out blockXSize, out blockYSize);
	}

	public PixelRange[] GetPyramidLevels(int bandIndex)
	{
		using Band band = _srcDs.GetRasterBand(bandIndex);
		int overviewCount = band.GetOverviewCount();
		PixelRange[] pixelRanges = new PixelRange[overviewCount + 1];
		pixelRanges[0] = new PixelRange(0, band.YSize - 1, 0, band.XSize - 1);
		for (int i = 0; i < overviewCount; i++)
		{
			using Band band2 = band.GetOverview(i);
			pixelRanges[i + 1] = new PixelRange(0, band2.YSize - 1, 0, band2.XSize - 1);
		}
		return pixelRanges;
	}

	public void ReadBandExact<T>(int band, int overview, int xOffset, int yOffset, int xLength, int yLength, int xOutLength, int yOutLength, Memory<T> buffer) where T : unmanaged, IEquatable<T>
	{
		ValidateBandIndex(band);
		using Band band2 = _srcDs.GetRasterBand(band);
		if (overview == 0)
		{
			ReadBandInternal(band2, xOffset, yOffset, xLength, yLength, xOutLength, yOutLength, buffer);
			return;
		}
		using Band b = band2.GetOverview(overview - 1);
		ReadBandInternal(b, xOffset, yOffset, xLength, yLength, xOutLength, yOutLength, buffer);
	}

	public void SetOverrideProjection(Projection prj)
	{
		_overwrittenProjection = prj;
	}

	public Projection GetProjection()
	{
		if (_overwrittenProjection != null)
		{
			return _overwrittenProjection;
		}
		SpatialReference spatialRef = _srcDs.GetSpatialRef();
		if (spatialRef == null)
		{
			return null;
		}
		return new Projection(spatialRef);
	}

	public bool TryGuessProjection(out Projection prj)
	{
		bool startsWithAndEndsWithTerm = false;
		bool startsWithAndEndsWith2 = false;
		foreach (string key in GetMetadataDictionary().Keys)
		{
			if (!key.EndsWith("#units"))
			{
				continue;
			}
			string metadataItem = GetMetadataItem(key) ?? "";
			bool condition = startsWithAndEndsWithTerm;
			bool startsWithAndEndsWith = metadataItem.StartsWith("degree") && metadataItem.TrimEnd().EndsWith("orth", StringComparison.OrdinalIgnoreCase);
			if (!startsWithAndEndsWith)
			{
				bool startsWithAndEndsWith3;
				switch (metadataItem)
				{
				case "degrees_N":
				case "degreeN":
				case "degree_N":
					startsWithAndEndsWith3 = true;
					break;
				default:
					startsWithAndEndsWith3 = false;
					break;
				}
				startsWithAndEndsWith = startsWithAndEndsWith3;
			}
			startsWithAndEndsWithTerm = condition | startsWithAndEndsWith;
			startsWithAndEndsWith = startsWithAndEndsWith2;
			condition = metadataItem.StartsWith("degree") && metadataItem.TrimEnd().EndsWith("ast", StringComparison.OrdinalIgnoreCase);
			if (!condition)
			{
				bool condition2;
				switch (metadataItem)
				{
				case "degrees_E":
				case "degreeE":
				case "degree_E":
					condition2 = true;
					break;
				default:
					condition2 = false;
					break;
				}
				condition = condition2;
			}
			startsWithAndEndsWith2 = startsWithAndEndsWith | condition;
			if (startsWithAndEndsWithTerm | startsWithAndEndsWith2)
			{
				break;
			}
		}
		if (startsWithAndEndsWithTerm | startsWithAndEndsWith2)
		{
			prj = WellKnownProjections.WGS84;
			return true;
		}
		prj = null;
		return false;
	}

	public static bool TryRewrapVrtLongitudesTo180(string vrtPath)
	{
		try
		{
			XElement xElement = XElement.Load(vrtPath);
			XElement xElement2 = xElement.Element("GeoTransform");
			if (xElement2 == null)
			{
				return false;
			}
			string[] parts = xElement2.Value.Split(',');
			if (parts.Length != 6)
			{
				return false;
			}
			double[] doubleValues = new double[6];
			for (int i = 0; i < 6; i++)
			{
				if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValues[i]))
				{
					return false;
				}
			}
			if (doubleValues[2] != 0.0 || doubleValues[4] != 0.0 || doubleValues[1] <= 0.0)
			{
				return false;
			}
			int attribute = (int)xElement.Attribute("rasterXSize");
			double computed = doubleValues[0];
			double computed2 = doubleValues[1];
			if (computed + computed2 * (double)attribute <= 180.0 + computed2 * 0.01)
			{
				return false;
			}
			if (computed2 * (double)attribute > 360.0 + computed2 * 0.5)
			{
				return false;
			}
			double computed3 = (180.0 - computed) / computed2;
			int ordinal = (int)Math.Round(computed3);
			if (ordinal < 0)
			{
				ordinal = 0;
			}
			if (ordinal > 0)
			{
				if (Math.Abs(computed3 - (double)ordinal) > 0.01)
				{
					return false;
				}
				if (Math.Abs(computed2 * (double)attribute - 360.0) > computed2 * 0.5)
				{
					return false;
				}
			}
			int attributeTerm = attribute - ordinal;
			foreach (XElement element in xElement.Elements("VRTRasterBand"))
			{
				foreach (XElement xElement6 in (from el in element.Elements()
					where el.Name.LocalName.EndsWith("Source")
					select el).ToList())
				{
					XElement xElement3 = xElement6.Element("SrcRect");
					XElement xElement4 = xElement6.Element("DstRect");
					if (xElement3 == null || xElement4 == null)
					{
						return false;
					}
					if ((int)xElement3.Attribute("xOff") != 0 || (int)xElement3.Attribute("xSize") != attribute || (int)xElement4.Attribute("xOff") != 0 || (int)xElement4.Attribute("xSize") != attribute)
					{
						return false;
					}
					if (ordinal != 0)
					{
						xElement3.SetAttributeValue("xOff", ordinal);
						xElement3.SetAttributeValue("xSize", attributeTerm);
						xElement4.SetAttributeValue("xOff", 0);
						xElement4.SetAttributeValue("xSize", attributeTerm);
						XElement xElement5 = new XElement(xElement6);
						xElement5.Element("SrcRect").SetAttributeValue("xOff", 0);
						xElement5.Element("SrcRect").SetAttributeValue("xSize", ordinal);
						xElement5.Element("DstRect").SetAttributeValue("xOff", attributeTerm);
						xElement5.Element("DstRect").SetAttributeValue("xSize", ordinal);
						xElement6.AddAfterSelf(xElement5);
					}
				}
			}
			doubleValues[0] = computed + (double)ordinal * computed2 - 360.0;
			xElement2.Value = string.Join(", ", doubleValues.Select((double d) => d.ToString("R", CultureInfo.InvariantCulture)));
			xElement.Save(vrtPath);
			return true;
		}
		catch
		{
			return false;
		}
	}

	public Extent GetFullNativeExtents()
	{
		return Projector.GetCorners(_srcDs);
	}

	public RasterDefinition GetNativeRaster()
	{
		return new RasterDefinition(GetFullNativeExtents(), PixelsTall, PixelsWide);
	}

	public void SetRasterDefinition(RasterDefinition rdef)
	{
		double[] geoTransform = new double[6]
		{
			rdef.MinX,
			rdef.CellSize,
			0.0,
			rdef.MaxY,
			0.0,
			0.0 - rdef.CellSize
		};
		_srcDs.SetGeoTransform(geoTransform);
	}

	private RasterDefinition GetRasterForBand(Band b)
	{
		return new RasterDefinition(GetFullNativeExtents(), b.YSize, b.XSize);
	}

	public PyramidDefinition GetPyramidDefinition(int bandIndex = 1)
	{
		using Band band = _srcDs.GetRasterBand(1);
		int overviewCount = band.GetOverviewCount();
		List<RasterDefinition> rasterDefinitions = new List<RasterDefinition>();
		rasterDefinitions.Add(GetNativeRaster());
		for (int i = 0; i < overviewCount; i++)
		{
			using Band b = band.GetOverview(i);
			rasterDefinitions.Add(GetRasterForBand(b));
		}
		return new PyramidDefinition(rasterDefinitions);
	}

	public Extent GetProjectedExtents(Projection targetProjection)
	{
		Extent fullNativeExtents = GetFullNativeExtents();
		Projection projection = GetProjection();
		if (projection == null)
		{
			throw new Exception("Source projection not detected. Please use OverwriteProjection.");
		}
		return Projector.GetBoundingBox(fullNativeExtents, projection, targetProjection);
	}

	public Extent GetNativeExtents(PixelRange pixelBBox)
	{
		double cellSizeX = GetCellSizeX();
		return GetFullNativeExtents().ExtendLeft(-1.0 * cellSizeX * (double)pixelBBox.Left).ExtendRight(-1.0 * cellSizeX * (double)(PixelsWide - pixelBBox.Right + 1)).ExtendUp(-1.0 * cellSizeX * (double)pixelBBox.Top)
			.ExtendDown(-1.0 * cellSizeX * (double)(PixelsTall - pixelBBox.Bottom + 1));
	}

	public PixelRange GetPixelCorners(Extent bbox)
	{
		return GetNativeRaster().GetPixelRange(bbox);
	}

	public PixelRange GetPixelCorners(Extent destExtent, Projection destProj)
	{
		Extent boundingBox = Projector.GetBoundingBox(destExtent, destProj, GetProjection());
		return GetPixelCorners(boundingBox);
	}

	public PixelRange GetPixelCorners()
	{
		return new PixelRange(0, PixelsTall - 1, 0, PixelsWide - 1);
	}

	private void InitializeMemoryDataset<T>(T[,] array, double minX, double maxY, double cellSize, Projection proj = null) where T : unmanaged
	{
		if (!GDALSetup.IsInitialized)
		{
			throw new Exception("GDALSetup not initialized.");
		}
		if (array == null)
		{
			throw new ArgumentNullException("array");
		}
		Filename = "";
		_srcDs = OpenMemoryRaster(array, out _pinnedArray);
		if (_srcDs == null)
		{
			throw new Exception("In-memory dataset was not successfully created.");
		}
		_srcDrv = _srcDs.GetDriver();
		if (_srcDrv == null)
		{
			throw new Exception("Driver could not be opened, GDAL may not support this format.");
		}
		double[] geoTransform = new double[6]
		{
			minX,
			cellSize,
			0.0,
			maxY,
			0.0,
			0.0 - cellSize
		};
		_srcDs.SetGeoTransform(geoTransform);
		if (proj != null)
		{
			_srcDs.SetProjection(proj.GetWKT());
		}
	}

	private void InitializeMemoryDataset<T>(T[] array, int width, int height, double minX, double maxY, double cellSize, Projection proj = null) where T : unmanaged
	{
		if (!GDALSetup.IsInitialized)
		{
			throw new Exception("GDALSetup not initialized.");
		}
		if (array == null)
		{
			throw new ArgumentNullException("array");
		}
		Filename = "";
		_srcDs = OpenMemoryRaster(array, width, height, out _pinnedArray);
		if (_srcDs == null)
		{
			throw new Exception("In-memory dataset was not successfully created.");
		}
		_srcDrv = _srcDs.GetDriver();
		if (_srcDrv == null)
		{
			throw new Exception("Driver could not be opened, GDAL may not support this format.");
		}
		double[] geoTransform = new double[6]
		{
			minX,
			cellSize,
			0.0,
			maxY,
			0.0,
			0.0 - cellSize
		};
		_srcDs.SetGeoTransform(geoTransform);
		if (proj != null)
		{
			_srcDs.SetProjection(proj.GetWKT());
		}
	}

	public static Extent GetFullNativeExtents(string filename)
	{
		using GDALRaster gDALRaster = new GDALRaster(filename);
		return gDALRaster.GetFullNativeExtents();
	}

	public static RasterDefinition GetNativeRaster(string filename)
	{
		using GDALRaster gDALRaster = new GDALRaster(filename);
		return gDALRaster.GetNativeRaster();
	}

	public static void ExportToGoodTiff(string fileInput, string TiffFileOut)
	{
		Executables.GdalTranslate("-of GTIFF -co COMPRESS=DEFLATE -co TILED=YES " + fileInput + " " + TiffFileOut, out var StdOut, out var StdErr);
		int pixelsWideGDALRasterTerm = 0;
		using (GDALRaster gDALRaster = new GDALRaster(TiffFileOut))
		{
			pixelsWideGDALRasterTerm = (int)Math.Max((double)gDALRaster.PixelsWide / 256.0, (double)gDALRaster.PixelsTall / 256.0);
		}
		string textValue = "-r average --config COMPRESS_OVERVIEW DEFLATE " + TiffFileOut + " ";
		int unity = 1;
		while (unity < pixelsWideGDALRasterTerm)
		{
			unity *= 2;
			textValue = textValue + unity + " ";
		}
		if (unity != 1)
		{
			Executables.GdalAddo(textValue, out StdErr, out StdOut);
		}
	}

	public static string[] GetVRTSources(string vrtfile)
	{
		using GDALRaster gDALRaster = new GDALRaster(vrtfile);
		return gDALRaster._srcDs.GetFileList();
	}

	public static void SetHistogram(string filename, double min, double max, int[] buckets)
	{
		using GDALRaster gDALRaster = new GDALRaster(filename, Access.GA_Update);
		gDALRaster._srcDs.GetRasterBand(1).SetDefaultHistogram(min, max, buckets.Length, buckets);
	}

	public static void GetHistogram(string filename, double min, double max, int numbuckets, out int[] buckets, ProgressReporter reporter)
	{
		using GDALRaster gDALRaster = new GDALRaster(filename);
		Gdal.GDALProgressFuncDelegate gDALProgressDelegate = GetGDALProgressDelegate(reporter);
		buckets = new int[numbuckets];
		gDALRaster._srcDs.GetRasterBand(1).GetHistogram(min, max, numbuckets, buckets, 0, 0, gDALProgressDelegate, null);
	}

	public static void ReadCurrentHistogram(string filename, out double Min, out double Max, out int[] Buckets, ProgressReporter reporter)
	{
		using GDALRaster gDALRaster = new GDALRaster(filename);
		Gdal.GDALProgressFuncDelegate gDALProgressDelegate = GetGDALProgressDelegate(reporter);
		gDALRaster._srcDs.GetRasterBand(1).GetDefaultHistogram(out Min, out Max, out var _, out Buckets, 0, gDALProgressDelegate, null);
		if (!(Path.GetExtension(filename) == ".vrt"))
		{
			return;
		}
		XElement xElement = XElement.Load(filename).Element("VRTRasterBand");
		if (xElement == null)
		{
			return;
		}
		XElement xElement2 = xElement.Element("Histograms");
		if (xElement2 == null)
		{
			return;
		}
		XElement xElement3 = xElement2.Element("HistItem");
		if (xElement3 == null)
		{
			return;
		}
		XElement xElement4 = xElement3.Element("HistCounts");
		if (xElement4 == null)
		{
			return;
		}
		string[] parts = xElement4.Value.Split(new char[1] { '|' }, StringSplitOptions.RemoveEmptyEntries);
		List<int> intList = new List<int>();
		string[] partsArray = parts;
		foreach (string s in partsArray)
		{
			int result = 0;
			if (int.TryParse(s, out result))
			{
				intList.Add(result);
			}
		}
		Buckets = intList.ToArray();
	}

	public static void GetStats(string filename, out double Min, out double Max, out double Mean, out double Stddev, bool ForceCompute = false)
	{
		Access fileAccess = (ForceCompute ? Access.GA_Update : Access.GA_ReadOnly);
		try
		{
			using GDALRaster gDALRaster = new GDALRaster(filename, fileAccess);
			Band rasterBand = gDALRaster._srcDs.GetRasterBand(1);
			if (rasterBand == null)
			{
				Min = double.NaN;
				Max = double.NaN;
				Mean = double.NaN;
				Stddev = double.NaN;
			}
			else
			{
				rasterBand.GetStatistics(0, ForceCompute ? 1 : 0, out Min, out Max, out Mean, out Stddev);
			}
		}
		catch
		{
			try
			{
				using GDALRaster gDALRaster2 = new GDALRaster(filename, Access.GA_ReadOnly);
				gDALRaster2._srcDs.GetRasterBand(1).GetStatistics(0, 0, out Min, out Max, out Mean, out Stddev);
			}
			catch
			{
				Min = double.NaN;
				Max = double.NaN;
				Mean = double.NaN;
				Stddev = double.NaN;
			}
		}
	}

	public static void SetStats(string filename, double Min, double Max, double Mean, double Stddev)
	{
		using GDALRaster gDALRaster = new GDALRaster(filename, Access.GA_Update);
		gDALRaster._srcDs.GetRasterBand(1).SetStatistics(Min, Max, Mean, Stddev);
	}

	public static void TransferProjectionInformation(string fromFile, string toFile)
	{
		using GDALRaster gDALRaster = new GDALRaster(fromFile, Access.GA_ReadOnly);
		using GDALRaster gDALRaster2 = new GDALRaster(toFile, Access.GA_Update);
		string projection = gDALRaster._srcDs.GetProjection();
		gDALRaster2._srcDs.SetProjection(projection);
		double[] doubleValues = new double[6];
		gDALRaster._srcDs.GetGeoTransform(doubleValues);
		gDALRaster2._srcDs.SetGeoTransform(doubleValues);
	}

	public static Projection GetProjection(string rasterFile)
	{
		using Dataset dataset = Gdal.OpenShared(rasterFile, Access.GA_ReadOnly);
		using SpatialReference spatialReference = dataset.GetSpatialRef();
		if (spatialReference == null)
		{
			return null;
		}
		return new Projection(spatialReference);
	}

	public static bool TryGetSpatialData(string rasterFile, out Extent extent, out Projection projection)
	{
		extent = Extent.Infinite;
		projection = null;
		if (string.IsNullOrEmpty(rasterFile))
		{
			return false;
		}
		try
		{
			using GDALRaster gDALRaster = new GDALRaster(rasterFile);
			extent = gDALRaster.GetFullNativeExtents();
			projection = gDALRaster.GetProjection();
			return extent.IsFiniteAndValid();
		}
		catch
		{
			return false;
		}
	}

	public static void SetProjection(string filename, Projection proj)
	{
		SetProjectionInfoInternal(filename, proj.GetWKT());
	}

	private static void SetProjectionInfoInternal(string filename, string wkt)
	{
		Dataset dataset = null;
		for (int i = 0; i < 10; i++)
		{
			try
			{
				dataset = Gdal.Open(filename, Access.GA_Update);
			}
			catch
			{
				Thread.Sleep(100);
				continue;
			}
			break;
		}
		if (dataset == null)
		{
			throw new Exception("Cannot open " + filename);
		}
		dataset.SetProjection(wkt);
		dataset.Dispose();
	}

	public static double[] GetLocation(string filename)
	{
		using Dataset dataset = Gdal.Open(filename, Access.GA_ReadOnly);
		if (dataset == null)
		{
			throw new Exception("Cannot open " + filename);
		}
		double[] location = new double[6];
		dataset.GetGeoTransform(location);
		return location;
	}

	public static void SetLocation(string filename, RasterDefinition raster)
	{
		SetLocation(filename, raster.Left, raster.Top, raster.CellSize);
	}

	public static void SetLocation(string filename, double minX, double maxY, double cellSize)
	{
		SetLocation(filename, new double[6]
		{
			minX,
			cellSize,
			0.0,
			maxY,
			0.0,
			0.0 - cellSize
		});
	}

	public static void SetLocation(string filename, double[] geoTransform)
	{
		if (geoTransform.Length != 6)
		{
			throw new Exception("Format unrecognized, need 6 double precision numbers for SetLocationInfo");
		}
		using Dataset srcDs = TryOpenDataset(filename, Access.GA_Update);
		SetLocationInternal(srcDs, geoTransform);
	}

	private static Dataset TryOpenDataset(string filename, Access access)
	{
		for (int i = 0; i < 10; i++)
		{
			try
			{
				Dataset dataset = Gdal.Open(filename, Access.GA_Update);
				if (dataset != null)
				{
					return dataset;
				}
			}
			catch
			{
				Thread.Sleep(100);
			}
		}
		return null;
	}

	private static void SetLocationInternal(Dataset srcDs, double[] geoTransform)
	{
		if (geoTransform[0] == 0.0 && geoTransform[1] == 1.0 && geoTransform[2] == 0.0 && geoTransform[3] == 0.0 && geoTransform[4] == 0.0 && Math.Abs(geoTransform[5]) == 1.0)
		{
			geoTransform[0] = 1E-13;
		}
		srcDs.SetGeoTransform(geoTransform);
	}

	public static void BuildVRT(IList<string> filenames, string outputFilename)
	{
		using (Gdal.BuildVRT(outputFilename, filenames.ToArray(), null, null, ""))
		{
		}
		SetDesiredSharedAttribute(outputFilename);
	}

	private static void SetDesiredSharedAttribute(XElement scope)
	{
	}

	private static void SetDesiredSharedAttribute(string vrtPath)
	{
	}

	public static OperationResult BuildClonedBandVrt(IReadOnlyList<string> sourcePaths, string vrtPath)
	{
		try
		{
			if (sourcePaths.Count == 0)
			{
				return OperationResult.Fail("No source paths provided.");
			}
			bool condition = false;
			foreach (string sourcePath in sourcePaths)
			{
				try
				{
					bool hasDataset;
					using (Dataset dataset = Gdal.BuildVRT(vrtPath, new string[1] { sourcePath }, null, null, null))
					{
						hasDataset = dataset != null;
					}
					if (hasDataset && File.Exists(vrtPath))
					{
						condition = true;
						break;
					}
				}
				catch (Exception)
				{
				}
			}
			if (!condition)
			{
				return OperationResult.Fail("None of the sources could be opened to build the VRT template.");
			}
			XDocument xDocument = XDocument.Load(vrtPath);
			XElement root = xDocument.Root;
			XElement other = root.Elements("VRTRasterBand").First();
			root.Elements("VRTRasterBand").Remove();
			for (int i = 0; i < sourcePaths.Count; i++)
			{
				XElement xElement = new XElement(other);
				xElement.SetAttributeValue("band", i + 1);
				xElement.Descendants("SourceFilename").First().Value = sourcePaths[i];
				root.Add(xElement);
			}
			SetDesiredSharedAttribute(root);
			xDocument.Save(vrtPath);
			return true;
		}
		catch (Exception ex2)
		{
			return OperationResult.Fail("Building a cloned-band VRT", ex2);
		}
	}

	public static void BuildMultibandVRT(IList<string> filenames, string outputFilename)
	{
		if (filenames.Count == 0)
		{
			return;
		}
		string filename = filenames[0];
		using (Dataset dataset = Gdal.BuildVRT(outputFilename, new string[1] { filename }, null, null, null))
		{
			if (dataset.GetProjection() == null)
			{
				using GDALRaster gDALRaster = new GDALRaster(filename, useShared: false);
				if (gDALRaster.TryGuessProjection(out var prj))
				{
					dataset.SetProjection(prj.GetWKT());
				}
			}
		}
		XElement xElement = XElement.Load(outputFilename);
		int elementsXElementTerm = (from b in xElement.Elements("VRTRasterBand").ToList()
			select int.Parse(b.Attribute("band").Value)).ToList().Last();
		elementsXElementTerm++;
		using (GdalRasterCollection collection = new GdalRasterCollection())
		{
			for (int unity = 1; unity < filenames.Count; unity++)
			{
				foreach (XElement bandXML in CreateBandXML(new DatasetIdentifier(filenames[unity]), outputFilename, collection))
				{
					bandXML.SetAttributeValue("band", elementsXElementTerm);
					xElement.Add(bandXML);
					elementsXElementTerm++;
				}
			}
		}
		SetDesiredSharedAttribute(xElement);
		xElement.Save(outputFilename);
	}

	public static OperationResult BuildMultibandVRT(IList<Tuple<BandIdentifier, DateTime>> fileBandPairs, string outputFilename, Dictionary<string, List<string>> bandMetadata = null, GdalRasterCollection collection = null, ProgressReporter prog = null)
	{
		if (bandMetadata == null)
		{
			bandMetadata = new Dictionary<string, List<string>>();
		}
		bandMetadata["Timestamp"] = fileBandPairs.Select((Tuple<BandIdentifier, DateTime> tup3) => tup3.Item2.ToString("yyyy-MM-dd HH:mm:ss")).ToList();
		return BuildMultibandVRT(fileBandPairs.Select((Tuple<BandIdentifier, DateTime> tup3) => tup3.Item1).ToList(), outputFilename, bandMetadata, collection, prog);
	}

	public static OperationResult BuildMultibandVRT(IList<BandIdentifier> bands, string outputFilename, Dictionary<string, List<string>> bandMetadata = null, GdalRasterCollection collection = null, ProgressReporter prog = null)
	{
		try
		{
			if (bands.Count == 0)
			{
				return OperationResult.Fail("No input bands provided.");
			}
			if (prog == null)
			{
				prog = ProgressReporter.None();
			}
			bool condition;
			if (collection == null)
			{
				condition = true;
				collection = new GdalRasterCollection();
			}
			else
			{
				condition = false;
			}
			BandIdentifier bandIdentifier = bands[0];
			if (!File.Exists(bandIdentifier.Dataset.Filename))
			{
				throw new Exception("Input file " + bandIdentifier.Dataset.Filename.InDoubleQuotes() + " does not exist.");
			}
			bool condition2 = false;
			using (GDALBuildVRTOptions buildVrtAppOptions = new GDALBuildVRTOptions(new string[2]
			{
				"-b",
				bandIdentifier.BandIndex.ToString()
			}))
			{
				using Dataset dataset = Gdal.BuildVRT(outputFilename, new string[1] { bandIdentifier.Dataset.ConnectionString }, buildVrtAppOptions, null, null);
				if (string.IsNullOrEmpty(dataset.GetProjection()))
				{
					using GDALRaster gDALRaster = new GDALRaster(bandIdentifier.Dataset.ConnectionString, useShared: false);
					if (gDALRaster.TryGuessProjection(out var prj))
					{
						condition2 = true;
						dataset.SetProjection(prj.GetWKT());
					}
				}
			}
			if (!File.Exists(outputFilename))
			{
				return OperationResult.Fail("Output file " + outputFilename + " could not be created.");
			}
			XElement xElement = XElement.Load(outputFilename);
			XElement xElement2 = xElement.Element("VRTRasterBand");
			if (xElement2 == null)
			{
				return OperationResult.Fail("Error generating XML: 'VRTRasterBand' element not found.");
			}
			if (bandMetadata != null)
			{
				XElement xElement3 = new XElement("Metadata");
				xElement2.Add(xElement3);
				foreach (KeyValuePair<string, List<string>> bandMetadatum in bandMetadata)
				{
					XElement xElement4 = new XElement("MDI");
					xElement3.Add(xElement4);
					xElement4.SetAttributeValue("key", bandMetadatum.Key);
					xElement4.SetValue(bandMetadatum.Value[0]);
				}
			}
			int ordinal = 2;
			for (int i = 1; i < bands.Count; i++)
			{
				BandIdentifier bandIdentifier2 = bands[i];
				XElement xElement5 = CreateBandXML(bandIdentifier2.Dataset, outputFilename, collection)[bandIdentifier2.BandIndex - 1];
				if (bandMetadata != null)
				{
					XElement xElement6 = new XElement("Metadata");
					xElement5.Add(xElement6);
					foreach (KeyValuePair<string, List<string>> bandMetadatum2 in bandMetadata)
					{
						XElement xElement7 = new XElement("MDI");
						xElement6.Add(xElement7);
						xElement7.SetAttributeValue("key", bandMetadatum2.Key);
						xElement7.SetValue(bandMetadatum2.Value[i]);
					}
				}
				xElement5.SetAttributeValue("band", ordinal);
				xElement.Add(xElement5);
				ordinal++;
			}
			SetDesiredSharedAttribute(xElement);
			xElement.Save(outputFilename);
			if (condition2)
			{
				TryRewrapVrtLongitudesTo180(outputFilename);
			}
			if (condition)
			{
				collection.Dispose();
			}
			return true;
		}
		catch (Exception ex)
		{
			prog.ReportError("Unknown exception: " + ex.Message);
			return OperationResult.Fail("Building VRT", ex);
		}
	}

	private static string SanitizeForGDAL(string filename)
	{
		if (File.Exists(filename))
		{
			return filename.InDoubleQuotes();
		}
		if (filename.StartsWith("\"") && filename.EndsWith("\""))
		{
			return filename;
		}
		if (filename.Length >= 3)
		{
			if (filename.StartsWith("."))
			{
				return filename.InDoubleQuotes();
			}
			if (filename[1] == ':' && (filename[2] == '/' || filename[2] == '\\'))
			{
				return filename.InDoubleQuotes();
			}
			if (!filename.Contains(":"))
			{
				return filename.InDoubleQuotes();
			}
		}
		return filename;
	}

	private static List<XElement> CreateBandXML(DatasetIdentifier id, string pathRelativeFrom, GdalRasterCollection collection)
	{
		string relativeConnectionString = id.GetRelativeConnectionString(pathRelativeFrom);
		bool relativeConnectionStringConnectionStringTerm = relativeConnectionString != id.ConnectionString;
		List<XElement> xElements = new List<XElement>();
		GDALRaster gDALRaster = collection.Acquire(id);
		int bandCount = gDALRaster.GetBandCount();
		string[] bandDataType_FriendlyName = gDALRaster.BandDataType_FriendlyName();
		ReadOnlySpan<double> noDataMemory = gDALRaster.GetNoDataMemory().Span;
		for (int i = 0; i < bandCount; i++)
		{
			XElement xElement = new XElement("VRTRasterBand");
			xElement.SetAttributeValue("dataType", bandDataType_FriendlyName[i]);
			XElement content = new XElement("NoDataValue", noDataMemory[i]);
			xElement.Add(content);
			XElement xElement2 = new XElement("ComplexSource");
			xElement.Add(xElement2);
			XElement xElement3 = new XElement("SourceFilename", relativeConnectionString);
			xElement2.Add(xElement3);
			xElement3.SetAttributeValue("relativeToVRT", relativeConnectionStringConnectionStringTerm ? "1" : "0");
			XElement content2 = new XElement("SourceBand", (i + 1).ToString());
			xElement2.Add(content2);
			XElement xElement4 = new XElement("SourceProperties");
			xElement2.Add(xElement4);
			xElement4.SetAttributeValue("RasterXSize", gDALRaster.PixelsWide);
			xElement4.SetAttributeValue("RasterYSize", gDALRaster.PixelsTall);
			xElement4.SetAttributeValue("DataType", bandDataType_FriendlyName[i]);
			gDALRaster.GetBlockSize(i + 1, out var blockXSize, out var blockYSize);
			xElement4.SetAttributeValue("BlockXSize", blockXSize);
			xElement4.SetAttributeValue("BlockYSize", blockYSize);
			XElement xElement5 = new XElement("SrcRect");
			xElement2.Add(xElement5);
			xElement5.SetAttributeValue("xOff", 0);
			xElement5.SetAttributeValue("yOff", 0);
			xElement5.SetAttributeValue("xSize", gDALRaster.PixelsWide);
			xElement5.SetAttributeValue("ySize", gDALRaster.PixelsTall);
			XElement xElement6 = new XElement("DstRect");
			xElement2.Add(xElement6);
			xElement6.SetAttributeValue("xOff", 0);
			xElement6.SetAttributeValue("yOff", 0);
			xElement6.SetAttributeValue("xSize", gDALRaster.PixelsWide);
			xElement6.SetAttributeValue("ySize", gDALRaster.PixelsTall);
			XElement content3 = new XElement("NODATA", noDataMemory[i]);
			xElement2.Add(content3);
			xElements.Add(xElement);
		}
		return xElements;
	}

	public static void FindMatchingFiles(string keyFile, IList<string> candidateFiles, out List<string> matchingCandidates, GdalRasterCollection collection = null, ProgressReporter reporter = null)
	{
		if (string.IsNullOrEmpty(keyFile))
		{
			throw new ArgumentNullException("keyFile");
		}
		if (candidateFiles == null)
		{
			throw new ArgumentNullException("candidateFiles");
		}
		matchingCandidates = new List<string>();
		if (candidateFiles.Count == 0)
		{
			return;
		}
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		bool condition;
		if (collection == null)
		{
			condition = true;
			collection = new GdalRasterCollection();
		}
		else
		{
			condition = false;
		}
		GDALRaster gDALRaster = collection.Acquire(keyFile);
		int pixelsWide = gDALRaster.PixelsWide;
		int pixelsTall = gDALRaster.PixelsTall;
		Type dataType = gDALRaster.GetDataType();
		Extent fullNativeExtents = gDALRaster.GetFullNativeExtents();
		for (int i = 0; i < candidateFiles.Count; i++)
		{
			string candidateFile = candidateFiles[i];
			try
			{
				GDALRaster gDALRaster2 = collection.Acquire(candidateFile);
				if (gDALRaster2.PixelsWide != pixelsWide)
				{
					PrintError(candidateFile, "Pixels Wide", gDALRaster2.PixelsWide, pixelsWide);
					continue;
				}
				if (gDALRaster2.PixelsTall != pixelsTall)
				{
					PrintError(candidateFile, "Pixels Tall", gDALRaster2.PixelsTall, pixelsTall);
					continue;
				}
				Type dataType2 = gDALRaster2.GetDataType();
				if (dataType2 != dataType)
				{
					PrintError(candidateFile, "Data Type", dataType2, dataType);
					continue;
				}
				Extent fullNativeExtents2 = gDALRaster2.GetFullNativeExtents();
				if (!fullNativeExtents2.ToleranceEquals(fullNativeExtents))
				{
					PrintError(candidateFile, "Extent", fullNativeExtents2, fullNativeExtents);
				}
				else
				{
					matchingCandidates.Add(candidateFile);
				}
			}
			catch (Exception ex)
			{
				reporter.ReportError(candidateFile.InDoubleQuotes() + " could not be read. " + ex.Message);
			}
		}
		if (condition)
		{
			collection.Dispose();
		}
		void PrintError(string compareFilename, string paramName, object baseVal, object newVal)
		{
			reporter.ReportError(compareFilename.InDoubleQuotes() + " rejected because " + paramName + " doesn't match base file. (" + baseVal.ToString() + " vs " + newVal.ToString() + ")");
		}
	}

	public static List<List<int>> GetRegisteredFiles(IList<string> filenames)
	{
		List<List<int>> registeredFiles = new List<List<int>>();
		(RasterDefinition, Projection)[] fileMetadata = new (RasterDefinition, Projection)[filenames.Count];
		for (int i = 0; i < filenames.Count; i++)
		{
			using GDALRaster gDALRaster = new GDALRaster(filenames[i]);
			fileMetadata[i] = (gDALRaster.GetNativeRaster(), gDALRaster.GetProjection());
		}
		List<(Projection, List<int>)> valueTuples = new List<(Projection, List<int>)>();
		List<int> intList = null;
		for (int j = 0; j < filenames.Count; j++)
		{
			Projection projection = fileMetadata[j].Item2;
			if (projection == null)
			{
				if (intList == null)
				{
					intList = new List<int>();
				}
				intList.Add(j);
				continue;
			}
			bool condition = false;
			foreach (var localValue in valueTuples)
			{
				if (localValue.Item1.IsEqual(projection))
				{
					localValue.Item2.Add(j);
					condition = true;
					break;
				}
			}
			if (!condition)
			{
				valueTuples.Add((projection, new List<int>(new _003C_003Ez__ReadOnlySingleElementList<int>(j))));
			}
		}
		if (!intList.IsNullOrEmpty())
		{
			valueTuples.Add((null, intList));
		}
		foreach (var localValue2 in valueTuples)
		{
			List<int> fileIdxs = localValue2.Item2;
			if (fileIdxs.Count == 1)
			{
				int unity = 1;
				List<int> intListList = new List<int>(unity);
				CollectionsMarshal.SetCount(intListList, unity);
				Span<int> intListSpan = CollectionsMarshal.AsSpan(intListList);
				int index = 0;
				intListSpan[index] = fileIdxs[0];
				registeredFiles.Add(intListList);
				continue;
			}
			foreach (List<int> registeredCollection in RasterDefinition.GetRegisteredCollections(fileIdxs.Select((int ii) => fileMetadata[ii].Item1).ToList(), 1E-05, 0.5))
			{
				registeredFiles.Add(registeredCollection.Select((int ii) => fileIdxs[ii]).ToList());
			}
		}
		return registeredFiles;
	}

	public static string GetDriverName(string filename)
	{
		Dataset dataset = Gdal.Open(filename, Access.GA_ReadOnly);
		if (dataset == null)
		{
			throw new Exception("Cannot open " + filename);
		}
		Driver driver = dataset.GetDriver();
		if (driver == null)
		{
			dataset.Dispose();
			throw new Exception("Cannot identify driver for " + filename);
		}
		string shortName = driver.ShortName;
		driver.Dispose();
		dataset.Dispose();
		return shortName;
	}

	public static string GetLinearUnitsName(string filename)
	{
		using Dataset dataset = Gdal.Open(filename, Access.GA_ReadOnly);
		if (dataset == null)
		{
			throw new Exception("Cannot open " + filename);
		}
		using SpatialReference spatialReference = new SpatialReference(dataset.GetProjectionRef());
		return spatialReference.GetLinearUnitsName();
	}

	public static void DebugTestMultithread(string fn)
	{
		GdalBandedRaster<float> gbr = GdalBandedRaster<float>.Pooled(fn, out var _);
		gbr.AllowParallelResampling = true;
		RasterDefinition baseRaster = gbr.RasterDefinition;
		int imgH = baseRaster.Rows;
		int imgW = baseRaster.Cols;
		int getH = 5;
		int getW = 5;
		Random rng = new Random(0);
		System.Threading.Tasks.Parallel.For(0, 10000, (int i) =>
		{
			float[] floatValues = new float[getH * getW];
			double nextDouble;
			double nextDouble2;
			lock (rng)
			{
				nextDouble = rng.NextDouble();
				nextDouble2 = rng.NextDouble();
			}
			int nextDoubleImgHTerm = (int)Math.Round(nextDouble * (double)(imgH - getH - 1));
			int nextDouble2ImgWTerm = (int)Math.Round(nextDouble2 * (double)(imgW - getW - 1));
			PixelRange pxRange = new PixelRange(nextDoubleImgHTerm, nextDoubleImgHTerm + getH - 1, nextDouble2ImgWTerm, nextDouble2ImgWTerm + getW - 1);
			ResampleArgs args = new ResampleArgs(baseRaster.GetSubRaster(pxRange));
			gbr.Resample(args, 0, floatValues);
		});
	}

	private static void GetOptimalCuts(int pixelsWide, int pixelsTall, int goalPieces, out int numPiecesWide, out int numPiecesTall)
	{
		int nVertSlices = 0;
		int nHorizSlices = 0;
		Func<int> func = () => (nVertSlices + 1) * (nHorizSlices + 1);
		Func<float> func2 = () => (float)pixelsWide / ((float)nVertSlices + 1f);
		Func<float> func3 = () => (float)pixelsTall / ((float)nHorizSlices + 1f);
		while (func() < goalPieces)
		{
			if (func2() > func3())
			{
				nVertSlices++;
			}
			else
			{
				nHorizSlices++;
			}
		}
		numPiecesWide = nVertSlices + 1;
		numPiecesTall = nHorizSlices + 1;
	}

	private static Gdal.GDALProgressFuncDelegate GetGDALProgressDelegate(ProgressReporter reporter)
	{
		if (reporter == null)
		{
			return null;
		}
		return ((Func<double, nint, nint, int>)((double complete, nint idk1, nint idk2) =>
		{
			reporter.ReportProgress((int)(complete * 100.0));
			return 1;
		})).Invoke;
	}

	private static Dataset OpenMemoryRaster<T>(T[] arr, int width, int height, out GCHandle pinnedPtr) where T : unmanaged
	{
		if (arr == null)
		{
			throw new ArgumentNullException("arr");
		}
		if (arr.Length != width * height)
		{
			throw new ArgumentException("Array is not the correct length.");
		}
		string gDALTypeName = GetGDALTypeName<T>();
		pinnedPtr = GCHandle.Alloc(arr, GCHandleType.Pinned);
		return Gdal.OpenShared("MEM:::DATAPOINTER=" + (IntPtr)pinnedPtr.AddrOfPinnedObject() + ",PIXELS=" + width + ",LINES=" + height + ",BANDS=1,DATATYPE=" + gDALTypeName, Access.GA_ReadOnly);
	}

	private static Dataset OpenMemoryRaster<T>(T[,] arr, out GCHandle pinnedPtr) where T : unmanaged
	{
		if (arr == null)
		{
			throw new ArgumentNullException("arr");
		}
		string gDALTypeName = GetGDALTypeName<T>();
		pinnedPtr = GCHandle.Alloc(arr, GCHandleType.Pinned);
		return Gdal.OpenShared("MEM:::DATAPOINTER=" + (IntPtr)pinnedPtr.AddrOfPinnedObject() + ",PIXELS=" + arr.GetLength(1) + ",LINES=" + arr.GetLength(0) + ",BANDS=1,DATATYPE=" + gDALTypeName, Access.GA_ReadOnly);
	}

	private static Dataset OpenMemoryRaster<T>(T[] arr, int width, int height, int bands, out GCHandle pinnedPtr) where T : unmanaged
	{
		if (arr == null)
		{
			throw new ArgumentNullException("arr");
		}
		if (arr.Length != width * height * bands)
		{
			throw new ArgumentException("Array is not the correct length.");
		}
		string gDALTypeName = GetGDALTypeName<T>();
		pinnedPtr = GCHandle.Alloc(arr, GCHandleType.Pinned);
		return Gdal.OpenShared("MEM:::DATAPOINTER=" + (IntPtr)pinnedPtr.AddrOfPinnedObject() + ",PIXELS=" + width + ",LINES=" + height + ",BANDS=" + bands + ",DATATYPE=" + gDALTypeName, Access.GA_ReadOnly);
	}

	public static GDALRaster FromMemory(byte[] fileBytes)
	{
		string filename = VirtualFile.VirtualFileNamePool.Acquire();
		Gdal.FileFromMemBuffer(filename, fileBytes);
		Dataset dataset = Gdal.OpenShared(filename, Access.GA_ReadOnly);
		dataset.SetGeoTransform(new double[6] { 0.0, 1.0, 0.0, 0.0, 0.0, -1.0 });
		return new GDALRaster(dataset)
		{
			Filename = filename
		};
	}

	public unsafe static GDALRaster FromMemory(Span<byte> fileBytes)
	{
		string filename = VirtualFile.VirtualFileNamePool.Acquire();
		fixed (byte* pabyData = fileBytes)
		{
			Gdal.FileFromMemBuffer(filename, fileBytes.Length, (nint)pabyData);
		}
		Dataset dataset = Gdal.OpenShared(filename, Access.GA_ReadOnly);
		dataset.SetGeoTransform(new double[6] { 0.0, 1.0, 0.0, 0.0, 0.0, -1.0 });
		return new GDALRaster(dataset)
		{
			Filename = filename
		};
	}

	private static string GetGDALTypeName<T>()
	{
		Type typeFromHandle = typeof(T);
		if (typeFromHandle == typeof(float))
		{
			return "Float32";
		}
		if (typeFromHandle == typeof(double))
		{
			return "Float64";
		}
		if (typeFromHandle == typeof(int))
		{
			return "Int32";
		}
		if (typeFromHandle == typeof(long))
		{
			return "Int64";
		}
		if (typeFromHandle == typeof(byte))
		{
			return "Byte";
		}
		if (typeFromHandle == typeof(short))
		{
			return "Int16";
		}
		throw new NotImplementedException();
	}

	public IMultiBandRasterProvider<T> AsBandedRasterProvider<T>() where T : unmanaged, IEquatable<T>
	{
		return new GdalBandedRasterDataSource<T>(this, Drivers.IsParallelReadSafe(DriverShortName));
	}

	public IRasterProvider<T> AsRasterProvider<T>(int bandIndex = 1) where T : unmanaged, IEquatable<T>
	{
		return new GdalBandedRasterDataSource<T>(this, Drivers.IsParallelReadSafe(DriverShortName)).AsRasterProvider(bandIndex - 1);
	}

	public bool IsVirtualMemory()
	{
		return Filename.StartsWith("/vsimem");
	}

	public bool IsVirtual()
	{
		return Filename.StartsWith("/vsi");
	}

	public void DebugRefresh()
	{
		_srcDs.Dispose();
		_srcDs = Gdal.OpenShared(Filename, Access.GA_ReadOnly);
		lock (_noDataLock)
		{
			_noDataCache = null;
		}
	}

	private void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			_srcDs?.Dispose();
			_srcDrv?.Dispose();
			_srcDs = null;
			_srcDrv = null;
			if (_pinnedArray.IsAllocated)
			{
				_pinnedArray.Free();
			}
			if (IsVirtualMemory() && Gdal.Unlink(Filename) != 0)
			{
				throw new Exception();
			}
			_isDisposed = true;
		}
	}

	~GDALRaster()
	{
		Dispose(disposing: false);
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
