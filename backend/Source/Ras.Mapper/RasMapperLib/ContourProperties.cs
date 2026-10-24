using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public struct ContourProperties
{
	private const string ContourTag = "Contour";

	private const string OnTag = "On";

	private const string IntervalTag = "Interval";

	private const string ColorARGBTag = "Color";

	public const float IntervalDefault = 5f;

	public const int ColorARGBDefault = -16777216;

	public static readonly ContourProperties DefaultContourOn = new ContourProperties(plotContours: true, 5f, -16777216);

	public static readonly ContourProperties DefaultContourOff = new ContourProperties(plotContours: false, 5f, -16777216);

	public static readonly ContourProperties DefaultContour = DefaultContourOff;

	public readonly bool PlotContours;

	public readonly float Interval;

	public readonly int ColorARGB;

	public Color Color => Color.FromArgb(ColorARGB);

	public ContourProperties(bool plotContours, float interval, int colorARGB)
	{
		this = default;
		PlotContours = plotContours;
		Interval = interval;
		ColorARGB = colorARGB;
	}

	public bool Matches(ContourProperties contourOther)
	{
		if (PlotContours != contourOther.PlotContours)
		{
			return false;
		}
		if (Interval != contourOther.Interval)
		{
			return false;
		}
		if (ColorARGB != contourOther.ColorARGB)
		{
			return false;
		}
		return true;
	}

	public bool MatchesDefault()
	{
		return Matches(DefaultContour);
	}

	public XmlElement XMLSave(XmlDocument doc)
	{
		XmlElement xmlElement = doc.CreateElement("Contour");
		bool plotContours = PlotContours;
		xmlElement.SetAttribute("On", plotContours.ToString());
		float interval = Interval;
		xmlElement.SetAttribute("Interval", interval.ToString());
		int colorARGB = ColorARGB;
		xmlElement.SetAttribute("Color", colorARGB.ToString());
		return xmlElement;
	}

	public XElement XMLSave()
	{
		XElement xElement = new XElement("Contour");
		xElement.SetAttributeValue("On", PlotContours);
		xElement.SetAttributeValue("Interval", PlotContours);
		xElement.SetAttributeValue("Color", ColorARGB);
		return xElement;
	}

	public XmlElement XMLSaveIfDifferent(XmlDocument doc, ContourProperties @base)
	{
		if (Matches(@base))
		{
			return null;
		}
		return XMLSave(doc);
	}

	public static ContourProperties XMLLoad(XElement parentElement)
	{
		return XMLLoadInternal(parentElement.Element("Contour"));
	}

	public static List<ContourProperties> XMLLoadMultiple(XElement parentElement)
	{
		return (from node in parentElement.Elements("Contour")
			select XMLLoadInternal(node)).ToList();
	}

	private static ContourProperties XMLLoadInternal(XElement node)
	{
		ContourProperties result;
		if (node == null)
		{
			result = DefaultContour;
		}
		else
		{
			try
			{
				bool attribute = XElementExtensions.GetAttribute(node, "On", fallbackValue: false);
				float attribute2 = XElementExtensions.GetAttribute(node, "Interval", 5f);
				int attribute3 = XElementExtensions.GetAttribute(node, "Color", -16777216);
				result = new ContourProperties(attribute, attribute2, attribute3);
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				result = DefaultContour;
				ProjectData.ClearProjectError();
			}
		}
		return result;
	}

	public static ContourProperties XMLLoad(XmlNode xmlLayer)
	{
		XmlNode xmlNode = xmlLayer.SelectSingleNode("Contour");
		if (xmlNode == null)
		{
			return DefaultContour;
		}
		bool plotContours = XML.BooleanAttribute(xmlNode, "On");
		float interval = XML.SingleAttribute(xmlNode, "Interval");
		int colorARGB = XML.IntegerAttribute(xmlNode, "Color");
		return new ContourProperties(plotContours, interval, colorARGB);
	}

	public static ContourProperties XMLLoad(XmlNode parentNode, ContourProperties @base)
	{
		if (parentNode.SelectSingleNode("Contour") == null)
		{
			return @base;
		}
		return XMLLoad(parentNode);
	}
}
