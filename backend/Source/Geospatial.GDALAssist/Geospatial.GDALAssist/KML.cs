using System;
using System.IO;
using System.Xml;

namespace Geospatial.GDALAssist;

public static class KML
{
	public enum AltitudeMode
	{
		Absolute,
		Relative
	}

	public static string GetColorABGR(int R, int G, int B, int A)
	{
		return A.ToString("X").PadLeft(2, '0') + B.ToString("X").PadLeft(2, '0') + G.ToString("X").PadLeft(2, '0') + R.ToString("X").PadLeft(2, '0');
	}

	public static string GetColorABGR(float R, float G, float B, float A)
	{
		return ((int)(A * 255f)).ToString("X").PadLeft(2, '0') + ((int)(B * 255f)).ToString("X").PadLeft(2, '0') + ((int)(G * 255f)).ToString("X").PadLeft(2, '0') + ((int)(R * 255f)).ToString("X").PadLeft(2, '0');
	}

	public static string GetColorABGR(byte R, byte G, byte B, byte A)
	{
		int ordinal = A;
		string padLeft = ordinal.ToString("X").PadLeft(2, '0');
		ordinal = B;
		string padLeft2 = ordinal.ToString("X").PadLeft(2, '0');
		ordinal = G;
		string padLeft3 = ordinal.ToString("X").PadLeft(2, '0');
		ordinal = R;
		return padLeft + padLeft2 + padLeft3 + ordinal.ToString("X").PadLeft(2, '0');
	}

	public static string GetAltitudeModeStr(AltitudeMode mode)
	{
		string result = "";
		switch (mode)
		{
		case AltitudeMode.Absolute:
			result = "absolute";
			break;
		case AltitudeMode.Relative:
			result = "relativeToGround";
			break;
		}
		return result;
	}

	public static void GMLToKML(string GMLInputFile, int EPSGSourceCode, bool IsPolygon, string ABGRLine = "ffff0000", int WidthLine = 2, string ABGRHighlight = "ff0000ff", int WidthHighlight = 4, string KMLOutputFile = null)
	{
		string stdErr = "";
		GMLToKMLInternal(GMLInputFile, "EPSG:" + EPSGSourceCode, IsPolygon, ABGRLine, WidthLine, ABGRHighlight, WidthHighlight, KMLOutputFile, ref stdErr);
	}

	public static void GMLToKML(string GMLInputFile, string ESRISourceFile, bool IsPolygon, ref string stdErr, string ABGRLine = "ffff0000", int WidthLine = 2, string ABGRHighlight = "ff0000ff", int WidthHighlight = 4, string KMLOutputFile = null, bool hasZ = false, AltitudeMode mode = AltitudeMode.Absolute)
	{
		GMLToKMLInternal(GMLInputFile, "ESRI::\"" + ESRISourceFile + "\"", IsPolygon, ABGRLine, WidthLine, ABGRHighlight, WidthHighlight, KMLOutputFile, ref stdErr, hasZ, mode);
	}

	private static void GMLToKMLInternal(string GMLInputFile, string SourceString, bool IsPolygon, string ABGRLine, int WidthLine, string ABGRSecondary, int WidthSecondary, string KMLOutputFile, ref string stdErr, bool hasZ = false, AltitudeMode mode = AltitudeMode.Absolute)
	{
		if (KMLOutputFile == null)
		{
			KMLOutputFile = Path.ChangeExtension(GMLInputFile, "kml");
		}
		File.Delete(KMLOutputFile);
		string altitudeModeStr = GetAltitudeModeStr(mode);
		string StdOut;
		if (hasZ)
		{
			Executables.Ogr2ogr(" -f \"KML\" \"" + KMLOutputFile + "\" -s_srs " + SourceString + " -t_srs EPSG:4326 -dim 3 -dsco AltitudeMode=" + altitudeModeStr + " \"" + GMLInputFile + "\"", out StdOut, out stdErr);
		}
		else
		{
			Executables.Ogr2ogr(" -f \"KML\" \"" + KMLOutputFile + "\" -s_srs " + SourceString + " -t_srs EPSG:4326 \"" + GMLInputFile + "\"", out StdOut, out stdErr);
		}
		if (File.Exists(KMLOutputFile))
		{
			if (IsPolygon)
			{
				KMLAddColor(ABGRLine, WidthLine, ABGRSecondary, WidthSecondary, KMLOutputFile);
			}
			else
			{
				KMLAddStyle(ABGRLine, WidthLine, ABGRSecondary, WidthSecondary, KMLOutputFile);
			}
		}
	}

