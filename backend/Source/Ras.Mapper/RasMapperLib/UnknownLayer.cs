using System.Xml;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class UnknownLayer : Layer
{
	private string _filename;

	private XmlElement _xml;

	public override bool CanEdit { get; }

	public UnknownLayer()
		: base("Unknown Layer")
	{
		CanEdit = false;
	}

	public UnknownLayer(string name, string sourceFilename)
		: base(name)
	{
		CanEdit = false;
		_filename = sourceFilename;
	}

	protected override void XMLLoadMetadata(XmlElement xmlLayer)
	{
		string stringAttribute = XML.StringAttribute(xmlLayer, "Name", DefaultLayerName);
		if (!string.IsNullOrEmpty(stringAttribute))
		{
			SetDisplayName(stringAttribute);
		}
		_xml = (XmlElement)xmlLayer.Clone();
	}

	protected override void XMLLoadChildren(XmlElement xmlEl)
	{
	}

	protected override void XMLSaveMetadata(XmlDocument doc, XmlElement xmlEl)
	{
		foreach (XmlAttribute attribute in _xml.Attributes)
		{
			xmlEl.SetAttribute(attribute.Name, attribute.Value);
		}
	}

	protected override void XMLSaveChildren(XmlDocument doc, XmlElement xmlEl)
	{
		foreach (XmlNode childNode in _xml.ChildNodes)
		{
			XmlNode newChild = doc.ImportNode(childNode, deep: true);
			xmlEl.AppendChild(newChild);
		}
	}
}
