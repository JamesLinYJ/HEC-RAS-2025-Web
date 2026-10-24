using H5Assist.VB;

namespace RasMapperLib;

public class RASErrors : FeatureLayer, IGeometryLayer
{
	public const string sDescription = "Description";

	public const string sErrorCollection = "Errors";

	public const string sLayerName = "Layer Name";

	public const string sFeatureName = "Feature Name";

	public const string HDFGroupName = "Geometry/Errors";

	public RASGeometry Geometry { get; }

	public override bool CanEdit { get; }

	public override string DefaultLayerName => "Errors";
string IGeometryLayer.HDFGroupName=> "Geometry/Errors";

	public RASErrors(RASGeometry geometry)
		: base("Errors")
	{
		CanEdit = false;
		Geometry = geometry;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddAttributeColumn("Layer Name", typeof(string), null, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Feature Name", typeof(string), null, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Description", typeof(string), null, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Errors", typeof(GeometryErrorCollectionBase), null, DataColumnViewState.Invisible);
	}

	protected void HDFLoadFeatureTable(H5Reader hr)
	{
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	protected void HDFSaveFeatureTable(H5Writer hw)
	{
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	protected override void OnLayerCheckChanged()
	{
		base.OnLayerCheckChanged();
		if (Checked)
		{
			Geometry.ValidateGeometry(forceShowErrors: false);
		}
	}

	public void AddError(GeometryErrorCollectionBase errors)
	{
		Feature currentFeature = errors.GetCurrentFeature();
		currentFeature?.EnsureImmutable();
		AddFeature(currentFeature);
		checked
		{
			int featureCountTerm = FeatureCount() - 1;
			SetDescription(featureCountTerm, errors.GetErrorDescription());
			SetLayerName(featureCountTerm, errors.GetLayerNames());
			SetFeatureName(featureCountTerm, errors.GetFeatureNames());
			SetErrors(FeatureCount() - 1, errors);
		}
	}

	public void ClearErrors()
	{
		ClearFeatureTable();
	}

	public string GetDescription(int errFid)
	{
		return DataTableExtensions.GetString(FeatureTable(), errFid, "Description");
	}

	public void SetDescription(int errFid, string desc)
	{
		FeatureRow(errFid)["Description"] = desc;
	}

	public GeometryErrorCollectionBase GetErrors(int errFid)
	{
		return FeatureRow(errFid)["Errors"] as GeometryErrorCollectionBase;
	}

	public void SetErrors(int errFid, GeometryErrorCollectionBase errors)
	{
		FeatureRow(errFid)["Errors"] = errors;
	}

	public string GetLayerName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Layer Name");
	}

	public void SetLayerName(int fid, string lyrName)
	{
		FeatureRow(fid)["Layer Name"] = lyrName;
	}

	public override string GetFeatureName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Feature Name");
	}

	public override void SetFeatureName(int fid, string newName)
	{
		FeatureRow(fid)["Feature Name"] = newName;
	}
}
