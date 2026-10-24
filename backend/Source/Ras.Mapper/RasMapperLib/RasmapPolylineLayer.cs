using System;
using System.Text;

namespace RasMapperLib;

public class RasmapPolylineLayer : PolylineFeatureLayer, IRasmapFeatureLayer
{
	protected string XMLGroupName { get; }

	protected string XMLElementName { get; }

	public RasmapPolylineLayer()
		: base("Lines")
	{
		XMLGroupName = "Lines";
		XMLElementName = "Line";
	}

	public RasmapPolylineLayer(string lyrName)
		: base(lyrName)
	{
		XMLGroupName = "Lines";
		XMLElementName = "Line";
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddAutoNameColumn();
	}

	protected override string FeatureToCSV(int fId)
	{
		Polyline polyline = Polyline(fId);
		if (polyline == null)
		{
			return "";
		}
		StringBuilder stringBuilder = new StringBuilder();
		checked
		{
			int count = polyline.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = polyline.PointM(i);
				stringBuilder.Append(pointM.X + "," + pointM.Y);
				if (i != polyline.Count - 1)
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
			return RasMapperLib.Polyline.FromBuffers(pointMs);
		}
	}

	string IRasmapFeatureLayer.XMLGroupName => XMLGroupName;

	string IRasmapFeatureLayer.XMLElementName => XMLElementName;
}
