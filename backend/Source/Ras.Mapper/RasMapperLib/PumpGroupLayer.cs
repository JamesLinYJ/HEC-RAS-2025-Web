using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using H5Assist.VB;
using Microsoft.VisualBasic;
using RasMapperLib.Structures;

namespace RasMapperLib;

public class PumpGroupLayer : DependentPolygonFeatureLayer, IGeometryLayer, IGroupLayer
{
	public class Cols
	{
		public const string PumpStationID = "Pump Station ID";

		public const string PumpGroupName = "Name";

		public const string BiasOn = "Bias On";

		public const string StartupTime = "Startup Time";

		public const string ShutdownTime = "Shutdown Time";

		public const string Width = "Width";

		public const string EfficiencyCurves = "Efficiency Curves";
	}

	public const string HDFGroupName = "Geometry/Pump Stations/Pump Groups";

	public const string sPumpGroups = "Pump Groups";

	public override string SimpleFeatureTypeName { get; set; }

	public RASGeometry Geometry { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override string DefaultLayerName => "Pump Groups";
string IGeometryLayer.HDFGroupName=> "Geometry/Pump Stations/Pump Groups";

	public PumpGroupLayer(RASGeometry geometry)
		: base("Pump Groups", geometry.SourceFilename)
	{
		SimpleFeatureTypeName = "Group";
		ForwardEditRequestsToParent = true;
		Geometry = geometry;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		DataColumnViewState viewState = DataColumnViewState.GrayedOut;
		AddManagedAttributeColumn("Pump Station ID", "Pump Station ID", typeof(int), null, viewState);
		AddManagedAttributeColumn("Name", "Name", typeof(string), null, viewState);
		AddManagedAttributeColumn("Bias On", "Bias On", typeof(bool), null, viewState);
		AddManagedAttributeColumn("Startup Time", "Start Up Time", typeof(float), null, viewState);
		AddManagedAttributeColumn("Shutdown Time", "Shut Down Time", typeof(float), null, viewState);
		AddManagedAttributeColumn("Width", "Width", typeof(float), null, viewState);
		AddAttributeColumn("Efficiency Curves", typeof(Point2DCollection));
	}

	protected void HDFLoadFeatureTable(H5Reader hr)
	{
		if (!hr.ExistsAsGroup("Geometry/Pump Stations/Pump Groups"))
		{
			return;
		}
		string path = "Geometry/Pump Stations/Pump Groups/Attributes";
		checked
		{
			if (hr.ExistsAsDataset(path))
			{
				int count = (int)hr.GetDatasetDimensions(path)[0] - 1 - FeatureCount();
				for (int i = 0; i <= count; i++)
				{
					AddFeature(RasMapperLib.Polygon.NullPolygon());
				}
				ReadManagedColumns(hr, path);
				List<List<Point2D>> pointCollection = HDF5Storage.ReadPointCollection(hr, "Geometry/Pump Stations/Pump Groups/Efficiency Curves");
				int count2 = pointCollection.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					SetEfficiencyCurves(j, new Point2DCollection(pointCollection[j]));
				}
			}
		}
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
			hw.Delete("Geometry/Pump Stations/Pump Groups");
			return;
		}
		hw.CreateGroup("Geometry/Pump Stations/Pump Groups");
		WriteManagedColumns(hw, "Geometry/Pump Stations/Pump Groups/Attributes");
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		List<Tuple<int, int>> oldNewIndexMap = args.GetOldNewIndexMap(FeatureCount());
		foreach (Tuple<int, int> oldNewIndexMap2 in oldNewIndexMap)
		{
			Geometry.Pumps.UpdateAssociations(oldNewIndexMap2.Item1, oldNewIndexMap2.Item2);
		}
	}

	protected override List<string> GetColumnDependecies()
	{
		return new List<string>(new string[1] { "Width" });
	}

	protected override List<FeatureLayer> GetLayerDependencies()
	{
		return new List<FeatureLayer>(new FeatureLayer[1] { Geometry.Pumps });
	}

	protected override Polygon RecomputePolygon(int fid)
	{
		return StructureElementLayerExtensions.RecomputeGroupPolygon(this, Geometry.Pumps, fid);
	}

	public override string GetFeatureName(int fid)
	{
		return GetGroupName(fid);
	}

	public override void SetFeatureName(int fid, string newName)
	{
		SetGroupName(fid, newName);
	}

	public int AddNewGroup(int parentStructureIndex, string groupName = "")
	{
		return IGroupLayerExtensions.AddNewGroup(this, parentStructureIndex, groupName);
	}

	int IGroupLayer.AddNewGroup(int parentStructureIndex,string groupName )
	{
		//ILSpy generated this explicit interface implementation from .override directive in AddNewGroup
		return this.AddNewGroup(parentStructureIndex, groupName);
	}

	public void SetAssociatedStructure(int fid, int parentStructureIndex)
	{
		SetActualStructureIndex(fid, parentStructureIndex);
	}

	void IGroupLayer.SetAssociatedStructure(int fid,int parentStructureIndex)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetAssociatedStructure
		this.SetAssociatedStructure(fid, parentStructureIndex);
	}

	public void UpdateAssociations(int fromStructureFID, int toStructureFID)
	{
		if (fromStructureFID == -1)
		{
			return;
		}
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (TryGetStructureIndex(i) == fromStructureFID)
				{
					SetActualStructureIndex(i, toStructureFID);
				}
			}
		}
	}

	void IGroupLayer.UpdateAssociations(int fromStructureFID,int toStructureFID)
	{
		//ILSpy generated this explicit interface implementation from .override directive in UpdateAssociations
		this.UpdateAssociations(fromStructureFID, toStructureFID);
	}

	public int TryGetStructureIndex(int fid)
	{
		return DataTableExtensions.GetInteger(FeatureTable(), fid, "Pump Station ID", -1);
	}

	int IStructureElementLayer.TryGetStructureIndex(int fid)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TryGetStructureIndex
		return this.TryGetStructureIndex(fid);
	}

	private void SetActualStructureIndex(int fid, int idx)
	{
		FeatureRow(fid)["Pump Station ID"] = idx;
	}

	void IGroupLayer.SetActualStructureIndex(int fid,int idx)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetActualStructureIndex
		this.SetActualStructureIndex(fid, idx);
	}

	public IStructureLayer GetAssociatedStructureLayer()
	{
		return Geometry.PumpStations;
	}

	IStructureLayer IStructureElementLayer.GetAssociatedStructureLayer()
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetAssociatedStructureLayer
		return this.GetAssociatedStructureLayer();
	}

	public string GetGroupName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Name");
	}

	public void SetGroupName(int fID, string sta)
	{
		FeatureRow(fID)["Name"] = sta;
	}

	public float GetWidth(int fID)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fID, "Width");
	}

	float IGroupLayer.GetWidth(int fID)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetWidth
		return this.GetWidth(fID);
	}

	public void SetWidth(int fID, float width)
	{
		FeatureRow(fID)["Width"] = width;
	}

	void IGroupLayer.SetWidth(int fID,float width)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetWidth
		this.SetWidth(fID, width);
	}

	public int GetPumpStationID(int fid)
	{
		return DataTableExtensions.GetInteger(FeatureTable(), fid, "Pump Station ID");
	}

	public void SetPumpStationID(int fid, int id)
	{
		FeatureRow(fid)["Pump Station ID"] = id;
	}

	public Point2DCollection GetEfficiencyCurves(int fid)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(FeatureRow(fid)["Efficiency Curves"]);
		if (objectValue == null || Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			return null;
		}
		return objectValue as Point2DCollection;
	}

	public void SetEfficiencyCurves(int fid, Point2DCollection curve)
	{
		FeatureRow(fid)["Efficiency Curves"] = curve;
	}
}
