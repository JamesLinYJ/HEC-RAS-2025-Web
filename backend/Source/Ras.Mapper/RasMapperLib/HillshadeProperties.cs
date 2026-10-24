using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public struct HillshadeProperties
{
	private const string HillshadeTag = "HillShade";

	private const string OnTag = "On";

	private const string AzimuthTag = "Azimuth";

	private const string ZenithTag = "Zenith";

	private const string ZFactorTag = "ZFactor";

	public const float AzimuthDefault = 315f;

	public const float ZenithDefault = 45f;

	public const float ZfactorDefault = 3f;

	public static readonly HillshadeProperties DefaultHillshadeOn = new HillshadeProperties(plotHillshade: true, 315f, 45f, 3f);

	public static readonly HillshadeProperties DefaultHillshadeOff = new HillshadeProperties(plotHillshade: false, 315f, 45f, 3f);

	public static readonly HillshadeProperties DefaultHillshade = DefaultHillshadeOn;

	public readonly bool PlotHillshade;

	public readonly float Azimuth;

	public readonly float Zenith;

	public readonly float ZFactor;

	public HillshadeProperties(bool plotHillshade, float azimuth, float zenith, float zFactor)
	{
		this = default;
		PlotHillshade = plotHillshade;
		Azimuth = azimuth;
		Zenith = zenith;
		ZFactor = zFactor;
	}

	public bool Matches(HillshadeProperties hsOther)
	{
		if (PlotHillshade != hsOther.PlotHillshade)
		{
			return false;
		}
		if (Azimuth != hsOther.Azimuth)
		{
			return false;
		}
		if (Zenith != hsOther.Zenith)
		{
			return false;
		}
		if (ZFactor != hsOther.ZFactor)
		{
			return false;
		}
		return true;
	}

	public bool MatchesDefault()
	{
		return Matches(DefaultHillshade);
	}

	public XmlElement XMLSave(XmlDocument doc)
	{
		XmlElement xmlElement = doc.CreateElement("HillShade");
		bool plotHillshade = PlotHillshade;
		xmlElement.SetAttribute("On", plotHillshade.ToString());
		float azimuth = Azimuth;
		xmlElement.SetAttribute("Azimuth", azimuth.ToString());
		azimuth = Zenith;
		xmlElement.SetAttribute("Zenith", azimuth.ToString());
		azimuth = ZFactor;
		xmlElement.SetAttribute("ZFactor", azimuth.ToString());
		return xmlElement;
	}

	public XmlElement XMLSaveIfDifferent(XmlDocument doc, HillshadeProperties @base)
	{
		if (Matches(@base))
		{
			return null;
		}
		return XMLSave(doc);
	}

	public XElement XMLSave()
	{
		XElement xElement = new XElement("HillShade");
		xElement.SetAttributeValue("On", PlotHillshade);
		xElement.SetAttributeValue("Azimuth", Azimuth);
		xElement.SetAttributeValue("Zenith", Zenith);
		xElement.SetAttributeValue("ZFactor", ZFactor);
		return xElement;
	}

	public static HillshadeProperties XMLLoad(XElement parentElement)
	{
		return XMLLoadInternal(parentElement.Element("HillShade"));
	}

	public static List<HillshadeProperties> XMLLoadMultiple(XElement parentElement)
	{
		return (from node in parentElement.Elements("HillShade")
			select XMLLoadInternal(node)).ToList();
	}

	private static HillshadeProperties XMLLoadInternal(XElement node)
	{
		HillshadeProperties result;
		if (node == null)
		{
			result = DefaultHillshade;
		}
		else
		{
			try
			{
				bool attribute = XElementExtensions.GetAttribute(node, "On", fallbackValue: false);
				float attribute2 = XElementExtensions.GetAttribute(node, "Azimuth", 315f);
				float attribute3 = XElementExtensions.GetAttribute(node, "Zenith", 45f);
				float attribute4 = XElementExtensions.GetAttribute(node, "ZFactor", 3f);
				result = new HillshadeProperties(attribute, attribute2, attribute3, attribute4);
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				result = DefaultHillshade;
				ProjectData.ClearProjectError();
			}
		}
		return result;
	}

	public static HillshadeProperties XMLLoad(XmlNode parentNode)
	{
		XmlNode xmlNode = parentNode.SelectSingleNode("HillShade");
		if (xmlNode == null)
		{
			return DefaultHillshade;
		}
		bool plotHillshade = XML.BooleanAttribute(xmlNode, "On");
		float azimuth = XML.SingleAttribute(xmlNode, "Azimuth", 315f);
		float zenith = XML.SingleAttribute(xmlNode, "Zenith", 45f);
		float zFactor = XML.SingleAttribute(xmlNode, "ZFactor", 3f);
		return new HillshadeProperties(plotHillshade, azimuth, zenith, zFactor);
	}

	public static HillshadeProperties XMLLoad(XmlNode parentNode, HillshadeProperties @base)
	{
		if (parentNode.SelectSingleNode("HillShade") == null)
		{
			return @base;
		}
		return XMLLoad(parentNode);
	}
}
