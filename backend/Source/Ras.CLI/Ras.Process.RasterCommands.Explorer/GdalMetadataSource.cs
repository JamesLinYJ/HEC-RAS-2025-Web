using System;
using System.Collections.Generic;
using System.IO;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using Geospatial.Vectors;
using OSGeo.GDAL;

namespace Ras.Process.RasterCommands.Explorer;

public class GdalMetadataSource : IRasterMetadataSource, IDisposable
{
	private readonly GDALRaster _raster;

	private readonly bool _writable;

	private readonly string _connectionString;

	public string DriverLabel { get; }

	public string DisplayName { get; }

	public bool CanEditMetadata => _writable;

	public bool CanEditGeoreference => _writable;

	public bool SupportsTilePreview => false;

	public bool TryGetTilePreview(out TilePreview preview, out string error)
	{
		preview = null;
		error = "The tile-coverage preview is only available with the native driver.";
		return false;
	}

	public GdalMetadataSource(string connectionString, string displayName)
	{
		_connectionString = connectionString;
		DisplayName = displayName;
		GDALRaster gDALRaster = null;
		bool writable = false;
		try
		{
			gDALRaster = new GDALRaster(connectionString, Access.GA_Update, useShared: false);
			writable = true;
		}
		catch
		{
			gDALRaster = new GDALRaster(connectionString, Access.GA_ReadOnly, useShared: false);
			writable = false;
		}
		_raster = gDALRaster;
		_writable = writable;
		DriverLabel = "GDAL (" + _raster.DriverShortName + ")";
	}

	public RasterSummary GetSummary()
	{
		int bandCount = _raster.GetBandCount();
		string dataType = ((bandCount > 0) ? _raster.BandDataType_FriendlyName()[0] : "unknown");
		return new RasterSummary(_raster.PixelsWide, _raster.PixelsTall, bandCount, dataType);
	}

	public IReadOnlyList<SubdatasetEntry> GetSubdatasets()
	{
		List<SubdatasetEntry> subdatasetEntries = new List<SubdatasetEntry>();
		int unity = 1;
		foreach (KeyValuePair<string, string> subDataset in _raster.GetSubDatasets())
		{
			subdatasetEntries.Add(new SubdatasetEntry(unity, subDataset.Key, subDataset.Value, subDataset.Key));
			unity++;
		}
		return subdatasetEntries;
	}

	public GeoreferenceInfo GetGeoreference()
	{
		try
		{
			bool hasGeoreference = _raster.IsGeoReferenced();
			Extent fullNativeExtents = _raster.GetFullNativeExtents();
			return new GeoreferenceInfo(hasGeoreference, fullNativeExtents.MinX, fullNativeExtents.MaxY, _raster.GetCellSizeX());
		}
		catch
		{
			return new GeoreferenceInfo(HasGeoreference: false, double.NaN, double.NaN, double.NaN);
		}
	}

	public ProjectionInfo GetProjection()
	{
		Projection projection = _raster.GetProjection();
		if (projection == null)
		{
			return new ProjectionInfo(HasProjection: false, "", 0, "", "", "");
		}
		int epsg = (projection.TryGetEPSGCode(out var epsg2) ? epsg2 : 0);
		string units = "";
		try
		{
			units = projection.GetLinearUnitsName();
		}
		catch
		{
		}
		string wkt = "";
		try
		{
			wkt = projection.GetPrettyWKT();
		}
		catch
		{
		}
		return new ProjectionInfo(HasProjection: true, projection.GetName(), epsg, units, wkt, "");
	}

	public IReadOnlyList<MetadataEntry> GetMetadata()
	{
		List<MetadataEntry> metadataEntries = new List<MetadataEntry>();
		foreach (KeyValuePair<string, string> metadataDictionary in _raster.GetMetadataDictionary())
		{
			metadataEntries.Add(new MetadataEntry(metadataDictionary.Key, metadataDictionary.Value, "", _writable));
		}
		ReadOnlySpan<double> noDataMemory = _raster.GetNoDataMemory().Span;
		if (noDataMemory.Length > 0 && !double.IsNaN(noDataMemory[0]))
		{
			metadataEntries.Add(new MetadataEntry("NoData (band 1)", noDataMemory[0].ToString(), "", IsEditable: false));
		}
		return metadataEntries;
	}

	public IReadOnlyList<PyramidLevelInfo> GetPyramidLevels()
	{
		List<PyramidLevelInfo> pyramidLevelInfos = new List<PyramidLevelInfo>();
		List<RasterDefinition> pyramidLevels = _raster.GetPyramidDefinition().PyramidLevels;
		for (int i = 0; i < pyramidLevels.Count; i++)
		{
			pyramidLevelInfos.Add(new PyramidLevelInfo(i, pyramidLevels[i].Cols, pyramidLevels[i].Rows, pyramidLevels[i].CellSize, Array.Empty<TiffTagEntry>()));
		}
		return pyramidLevelInfos;
	}

	public bool TrySetMetadataItem(string key, string stringValue, out string error)
	{
		error = null;
		if (!_writable)
		{
			error = "Raster is open read-only; metadata can't be edited.";
			return false;
		}
		try
		{
			_raster.SetMetadataItem(key, stringValue);
			_raster.FlushCache();
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	public bool TrySetGeoreference(double originX, double originY, double cellSize, out string error)
	{
		error = null;
		if (!_writable)
		{
			error = "Raster is open read-only; georeferencing can't be edited.";
			return false;
		}
		if (cellSize <= 0.0)
		{
			error = "Cell size must be greater than zero.";
			return false;
		}
		try
		{
			RasterDefinition rasterDefinition = new RasterDefinition(new Point(originX, originY), cellSize, _raster.PixelsTall, _raster.PixelsWide);
			_raster.SetRasterDefinition(rasterDefinition);
			_raster.FlushCache();
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	public bool TrySetTiffTag(int levelIndex, int tagId, string stringValue, out string error)
	{
		error = "Editing raw TIFF tags is only supported by the native driver.";
		return false;
	}

	public bool TryOpenSubdataset(SubdatasetEntry entry, out IRasterMetadataSource sub, out string error)
	{
		error = null;
		sub = null;
		try
		{
			sub = new GdalMetadataSource(entry.ConnectionString, Path.GetFileName(DisplayName) + " : " + entry.Name);
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	public void Dispose()
	{
		_raster?.Dispose();
	}
}
