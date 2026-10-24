using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Xml.Linq;

namespace Visual.Mapping.Observables;

public static class ColorXmlHelper
{
	public static XElement SerializeColor(Color color, double? nullable = null)
	{
		XElement xElement = new XElement("Color");
		xElement.SetAttributeValue("R", (int)color.R);
		xElement.SetAttributeValue("G", (int)color.G);
		xElement.SetAttributeValue("B", (int)color.B);
		if (nullable.HasValue)
		{
			xElement.SetAttributeValue("Value", nullable.Value);
		}
		return xElement;
	}

	public static bool TryParseColor(XElement node, out Color color, out double doubleValue)
	{
		color = default;
		doubleValue = 0.0;
		if (!int.TryParse(node.Attribute("R")?.Value, out var result) || !int.TryParse(node.Attribute("G")?.Value, out var result2) || !int.TryParse(node.Attribute("B")?.Value, out var result3))
		{
			return false;
		}
		color = Color.FromArgb(255, result, result2, result3);
		double.TryParse(node.Attribute("Value")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue);
		return true;
	}

	public static void SerializeColorRamp(XElement parent, List<Color> colors, double[] values)
	{
		for (int i = 0; i < colors.Count; i++)
		{
			parent.Add(SerializeColor(colors[i], values[i]));
		}
	}

	public static bool TryParseColorRamp(XElement parent, out List<Color> colors, out List<double> values)
	{
		colors = new List<Color>();
		values = new List<double>();
		foreach (XElement element in parent.Elements("Color"))
		{
			if (TryParseColor(element, out var color, out var computed))
			{
				colors.Add(color);
				values.Add(computed);
			}
		}
		return colors.Count > 0;
	}

	public static void SetIfNotDefault<T>(XElement node, string name, T ifNotDefault, T defaultValue)
	{
		if (!object.Equals(ifNotDefault, defaultValue))
		{
			node.SetAttributeValue(name, ifNotDefault);
		}
	}
}
