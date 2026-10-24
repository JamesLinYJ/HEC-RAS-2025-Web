using System.Xml.Linq;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

[StandardModule]
internal static class XElementExtensions
{
	public static bool GetAttribute(this XElement el, string attrName, bool fallbackValue)
	{
		XAttribute xAttribute = el.Attribute(attrName);
		if (xAttribute == null)
		{
			return fallbackValue;
		}
		bool result = fallbackValue;
		bool.TryParse(xAttribute.Value, out result);
		return result;
	}

	public static int GetAttribute(this XElement el, string attrName, int fallbackValue)
	{
		XAttribute xAttribute = el.Attribute(attrName);
		if (xAttribute == null)
		{
			return fallbackValue;
		}
		int result = fallbackValue;
		int.TryParse(xAttribute.Value, out result);
		return result;
	}

	public static float GetAttribute(this XElement el, string attrName, float fallbackValue)
	{
		XAttribute xAttribute = el.Attribute(attrName);
		if (xAttribute == null)
		{
			return fallbackValue;
		}
		float result = fallbackValue;
		float.TryParse(xAttribute.Value, out result);
		return result;
	}
}
