using System.Xml;

namespace RasMapperLib;

public interface ISurfaceFill
{
	SurfaceFillType SurfaceFillType { get; set; }

	byte Alpha { get; set; }

	double[] Values { get; }

	bool UseDatasetMinMax { get; }

	bool Matches(ISurfaceFill other);

	XmlElement XMLSave(XmlDocument doc);

	XmlElement XMLSaveIfDifferent(XmlDocument doc, ISurfaceFill @base);

	void XMLLoad(XmlElement ele);

	void ModifyGivenBackgroundRectangleVertical(ref int height);

	string GetStringFormat();

	void GetMaxMin(ref object minValue, ref object maxValue);
}