	public static void CombineKML(string[] KMLInputFiles, string KMLOutputFile, string GoogleEarthFolderName = "HEC RAS Places")
	{
		try
		{
			if (File.Exists(KMLOutputFile))
			{
				File.Delete(KMLOutputFile);
			}
			File.WriteAllLines(KMLOutputFile, new string[3] { "<?xml version=\"1.0\" encoding=\"utf-8\"?>", "<kml xmlns=\"http://www.opengis.net/kml/2.2\" xmlns:gx=\"http://www.google.com/kml/ext/2.2\" xmlns:kml=\"http://www.opengis.net/kml/2.2\" xmlns:atom=\"http://www.w3.org/2005/Atom\">", "</kml>" });
			XmlDocument xmlDocument = new XmlDocument();
			xmlDocument.Load(KMLOutputFile);
			XmlNode xmlNode = xmlDocument.CreateElement("Folder");
			XmlNode xmlNode2 = xmlDocument.CreateElement("name");
			xmlNode2.InnerText = GoogleEarthFolderName;
			XmlNode xmlNode3 = xmlDocument.CreateElement("open");
			xmlNode3.InnerText = "1";
			xmlNode.AppendChild(xmlNode2);
			xmlNode.AppendChild(xmlNode3);
			foreach (string kmlInputFile in KMLInputFiles)
			{
				if (!File.Exists(kmlInputFile))
				{
					continue;
				}
				XmlDocument xmlDocument2 = new XmlDocument();
				xmlDocument2.Load(kmlInputFile);
				XmlNodeList elementsByTagName = xmlDocument2.GetElementsByTagName("Document");
				if (elementsByTagName.Count <= 0)
				{
					continue;
				}
				XmlNode xmlNode4 = xmlDocument.ImportNode(elementsByTagName[0], deep: true);
				bool condition = false;
				foreach (XmlNode childNode in xmlNode4.ChildNodes)
				{
					if (childNode.LocalName == "name")
					{
						childNode.InnerText = Path.GetFileName(kmlInputFile);
						condition = true;
					}
				}
				if (!condition)
				{
					XmlNode xmlNode6 = xmlDocument.CreateElement("name");
					xmlNode6.InnerText = Path.GetFileName(kmlInputFile);
					xmlNode4.AppendChild(xmlNode6);
				}
				xmlNode.AppendChild(xmlNode4);
			}
			XmlNodeList elementsByTagName2 = xmlDocument.GetElementsByTagName("kml");
			if (elementsByTagName2.Count > 0)
			{
				elementsByTagName2[0].AppendChild(xmlNode);
			}
			xmlDocument.Save(KMLOutputFile);
		}
		catch
		{
			throw new Exception("Unable to create KML Output file: " + KMLOutputFile);
		}
	}

	private static void KMLAddColor(string ABGRLine, int WidthNormal, string ABGRSecondary, int WidthHighlight, string KMLOutputFile)
	{
		XmlDocument xmlDocument = new XmlDocument();
		xmlDocument.Load(KMLOutputFile);
		foreach (XmlNode elementsByTagName in xmlDocument.GetElementsByTagName("Placemark"))
		{
			foreach (XmlNode childNode in elementsByTagName.ChildNodes)
			{
				if (childNode.LocalName == "Style")
				{
					XmlNode xmlNode3 = xmlDocument.CreateNode("element", "styleUrl", "");
					xmlNode3.InnerText = "#MapStyle";
					elementsByTagName.ReplaceChild(xmlNode3, childNode);
				}
			}
		}
		XmlElement xmlElement = xmlDocument.CreateElement("Style");
		xmlElement.SetAttribute("id", "MapStyle");
		XmlElement xmlElement2 = xmlDocument.CreateElement("LineStyle");
		XmlElement xmlElement3 = xmlDocument.CreateElement("color");
		xmlElement3.InnerText = ABGRLine;
		XmlElement xmlElement4 = xmlDocument.CreateElement("width");
		xmlElement4.SetAttribute("xmlns", "");
		xmlElement4.InnerText = WidthNormal.ToString();
		xmlElement2.AppendChild(xmlElement3);
		xmlElement2.AppendChild(xmlElement4);
		XmlElement xmlElement5 = xmlDocument.CreateElement("PolyStyle");
		XmlElement xmlElement6 = xmlDocument.CreateElement("fill");
		xmlElement6.InnerText = 1.ToString();
		XmlElement xmlElement7 = xmlDocument.CreateElement("color");
		xmlElement7.SetAttribute("xmlns", "");
		xmlElement7.InnerText = ABGRSecondary;
		xmlElement5.AppendChild(xmlElement6);
		xmlElement5.AppendChild(xmlElement7);
		xmlElement.AppendChild(xmlElement2);
		xmlElement.AppendChild(xmlElement5);
		xmlDocument.GetElementsByTagName("Document")[0].AppendChild(xmlElement);
		xmlDocument.Save(KMLOutputFile);
	}

