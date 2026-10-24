using System;

namespace RasMapperLib;

public class RasmapPointLayer : PointFeatureLayer, IRasmapFeatureLayer
{
	protected string XMLGroupName { get; }

	protected string XMLElementName { get; }

	public override string DefaultLayerName { get; }

	public RasmapPointLayer()
		: base("Points")
	{
		XMLGroupName = "Points";
		XMLElementName = "Point";
		DefaultLayerName = "Points";
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddAttributeColumn("Name", typeof(string));
	}

	protected override string FeatureToCSV(int fId)
	{
		Point point = Point(fId);
		if (point == null)
		{
			return "";
		}
		return point.X + "," + point.Y + "," + point.Z + "," + point.M;
	}

	protected override Feature CSVToFeature(string featureCSV, string typeName)
	{
		string[] parts = featureCSV.Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length < 2)
		{
			return null;
		}
		double result = double.NaN;
		double result2 = double.NaN;
		if (double.TryParse(parts[0], out var result3) && double.TryParse(parts[1], out var result4))
		{
			if (parts.Length >= 3)
			{
				double.TryParse(parts[2], out result);
			}
			if (parts.Length == 4)
			{
				double.TryParse(parts[3], out result2);
			}
			return new Point(result3, result4, result, result2);
		}
		return null;
	}

	string IRasmapFeatureLayer.XMLGroupName => XMLGroupName;

	string IRasmapFeatureLayer.XMLElementName => XMLElementName;
}
