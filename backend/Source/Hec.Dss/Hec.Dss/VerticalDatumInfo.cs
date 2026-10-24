using System;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Hec.Dss;

public class VerticalDatumInfo
{
	public string NativeDatum = "";

	public string LocalDatumName = "";

	public string Units = "";

	public double Elevation = -3.4028234663852886E+38;

	public double NGVD29Offset = -3.4028234663852886E+38;

	public double NAVD88Offset = -3.4028234663852886E+38;

	public bool NGVD29OffsetIsEstimate = true;

	public bool NAVD88OffsetIsEstimate = true;

	public VerticalDatumInfo()
	{
	}

	public string ToXml(bool humanFriendly = false)
	{
		XmlDocument xmlDocument = new XmlDocument();
		XmlElement xmlElement = xmlDocument.CreateElement("vertical-datum-info");
		xmlDocument.AppendChild(xmlElement);
		XmlAttribute xmlAttribute = xmlDocument.CreateAttribute("unit");
		xmlAttribute.Value = Units;
		xmlElement.Attributes.Append(xmlAttribute);
		XmlElement xmlElement2 = xmlDocument.CreateElement("elevation");
		xmlElement2.InnerText = Elevation.ToString();
		xmlElement.AppendChild(xmlElement2);
		XmlElement xmlElement3 = xmlDocument.CreateElement("native-datum");
		xmlElement3.InnerText = NativeDatum;
		xmlElement.AppendChild(xmlElement3);
		AppendOffset(xmlDocument, "NGVD-29", NGVD29OffsetIsEstimate, NGVD29Offset);
		AppendOffset(xmlDocument, "NAVD-88", NAVD88OffsetIsEstimate, NAVD88Offset);
		if (humanFriendly)
		{
			return XDocument.Parse(ToXml()).ToString();
		}
		return xmlDocument.OuterXml;
	}

	private void AppendOffset(XmlDocument doc, string offsetName, bool isEstimate, double offsetValue)
	{
		XmlElement xmlElement = doc.CreateElement("offset");
		XmlAttribute xmlAttribute = doc.CreateAttribute("estimate");
		xmlAttribute.Value = isEstimate.ToString().ToLower();
		xmlElement.Attributes.Append(xmlAttribute);
		XmlElement xmlElement2 = doc.CreateElement("to-datum");
		xmlElement2.InnerText = offsetName.ToString();
		xmlElement.AppendChild(xmlElement2);
		XmlElement xmlElement3 = doc.CreateElement("value");
		xmlElement3.InnerText = offsetValue.ToString();
		xmlElement.AppendChild(xmlElement3);
		doc.FirstChild.AppendChild(xmlElement);
	}

	public VerticalDatumInfo(string xmlFragement)
	{
		try
		{
			XmlDocument xmlDocument = new XmlDocument();
			xmlDocument.LoadXml(xmlFragement);
			XmlElement documentElement = xmlDocument.DocumentElement;
			string innerText = GetInnerText(documentElement, "/vertical-datum-info/elevation");
			if (innerText.Trim().Length > 0 && double.TryParse(innerText, out var result))
			{
				Elevation = result;
			}
			XmlNode n = documentElement.SelectSingleNode("/vertical-datum-info");
			Units = GetAttribute(n, "unit", "");
			NativeDatum = GetInnerText(documentElement, "/vertical-datum-info/native-datum");
			ReadOffsets(documentElement);
		}
		catch (Exception ex)
		{
			Console.WriteLine("Error: parsing xml vertical-datum-info " + ex.Message);
		}
	}

	private void ReadOffsets(XmlElement root)
	{
		XmlNodeList xmlNodeList = root.SelectNodes("/vertical-datum-info/offset");
		for (int i = 0; i < xmlNodeList.Count; i++)
		{
			XmlNode xmlNode = xmlNodeList[i];
			if (xmlNode["to-datum"] == null || xmlNode["value"] == null)
			{
				continue;
			}
			string innerText = xmlNode["to-datum"].InnerText;
			string innerText2 = xmlNode["value"].InnerText;
			if (bool.TryParse(GetAttribute(xmlNode, "estimate", "true"), out var result) && double.TryParse(innerText2, out var result2))
			{
				if (Regex.IsMatch(innerText, "NGVD\\s*(-?)\\s*29"))
				{
					NGVD29OffsetIsEstimate = result;
					NGVD29Offset = result2;
				}
				else if (Regex.IsMatch(innerText, "NAVD\\s*(-?)\\s*88"))
				{
					NAVD88OffsetIsEstimate = result;
					NAVD88Offset = result2;
				}
			}
		}
	}

	private static string GetAttribute(XmlNode n, string name, string defaultValue)
	{
		if (n.Attributes.Count == 0)
		{
			return defaultValue;
		}
		XmlAttribute xmlAttribute = n.Attributes[name];
		if (xmlAttribute != null)
		{
			return xmlAttribute.Value;
		}
		return defaultValue;
	}

	private string GetInnerText(XmlElement root, string path)
	{
		return GetInnerText(root.SelectSingleNode(path));
	}

	private static string GetInnerText(XmlNode node)
	{
		if (node == null)
		{
			return "";
		}
		if (node.InnerText != "")
		{
			return node.InnerText;
		}
		return "";
	}
}