	private static void KMLAddStyle(string ABGRNormal, int WidthNormal, string ABGRHighlight, int WidthHighlight, string KMLOutputFile)
	{
		string newGuidGuidTerm = "RegularStyle" + Guid.NewGuid();
		string newGuidGuidTerm2 = "HighlightStyle" + Guid.NewGuid();
		string newGuidGuidTerm3 = "StyleMap" + Guid.NewGuid();
		XmlDocument xmlDocument = new XmlDocument();
		xmlDocument.Load(KMLOutputFile);
		XmlNode documentElement = xmlDocument.DocumentElement;
		XmlNode xmlNode = xmlDocument.CreateNode(XmlNodeType.Element, "name", null);
		xmlNode.InnerText = KMLOutputFile;
		documentElement.FirstChild.AppendChild(xmlNode);
		XmlNode xmlNode2 = xmlDocument.CreateNode(XmlNodeType.Element, "Style", null);
		XmlAttribute xmlAttribute = xmlDocument.CreateAttribute("id");
		xmlAttribute.Value = newGuidGuidTerm;
		xmlNode2.Attributes.Append(xmlAttribute);
		XmlNode xmlNode3 = xmlDocument.CreateNode(XmlNodeType.Element, "IconStyle", null);
		XmlNode xmlNode4 = xmlDocument.CreateNode(XmlNodeType.Element, "scale", null);
		xmlNode4.InnerText = "1.1";
		XmlNode xmlNode5 = xmlDocument.CreateNode(XmlNodeType.Element, "Icon", null);
		XmlNode xmlNode6 = xmlDocument.CreateNode(XmlNodeType.Element, "href", null);
		xmlNode6.InnerText = "http://maps.google.com/mapfiles/kml/pushpin/ylw-pushpin.png";
		XmlNode xmlNode7 = xmlDocument.CreateNode(XmlNodeType.Element, "hotSpot", null);
		XmlAttribute xmlAttribute2 = xmlDocument.CreateAttribute("x");
		xmlAttribute2.Value = "20";
		xmlNode7.Attributes.Append(xmlAttribute2);
		XmlAttribute xmlAttribute3 = xmlDocument.CreateAttribute("y");
		xmlAttribute3.Value = "2";
		xmlNode7.Attributes.Append(xmlAttribute3);
		XmlAttribute xmlAttribute4 = xmlDocument.CreateAttribute("xunits");
		xmlAttribute4.Value = "pixels";
		xmlNode7.Attributes.Append(xmlAttribute4);
		XmlAttribute xmlAttribute5 = xmlDocument.CreateAttribute("yunits");
		xmlAttribute5.Value = "pixels";
		xmlNode7.Attributes.Append(xmlAttribute5);
		XmlNode xmlNode8 = xmlDocument.CreateNode(XmlNodeType.Element, "LineStyle", null);
		XmlNode xmlNode9 = xmlDocument.CreateNode(XmlNodeType.Element, "color", null);
		xmlNode9.InnerText = ABGRNormal;
		xmlNode8.AppendChild(xmlNode9);
		XmlNode xmlNode10 = xmlDocument.CreateNode(XmlNodeType.Element, "width", null);
		xmlNode10.InnerText = WidthNormal.ToString();
		xmlNode8.AppendChild(xmlNode10);
		xmlNode5.AppendChild(xmlNode6);
		xmlNode3.AppendChild(xmlNode4);
		xmlNode3.AppendChild(xmlNode5);
		xmlNode3.AppendChild(xmlNode7);
		xmlNode3.AppendChild(xmlNode8);
		xmlNode2.AppendChild(xmlNode3);
		xmlNode2.AppendChild(xmlNode8);
		documentElement.FirstChild.AppendChild(xmlNode2);
		XmlNode xmlNode11 = xmlDocument.CreateNode(XmlNodeType.Element, "Style", null);
		XmlAttribute xmlAttribute6 = xmlDocument.CreateAttribute("id");
		xmlAttribute6.Value = newGuidGuidTerm2;
		xmlNode11.Attributes.Append(xmlAttribute6);
		XmlNode xmlNode12 = xmlDocument.CreateNode(XmlNodeType.Element, "IconStyle", null);
		XmlNode xmlNode13 = xmlDocument.CreateNode(XmlNodeType.Element, "scale", null);
		xmlNode13.InnerText = "1.3";
		XmlNode xmlNode14 = xmlDocument.CreateNode(XmlNodeType.Element, "Icon", null);
		XmlNode xmlNode15 = xmlDocument.CreateNode(XmlNodeType.Element, "href", null);
		xmlNode15.InnerText = "http://maps.google.com/mapfiles/kml/pushpin/ylw-pushpin.png";
		XmlNode xmlNode16 = xmlDocument.CreateNode(XmlNodeType.Element, "hotSpot", null);
		XmlAttribute xmlAttribute7 = xmlDocument.CreateAttribute("x");
		xmlAttribute7.Value = "20";
		xmlNode16.Attributes.Append(xmlAttribute7);
		XmlAttribute xmlAttribute8 = xmlDocument.CreateAttribute("y");
		xmlAttribute8.Value = "2";
		xmlNode16.Attributes.Append(xmlAttribute8);
		XmlAttribute xmlAttribute9 = xmlDocument.CreateAttribute("xunits");
		xmlAttribute9.Value = "pixels";
		xmlNode16.Attributes.Append(xmlAttribute9);
		XmlAttribute xmlAttribute10 = xmlDocument.CreateAttribute("yunits");
		xmlAttribute10.Value = "pixels";
		xmlNode16.Attributes.Append(xmlAttribute10);
		XmlNode xmlNode17 = xmlDocument.CreateNode(XmlNodeType.Element, "LineStyle", null);
		XmlNode xmlNode18 = xmlDocument.CreateNode(XmlNodeType.Element, "color", null);
		xmlNode18.InnerText = ABGRHighlight;
		xmlNode17.AppendChild(xmlNode18);
		XmlNode xmlNode19 = xmlDocument.CreateNode(XmlNodeType.Element, "width", null);
		xmlNode19.InnerText = WidthHighlight.ToString();
		xmlNode17.AppendChild(xmlNode19);
		xmlNode14.AppendChild(xmlNode15);
		xmlNode12.AppendChild(xmlNode13);
		xmlNode12.AppendChild(xmlNode14);
		xmlNode12.AppendChild(xmlNode16);
		xmlNode12.AppendChild(xmlNode17);
		xmlNode11.AppendChild(xmlNode12);
		xmlNode11.AppendChild(xmlNode17);
		documentElement.FirstChild.AppendChild(xmlNode11);
		XmlNode xmlNode20 = xmlDocument.CreateNode(XmlNodeType.Element, "StyleMap", null);
		XmlNode xmlNode21 = xmlDocument.CreateNode(XmlNodeType.Element, "Pair", null);
		XmlNode xmlNode22 = xmlDocument.CreateNode(XmlNodeType.Element, "key", null);
		xmlNode22.InnerText = "normal";
		xmlNode21.AppendChild(xmlNode22);
		XmlNode xmlNode23 = xmlDocument.CreateNode(XmlNodeType.Element, "styleUrl", null);
		xmlNode23.InnerText = "#" + newGuidGuidTerm;
		xmlNode21.AppendChild(xmlNode23);
		XmlNode xmlNode24 = xmlDocument.CreateNode(XmlNodeType.Element, "Pair", null);
		XmlNode xmlNode25 = xmlDocument.CreateNode(XmlNodeType.Element, "key", null);
		xmlNode25.InnerText = "highlight";
		xmlNode24.AppendChild(xmlNode25);
		XmlNode xmlNode26 = xmlDocument.CreateNode(XmlNodeType.Element, "styleUrl", null);
		xmlNode26.InnerText = "#" + newGuidGuidTerm2;
		xmlNode24.AppendChild(xmlNode26);
		xmlNode20.AppendChild(xmlNode21);
		xmlNode20.AppendChild(xmlNode24);
		XmlAttribute xmlAttribute11 = xmlDocument.CreateAttribute("id");
		xmlAttribute11.Value = newGuidGuidTerm3;
		xmlNode20.Attributes.Append(xmlAttribute11);
		documentElement.FirstChild.AppendChild(xmlNode20);
		XmlNodeList elementsByTagName = xmlDocument.GetElementsByTagName("Style");
		_ = elementsByTagName.Count;
		for (int i = 0; i < elementsByTagName.Count; i++)
		{
			XmlNode xmlNode27 = elementsByTagName[i];
			if (xmlNode27.ParentNode.Name == "Placemark")
			{
				i = -1;
				xmlNode27.ParentNode.RemoveChild(xmlNode27);
			}
		}
		XmlNodeList elementsByTagName2 = xmlDocument.GetElementsByTagName("styleUrl");
		_ = elementsByTagName2.Count;
		for (int j = 0; j < elementsByTagName2.Count; j++)
		{
			XmlNode xmlNode28 = elementsByTagName2[j];
			if (xmlNode28.ParentNode.Name == "Placemark")
			{
				j = -1;
				xmlNode28.ParentNode.RemoveChild(xmlNode28);
			}
		}
		foreach (XmlNode elementsByTagName3 in xmlDocument.GetElementsByTagName("Placemark"))
		{
			XmlNode xmlNode30 = xmlDocument.CreateElement("styleUrl");
			xmlNode30.InnerText = "#" + newGuidGuidTerm3;
			elementsByTagName3.AppendChild(xmlNode30);
		}
		xmlDocument.Save(KMLOutputFile);
	}
}
