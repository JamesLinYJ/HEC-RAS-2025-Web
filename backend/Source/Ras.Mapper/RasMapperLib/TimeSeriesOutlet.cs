using System;

namespace RasMapperLib;

public class TimeSeriesOutlet : StructureOutletOverrideLayer
{
	public const string sLayerName = "Outlet Time Series";

	public override string SimpleFeatureTypeName { get; set; }

	public override string HDFGroupName { get; }

	public override bool CanEdit { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public TimeSeriesOutlet(RASGeometry geom)
		: base(geom, "Outlet Time Series")
	{
		SimpleFeatureTypeName = "Outlet Time Series";
		HDFGroupName = "Geometry/Structures/Outlet Time Series";
		CanEdit = true;
		ForwardEditRequestsToParent = true;
	}

	public override int AddNewElement(int parentIndex, string elementName = "")
	{
		throw new NotImplementedException();
	}
}
