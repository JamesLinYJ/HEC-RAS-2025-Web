using H5Assist.VB;

namespace RasMapperLib;

public abstract class MeshPolygonLayer : PolygonFeatureLayer, IGeometryLayer
{
	public const string sCellSizeX = "Cell Size X";

	public const string sCellSizeY = "Cell Size Y";

	public const string sCellShiftX = "Cell Shift DX";

	public const string sCellShiftY = "Cell Shift DY";

	private const string sGenerateHexagon = "Generate as Hexagon";

	public RASGeometry Geometry { get; }

	public abstract string HDFGroupName { get; }

	private string IGeometryLayer_HDFGroupName => HDFGroupName;

	public override bool FeaturesRequireUniqueNames { get; set; }

	protected MeshPolygonLayer(string lyrName, RASGeometry geometry)
		: base(lyrName, geometry.SourceFilename)
	{
		FeaturesRequireUniqueNames = true;
		Geometry = geometry;
	}

	public MeshPolygonLayer(string name)
		: base(name)
	{
		FeaturesRequireUniqueNames = true;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddManagedAttributeColumn("Name", "Name", typeof(string));
		DataColumnGroup dataColumnGroup = new DataColumnGroup("Cell Spacing", isVisible: true);
		AddManagedAttributeColumn("Cell Size X", "Spacing dx", typeof(float), dataColumnGroup);
		AddManagedAttributeColumn("Cell Size Y", "Spacing dy", typeof(float), dataColumnGroup);
		AddManagedAttributeColumn("Cell Shift DX", "Shift dx", typeof(float), dataColumnGroup);
		AddManagedAttributeColumn("Cell Shift DY", "Shift dy", typeof(float), dataColumnGroup);
		AddAttributeColumn("Generate as Hexagon", typeof(bool), dataColumnGroup);
		AddAttributeColumn("IsDefaultName", typeof(bool), null, DataColumnViewState.Invisible);
	}

	public virtual void HDFLoadFeatureTable(H5Reader hr)
	{
		checked
		{
			if (hr.ExistsAsGroup(HDFGroupName))
			{
				HDF5Storage.ReadPolyFeatures(this, hr, HDFGroupName + "/Polygon");
				EnsurePositivePolygonOrientations();
				string hdfGroupNameTerm = HDFGroupName + "/Attributes";
				if (hr.ExistsAsDataset(hdfGroupNameTerm))
				{
					ReadManagedColumns(hr, hdfGroupNameTerm);
				}
				int count = FeatureCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					SetIsDefaultName(i, isDefaultName: false);
					SetGenerateAsHexagon(i, generateAsHexagon: false);
				}
			}
		}
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	public virtual void HDFSaveFeatureTable(H5Writer hw)
	{
		if (FeatureCount() == 0)
		{
			hw.Delete(HDFGroupName);
			return;
		}
		hw.CreateGroup(HDFGroupName);
		HDF5Storage.WritePolygonFeatures(this, hw, HDFGroupName + "/Polygon");
		WriteManagedColumns(hw, HDFGroupName + "/Attributes");
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	public void HDFSaveFeatureTable_503(H5Writer hw)
	{
		if (FeatureCount() == 0)
		{
			hw.Delete(HDFGroupName);
			return;
		}
		hw.CreateGroup(HDFGroupName);
		HDF5Storage.WritePolygonFeatures(this, hw, HDFGroupName + "/Polygon");
	}

	public float GetCellSize(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Cell Size X");
	}

	public void SetCellSize(int fid, float cellSize)
	{
		FeatureRow(fid)["Cell Size X"] = cellSize;
		FeatureRow(fid)["Cell Size Y"] = cellSize;
	}

	public Point2D GetCellSize2D(int fid)
	{
		return new Point2D(DataTableExtensions.GetSingle(FeatureTable(), fid, "Cell Size X"), DataTableExtensions.GetSingle(FeatureTable(), fid, "Cell Size Y"));
	}

	public void SetCellSize2D(int fid, float cellSizeX, float cellSizeY)
	{
		FeatureRow(fid)["Cell Size X"] = cellSizeX;
		FeatureRow(fid)["Cell Size Y"] = cellSizeY;
	}

	public bool GetGenerateAsHexagon(int fid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), fid, "Generate as Hexagon");
	}

	public void SetGenerateAsHexagon(int fid, bool generateAsHexagon)
	{
		FeatureRow(fid)["Generate as Hexagon"] = generateAsHexagon;
	}

	public bool GetIsDefaultName(int fid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), fid, "IsDefaultName");
	}

	public void SetIsDefaultName(int fid, bool isDefaultName)
	{
		FeatureRow(fid)["IsDefaultName"] = isDefaultName;
	}
}
