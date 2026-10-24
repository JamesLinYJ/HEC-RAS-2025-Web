using System.Collections.Generic;
using System.Data;

namespace RasMapperLib;

public class GeometryIntersectionErrorCollection : GeometryErrorCollectionBase
{
	public FeatureLayer Layer1;

	public FeatureLayer Layer2;

	public DataRow Row1;

	public DataRow Row2;

	public MultiPoint IntersectionPoints;

	public GeometryIntersectionErrorCollection(FeatureLayer lyr1, FeatureLayer lyr2, int fid1, int fid2, MultiPoint intersectionPoints, IList<GeometryError> errs)
		: base(errs)
	{
		Layer1 = lyr1;
		Layer2 = lyr2;
		Row1 = Layer1.FeatureRow(fid1);
		Row2 = Layer2.FeatureRow(fid2);
		IntersectionPoints = intersectionPoints;
	}

	public override string GetFeatureNames()
	{
		int indexOf = Layer1.FeatureTable().Rows.IndexOf(Row1);
		int indexOf2 = Layer2.FeatureTable().Rows.IndexOf(Row2);
		string indexOfGetFeatureNameTerm = "";
		string indexOf2GetFeatureNameTerm = "";
		indexOfGetFeatureNameTerm = ((indexOf != -1) ? Layer1.GetFeatureName(indexOf) : "N/A");
		indexOf2GetFeatureNameTerm = ((indexOf2 != -1) ? Layer2.GetFeatureName(indexOf2) : "N/A");
		return indexOfGetFeatureNameTerm + ", " + indexOf2GetFeatureNameTerm;
	}

	public override Feature GetCurrentFeature()
	{
		return IntersectionPoints;
	}

	public override string GetLayerNames()
	{
		return Layer1.Name + ", " + Layer2.Name;
	}
}
