using System.Collections.Generic;
using System.Data;

namespace RasMapperLib;

public class GeometryErrorCollection : GeometryErrorCollectionBase
{
	public FeatureLayer Layer;

	public DataRow Row;

	public GeometryErrorCollection(FeatureLayer lyr, int fid, IList<GeometryError> errs)
		: base(errs)
	{
		Layer = lyr;
		Row = Layer.FeatureRow(fid);
	}

	public override string GetFeatureNames()
	{
		int indexOf = Layer.FeatureTable().Rows.IndexOf(Row);
		if (indexOf == -1)
		{
			return "N/A";
		}
		return Layer.GetFeatureName(indexOf);
	}

	public override Feature GetCurrentFeature()
	{
		int indexOf = Layer.FeatureTable().Rows.IndexOf(Row);
		if (indexOf == -1)
		{
			return null;
		}
		return Layer.Feature(indexOf);
	}

	public override string GetLayerNames()
	{
		return Layer.Name;
	}
}
