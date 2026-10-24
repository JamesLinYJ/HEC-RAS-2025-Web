using System;
using System.Collections.Generic;
using System.Linq;

namespace RasMapperLib;

public class MultiPointFeatureLayer : FeatureLayer
{
	private const string sCount = "Count";

	public MultiPoint this[int index] => Points(index);

	public MultiPointFeatureLayer()
		: base("MultiPoint Layer")
	{
	}

	public MultiPointFeatureLayer(string name)
		: base(name)
	{
	}

	public MultiPointFeatureLayer(string name, string filename)
		: base(name, filename)
	{
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddAttributeColumn("Count", typeof(int), _featureColumnGroup, DataColumnViewState.GrayedOut);
	}

	public override List<PlotOption> GetStaticPlotOptions()
	{
		List<PlotOption> staticPlotOptions = base.GetStaticPlotOptions();
		staticPlotOptions.AddRange(MultiPoint.GetAvailablePlotOptions(SharedData.DebugMode));
		return staticPlotOptions;
	}

	public IEnumerable<MultiPoint> Points()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				yield return Points(i);
			}
		}
	}

	public MultiPoint Points(int fid)
	{
		if (!(Feature(fid) is MultiPoint result))
		{
			return (MultiPoint)ResolveNullFeature();
		}
		return result;
	}

	public override Feature ResolveNullFeature()
	{
		return MultiPoint.NullPoints();
	}

	public override List<Type> AvailableFeatureTypes()
	{
		return new Type[1] { typeof(MultiPoint) }.ToList();
	}

	protected override void ResetFeatureAttributes(int fid)
	{
		if (Points(fid) == null)
		{
			FeatureRow(fid)["Count"] = 0;
		}
		else
		{
			FeatureRow(fid)["Count"] = Points(fid).Count;
		}
	}

	public override void CopySelectedFeaturesToSharedDataClipboard()
	{
		SharedData.CopiedFeatures.Clear();
		bool condition = false;
		foreach (SelectedFeature selectedFeature in SelectedFeatures)
		{
			if (!selectedFeature.IsOpen)
			{
				continue;
			}
			foreach (int selectedPoint in selectedFeature.SelectedPoints)
			{
				PointM pointM = ((MultiPoint)Feature(selectedFeature.FID)).PointM(selectedPoint);
				SharedData.CopiedFeatures.Add(new Point(pointM));
			}
			condition = true;
			break;
		}
		if (!condition)
		{
			base.CopySelectedFeaturesToSharedDataClipboard();
		}
	}
}
