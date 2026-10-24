using System;
using System.Collections.Generic;
using H5Assist.VB;

namespace RasMapperLib;

public class RatingCurveOutlet : StructureOutletOverrideLayer
{
	public const string sIsFlow = "Is Flow";

	public const string sLayerName = "Rating Curve Outlets";

	private Dictionary<int, List<Point2D>> _rcValues;

	public override string SimpleFeatureTypeName { get; set; }

	public override string HDFGroupName { get; }

	public override bool CanEdit { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public RatingCurveOutlet(RASGeometry geom)
		: base(geom, "Rating Curve Outlets")
	{
		SimpleFeatureTypeName = "Rating Curve Outlet";
		HDFGroupName = "Geometry/Structures/Outlet Rating Curves";
		CanEdit = true;
		ForwardEditRequestsToParent = true;
		_rcValues = new Dictionary<int, List<Point2D>>();
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddManagedAttributeColumn("Is Flow", "Is Flow", typeof(bool), null, DataColumnViewState.GrayedOut);
	}

	protected override void HDFLoadFeatureTable(H5Reader hr)
	{
		base.HDFLoadFeatureTable(hr);
		string hdfGroupNameTerm = HDFGroupName + "/Rating Curve Info";
		if (!hr.ExistsAsDataset(hdfGroupNameTerm))
		{
			return;
		}
		int[,] values = null;
		hr.ReadRows(hdfGroupNameTerm, 0, FeatureCount(), ref values);
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				int startRow = values[i, 0];
				int ordinal = values[i, 1];
				float[,] values2 = null;
				hr.ReadRows(HDFGroupName + "/Rating Curve Values", startRow, ordinal, ref values2);
				_rcValues[i] = new List<Point2D>();
				int count2 = ordinal - 1;
				for (int j = 0; j <= count2; j++)
				{
					_rcValues[i].Add(new Point2D(values2[j, 0], values2[j, 1]));
				}
			}
		}
	}

	public override int AddNewElement(int parentIndex, string elementName = "")
	{
		throw new NotImplementedException();
	}

	public List<Point2D> GetRCValues(int fid)
	{
		if (_rcValues.ContainsKey(fid))
		{
			return _rcValues[fid];
		}
		return new List<Point2D>();
	}
}
