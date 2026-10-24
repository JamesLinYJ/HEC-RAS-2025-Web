using System;
using System.Text;

namespace RasMapperLib;

public class RasmapPolygonLayer : PolygonFeatureLayer, IRasmapFeatureLayer
{
	protected string XMLGroupName { get; }

	protected string XMLElementName { get; }

	public RasmapPolygonLayer()
		: base("Polygons")
	{
		XMLGroupName = "Polygons";
		XMLElementName = "Polygon";
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddAttributeColumn("Name", typeof(string));
		AddAttributeColumn("Area", typeof(double), null, DataColumnViewState.GrayedOut);
	}

	public Polygon GetPolygon(int fid)
	{
		return Feature(fid) as Polygon;
	}

	protected override string FeatureToCSV(int fId)
	{
		Polygon polygon = GetPolygon(fId);
		if (polygon == null)
		{
			return "";
		}
		StringBuilder stringBuilder = new StringBuilder();
		checked
		{
			int count = polygon.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = polygon.PointM(i);
				stringBuilder.Append(pointM.X + "," + pointM.Y);
				if (i != polygon.Count - 1)
				{
					stringBuilder.Append(",");
				}
			}
			return stringBuilder.ToString();
		}
	}

	protected override Feature CSVToFeature(string featureCSV, string typeName)
	{
		string[] parts = featureCSV.Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries);
		PointMs pointMs = new PointMs();
		checked
		{
			int count = parts.Length - 1;
			for (int i = 0; i <= count; i += 2)
			{
				if (double.TryParse(parts[i], out var result) && double.TryParse(parts[i + 1], out var result2))
				{
					pointMs.Add(result, result2);
					continue;
				}
				return null;
			}
			return RasMapperLib.Polygon.FromBuffers(pointMs);
		}
	}

	string IRasmapFeatureLayer.XMLGroupName => XMLGroupName;

	string IRasmapFeatureLayer.XMLElementName => XMLElementName;
}
