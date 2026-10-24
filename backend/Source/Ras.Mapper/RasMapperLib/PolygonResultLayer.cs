using System;
using System.Xml;

namespace RasMapperLib;

public class PolygonResultLayer : PolygonFeatureLayer, IResultProfile
{

	public float ContourPolygonValue { get; set; }

	public ResultProfile Profile { get; set; }

	public RASResults Result { get; set; }

	public string SHPFilename => StoredMapBaseFilename + ".shp";

	private string StoredMapBaseFilename
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public PolygonResultLayer(RASResults res)
		: base("Polygon Result Layer")
	{
		ContourPolygonValue = 0f;
		Result = res;
	}

	public PolygonResultLayer(RASResults res, string lyrName, string filename)
		: base(lyrName, filename)
	{
		ContourPolygonValue = 0f;
		Result = res;
	}

	protected override void XMLSaveMetadata(XmlDocument doc, XmlElement layernode)
	{
		base.XMLSaveMetadata(doc, layernode);
		XmlElement xmlElement = doc.CreateElement("Parameters");
		layernode.AppendChild(xmlElement);
		Profile.XMLSave(xmlElement);
	}

	protected override void XMLLoadMetadata(XmlElement xmlLayer)
	{
		base.XMLLoadMetadata(xmlLayer);
		XmlElement xmlElement = (XmlElement)xmlLayer.SelectSingleNode("Parameters");
		if (xmlElement != null)
		{
			Profile = new ResultProfile(Result);
			Profile.XMLLoad(xmlElement);
		}
	}
}
