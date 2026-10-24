using System.Data;
using System.IO;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Systems;

namespace RasMapperLib;

public class RiverStations : PointFeatureLayer, IGeometryLayer
{
	private const string sStation = "Station";

	private const string sDistanceAlongRiver = "Distance along Reach";

	private const string GroupName = "Geometry/River Stationing";

	public RASGeometry Geometry { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override string DefaultLayerName => "River Station Markers";

	public string HDFGroupName => "Geometry/River Stationing";

	public RiverStations(RASGeometry geometry)
		: base("River Station Markers", geometry.SourceFilename)
	{
		ForwardEditRequestsToParent = true;
		Geometry = geometry;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddAttributeColumn("Station", typeof(float));
		AddAttributeColumn("River", typeof(string), null, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Reach", typeof(string), null, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Distance along Reach", typeof(float), null, DataColumnViewState.GrayedOut);
	}

	protected void HDFLoadFeatureTable(H5Reader hr)
	{
		HDF5Storage.ReadPointFeatures(this, hr, "Geometry/River Stationing/Points");
		HDF5Storage.ReadColumns(this, hr, "Geometry/River Stationing/Stations", "Station");
		Geometry.UpdateRiverStationingAssociations();
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	protected void HDFSaveFeatureTable(H5Writer hw)
	{
		if (FeatureCount() == 0)
		{
			hw.Delete(HDFGroupName);
			return;
		}
		hw.CreateGroup("Geometry/River Stationing");
		HDF5Storage.WritePointFeatures(this, hw, "Geometry/River Stationing/Points");
		HDF5Storage.WriteColumns(this, hw, "Geometry/River Stationing/Stations", "Station");
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	public void Stream(BinaryWriter bw)
	{
		bw.Write(FeatureCount());
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Point(i).Stream(bw);
				bw.Write(GetUserStation(i));
				Pipe.WriteString(bw, GetRiver(i));
				Pipe.WriteString(bw, GetReach(i));
				bw.Write(GetDistanceAlongReach(i));
			}
		}
	}

	public static RiverStations Stream(BinaryReader br, RASGeometry geom)
	{
		RiverStations riverStations = new RiverStations(geom);
		riverStations.InitializeEmptyFeatureTable();
		riverStations.SuppressTableReload(boolValue: true);
		riverStations.SuppressFeatureChanged(boolValue: true);
		checked
		{
			int count = br.ReadInt32() - 1;
			for (int i = 0; i <= count; i++)
			{
				Point feature = RasMapperLib.Point.Stream(br);
				riverStations.AddFeature(feature);
				float readSingle = br.ReadSingle();
				string riv = Pipe.ReadString(br);
				string reach = Pipe.ReadString(br);
				float actualStation = br.ReadSingle();
				riverStations.SetRiver(i, riv, reach, actualStation);
				riverStations.SetUserStation(i, readSingle);
			}
			return riverStations;
		}
	}

	public void SetRiver(int fid, string riv, string reach, float actualStation)
	{
		DataRow dataRow = FeatureRow(fid);
		dataRow["River"] = riv;
		dataRow["Reach"] = reach;
		dataRow["Distance along Reach"] = actualStation;
	}

	public string GetRiver(int fid)
	{
		return FeatureRow(fid)["River"] as string;
	}

	public string GetReach(int fid)
	{
		return FeatureRow(fid)["Reach"] as string;
	}

	public float GetUserStation(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Station");
	}

	public void SetUserStation(int fid, float userStation)
	{
		FeatureRow(fid)["Station"] = userStation;
	}

	public float GetDistanceAlongReach(int fid)
	{
		return Conversions.ToSingle(FeatureRow(fid)["Distance along Reach"]);
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		if (args.ChangeType == FeatureChangedType.FeatureDeleted)
		{
			return;
		}
		foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
		{
			Geometry.UpdateRiverStationingAssociations(featureIndicesChanged);
		}
	}
}
