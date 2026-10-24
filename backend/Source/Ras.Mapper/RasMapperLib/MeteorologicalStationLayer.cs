using System.Collections.Generic;
using H5Assist.VB;

namespace RasMapperLib;

public class MeteorologicalStationLayer : PointFeatureLayer
{
	private const string HDFGroupName = "Event Conditions/Meteorology";

	private const string sStationName = "Station Name";

	private const string sGageHeight = "Gage Height";

	private const string sLatitude = "Latitude";

	private const string sLongitude = "Longitude";

	private const string sXValue = "X (Native)";

	private const string sYValue = "Y (Native)";

	private RasterM _defaultMetRasterization;

	public RasterM DefaultMetRasterization
	{
		get
		{
			EnsureDataLoaded();
			return _defaultMetRasterization;
		}
	}

	public static MeteorologicalStationLayer TryParseMetLayer(H5Reader hr)
	{
		string absolutePath = "Event Conditions/Meteorology/Meteorology Stations";
		if (!hr.ExistsAsDataset(absolutePath))
		{
			return null;
		}
		return new MeteorologicalStationLayer(hr.Filename);
	}

	public MeteorologicalStationLayer()
	{
	}

	public MeteorologicalStationLayer(string sourceFilename)
		: base("Meteorological Stations", sourceFilename)
	{
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddManagedAttributeColumn("Station Name", "Station Name", typeof(string));
		AddManagedAttributeColumn("Gage Height", "Gage Height", typeof(float));
		AddManagedAttributeColumn("Latitude", "Latitude", typeof(double));
		AddManagedAttributeColumn("Longitude", "Longitude", typeof(double));
		AddManagedAttributeColumn("X (Native)", "X", typeof(double));
		AddManagedAttributeColumn("Y (Native)", "Y", typeof(double));
	}

	protected override void LoadFeatureTable()
	{
		if (!SourceFileExists)
		{
			return;
		}
		checked
		{
			using H5Reader h5Reader = new H5Reader(SourceFilename);
			string path = "Event Conditions/Meteorology/Meteorology Stations";
			if (h5Reader.ExistsAsDataset(path))
			{
				HDF5Storage.TryReadRaster(h5Reader, "Event Conditions/Meteorology", ref _defaultMetRasterization, "Raster Parameter");
				int datasetDimension = (int)h5Reader.GetDatasetDimensions(path)[0];
				int count = datasetDimension - 1;
				for (int i = 0; i <= count; i++)
				{
					AddFeature(new Point(0.0, 0.0));
				}
				ReadManagedColumns(h5Reader, path);
				int count2 = datasetDimension - 1;
				for (int j = 0; j <= count2; j++)
				{
					SetFeature(j, GetPointFromColumns(j));
				}
			}
		}
	}

	private void EnsureDataLoaded()
	{
		FeatureTable();
	}

	public List<string> GetMetDatasetNames(string metTypeName)
	{
		List<string> metDatasetNames = new List<string>();
		string getMetFolderNameRasEventConditionsTerm = RASEventConditions.GetMetFolderName(metTypeName) + "/";
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				string stationName = GetStationName(i);
				metDatasetNames.Add(getMetFolderNameRasEventConditionsTerm + stationName);
			}
			return metDatasetNames;
		}
	}

	public List<string> GetWindVelocityXDatasetNames()
	{
		return GetMetDatasetNames("Wind Velocity X");
	}

	public List<string> GetWindVelocityYDatasetNames()
	{
		return GetMetDatasetNames("Wind Velocity Y");
	}

	public List<string> GetWindSpeedDatasetNames()
	{
		return GetMetDatasetNames("Wind Speed");
	}

	public List<string> GetWindDirectionDatasetNames()
	{
		return GetMetDatasetNames("Wind Direction");
	}

	public override string GetFeatureName(int fid)
	{
		return GetStationName(fid);
	}

	public string GetStationName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Station Name");
	}

	private Point GetPointFromColumns(int fid)
	{
		double x = DataTableExtensions.GetDouble(FeatureTable(), fid, "X (Native)");
		double y = DataTableExtensions.GetDouble(FeatureTable(), fid, "Y (Native)");
		return new Point(x, y);
	}
}
