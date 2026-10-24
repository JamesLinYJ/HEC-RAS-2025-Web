using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security;
using System.Xml;
using System.Xml.XPath;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib.Utilities;

public abstract class XML
{
	private class XmlFragmentWriter : XmlTextWriter
	{
		public XmlFragmentWriter(StringWriter sw)
			: base(sw)
		{
		}

		public override void WriteStartDocument()
		{
		}
	}

	public static string BaseURItoFilename(string uri)
	{
		if (Operators.CompareString(uri.Substring(0, 8), "file:///", TextCompare: false) == 0)
		{
			return uri.Substring(8).Replace("/", "\\");
		}
		return "";
	}

	public static double DoubleVal(string stringValue)
	{
		double result;
		if (Operators.CompareString(stringValue, "", TextCompare: false) == 0)
		{
			result = double.NaN;
		}
		else
		{
			try
			{
				result = double.Parse(stringValue);
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				result = double.NaN;
				ProjectData.ClearProjectError();
			}
		}
		return result;
	}

	public static float SingleVal(string stringValue, float defaultValue = float.NaN)
	{
		float result;
		if (Operators.CompareString(stringValue, "", TextCompare: false) == 0)
		{
			result = defaultValue;
		}
		else
		{
			try
			{
				result = float.Parse(stringValue);
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				result = defaultValue;
				ProjectData.ClearProjectError();
			}
		}
		return result;
	}

	public static int IntegerVal(string stringValue)
	{
		int result;
		if (Operators.CompareString(stringValue, "", TextCompare: false) == 0)
		{
			result = int.MinValue;
		}
		else
		{
			try
			{
				result = int.Parse(stringValue);
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				result = int.MinValue;
				ProjectData.ClearProjectError();
			}
		}
		return result;
	}

	public static bool BooleanVal(string stringValue)
	{
		if (Operators.CompareString(stringValue, "", TextCompare: false) == 0)
		{
			return false;
		}
		if (bool.TryParse(stringValue, out var result))
		{
			return result;
		}
		return false;
	}

	public static byte ByteVal(string stringValue)
	{
		byte result;
		if (Operators.CompareString(stringValue, "", TextCompare: false) == 0)
		{
			result = 0;
		}
		else
		{
			try
			{
				result = byte.Parse(stringValue);
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				result = 0;
				ProjectData.ClearProjectError();
			}
		}
		return result;
	}

	public static double DoubleAttribute(IXPathNavigable xmlNode, string attributeName)
	{
		if (xmlNode == null)
		{
			return double.NaN;
		}
		return DoubleVal(((XmlElement)xmlNode).GetAttribute(attributeName));
	}

	public static float SingleAttribute(IXPathNavigable xmlNode, string attributeName, float defaultValue = float.NaN)
	{
		if (xmlNode == null)
		{
			return defaultValue;
		}
		return SingleVal(((XmlElement)xmlNode).GetAttribute(attributeName), defaultValue);
	}

	public static int IntegerAttribute(IXPathNavigable xmlNode, string attributeName, int defaultValue = int.MinValue)
	{
		if (xmlNode == null)
		{
			return defaultValue;
		}
		string attribute = ((XmlElement)xmlNode).GetAttribute(attributeName);
		if (Operators.CompareString(attribute, "", TextCompare: false) == 0)
		{
			return defaultValue;
		}
		return IntegerVal(attribute);
	}

	public static long LongAttribute(IXPathNavigable xmlNode, string attributeName, long defaultValue = long.MinValue)
	{
		if (xmlNode == null)
		{
			return defaultValue;
		}
		string attribute = ((XmlElement)xmlNode).GetAttribute(attributeName);
		if (Operators.CompareString(attribute, "", TextCompare: false) == 0)
		{
			return defaultValue;
		}
		if (long.TryParse(attribute, out var result))
		{
			return result;
		}
		return defaultValue;
	}

	public static string StringAttribute(IXPathNavigable xmlNode, string attributeName, string defaultValue = "")
	{
		if (xmlNode == null)
		{
			return defaultValue;
		}
		string attribute = ((XmlElement)xmlNode).GetAttribute(attributeName);
		if (Operators.CompareString(attribute, "", TextCompare: false) == 0)
		{
			attribute = defaultValue;
		}
		return attribute;
	}

	public static bool BooleanAttribute(IXPathNavigable xmlNode, string attributeName, bool defaultValue = false)
	{
		if (xmlNode == null)
		{
			return defaultValue;
		}
		string attribute = ((XmlElement)xmlNode).GetAttribute(attributeName);
		if (Operators.CompareString(attribute, "", TextCompare: false) == 0)
		{
			return defaultValue;
		}
		return BooleanVal(attribute);
	}

	public static DateTime DateAttribute(IXPathNavigable xmlNode, string attributeName)
	{
		if (xmlNode == null)
		{
			return DateTime.MinValue;
		}
		return DateAndTime.DateValue(((XmlElement)xmlNode).GetAttribute(attributeName));
	}

	public static byte ByteAttribute(IXPathNavigable xmlNode, string attributeName, byte defaultValue = 0)
	{
		if (xmlNode == null)
		{
			return defaultValue;
		}
		return ByteVal(((XmlElement)xmlNode).GetAttribute(attributeName));
	}

	public static XmlElement AddTextElementSanitized(XmlDocument doc, XmlNode @base, string name, string sanitizeValue)
	{
		name = SanitizeName(name);
		sanitizeValue = SanitizeValue(sanitizeValue);
		return AddTextElement(doc, @base, name, sanitizeValue);
	}

	public static string SanitizeName(string name)
	{
		name = name.Replace(" ", "_");
		name = new string(name.Where([SpecialName] (char ch) => XmlConvert.IsXmlChar(ch)).ToArray());
		name = name.Replace("'", "");
		name = name.Replace("\"", "");
		return name;
	}

	public static string UnsanitizeName(string name)
	{
		name = name.Replace("_", " ");
		return name;
	}

	public static string SanitizeValue(string stringValue)
	{
		return SecurityElement.Escape(stringValue);
	}

	public static XmlElement AddTextElement(XmlDocument doc, XmlNode @base, string name, string innerText)
	{
		if (Operators.CompareString(innerText, "", TextCompare: false) != 0)
		{
			XmlElement xmlElement = doc.CreateElement(name);
			xmlElement.InnerText = innerText;
			@base.AppendChild(xmlElement);
			return xmlElement;
		}
		return null;
	}

	public static XmlElement AddTextElement(XmlDocument doc, XmlNode @base, string prefix, string name, string xmlns, string innerText)
	{
		if (Operators.CompareString(innerText, "", TextCompare: false) != 0)
		{
			XmlElement xmlElement = doc.CreateElement(prefix, name, xmlns);
			xmlElement.InnerText = innerText;
			@base.AppendChild(xmlElement);
			return xmlElement;
		}
		return null;
	}

	public static void SetTextElementText(XmlDocument doc, XmlNode @base, string name, string textElementText)
	{
		if (Operators.CompareString(textElementText, "", TextCompare: false) != 0)
		{
			XmlElement xmlElement = (XmlElement)@base.SelectSingleNode(name);
			if (xmlElement == null)
			{
				xmlElement = doc.CreateElement(name);
				@base.AppendChild(xmlElement);
			}
			xmlElement.InnerText = textElementText;
		}
	}

	public static string GetTextElementText(XmlNode @base, string name)
	{
		XmlElement xmlElement = (XmlElement)@base.SelectSingleNode(name);
		if (xmlElement != null)
		{
			return xmlElement.InnerText;
		}
		return "";
	}

	public static double DoubleTextElement(XmlNode @base, string name, XmlNamespaceManager nsmgr = null)
	{
		XmlNode xmlNode = @base.SelectSingleNode(name, nsmgr);
		if (xmlNode == null)
		{
			return double.NaN;
		}
		return DoubleVal(xmlNode.InnerText);
	}

	public static float SingleTextElement(XmlNode @base, string name)
	{
		XmlNode xmlNode = @base.SelectSingleNode(name);
		if (xmlNode == null)
		{
			return float.NaN;
		}
		return SingleVal(xmlNode.InnerText);
	}

	public static bool BooleanTextElement(XmlNode @base, string name)
	{
		XmlNode xmlNode = @base.SelectSingleNode(name);
		if (xmlNode == null)
		{
			return false;
		}
		return BooleanVal(xmlNode.InnerText);
	}

	public static int IntegerTextElement(XmlNode @base, string name, int defaultVal = int.MinValue)
	{
		XmlNode xmlNode = @base.SelectSingleNode(name);
		if (xmlNode == null)
		{
			return defaultVal;
		}
		return IntegerVal(xmlNode.InnerText);
	}

	public static bool ElementExists(XmlNode @base, string name)
	{
		if (@base.SelectSingleNode(name) == null)
		{
			return false;
		}
		return true;
	}

	public static byte ByteTextElement(XmlNode @base, string name, byte defaultVal = 0)
	{
		XmlNode xmlNode = @base.SelectSingleNode(name);
		if (xmlNode == null)
		{
			return defaultVal;
		}
		return ByteVal(xmlNode.InnerText);
	}

	public static int EnumTextElement(XmlNode @base, string name, Type eType, int defaultVal = int.MinValue)
	{
		XmlNode xmlNode = @base.SelectSingleNode(name);
		if (xmlNode == null || !Enum.IsDefined(eType, xmlNode.InnerText))
		{
			return defaultVal;
		}
		return Conversions.ToInteger(Enum.Parse(eType, xmlNode.InnerText));
	}

	public static string StringTextElement(XmlNode @base, string name)
	{
		if (@base == null)
		{
			return "";
		}
		XmlNode xmlNode = @base.SelectSingleNode(name);
		if (xmlNode == null)
		{
			return "";
		}
		return xmlNode.InnerText;
	}

	public static XmlNamespaceManager MakeNameSpaceManager(XmlDocument doc, ref string dns)
	{
		XmlNamespaceManager xmlNamespaceManager = new XmlNamespaceManager(doc.NameTable);
		foreach (XmlAttribute attribute in doc.DocumentElement.Attributes)
		{
			if (Operators.CompareString(attribute.Name, "xmlns", TextCompare: false) == 0)
			{
				xmlNamespaceManager.AddNamespace("", attribute.Value);
			}
			if (attribute.Name.Length > 6 && Operators.CompareString(attribute.Name.Substring(0, 6), "xmlns:", TextCompare: false) == 0)
			{
				xmlNamespaceManager.AddNamespace(attribute.Name.Substring(6), attribute.Value);
			}
		}
		if (Operators.CompareString(xmlNamespaceManager.DefaultNamespace, "", TextCompare: false) != 0)
		{
			dns = "dns";
			xmlNamespaceManager.AddNamespace(dns, xmlNamespaceManager.DefaultNamespace);
		}
		else
		{
			XmlElement documentElement = doc.DocumentElement;
			dns = documentElement.GetPrefixOfNamespace(documentElement.NamespaceURI);
		}
		if (dns.Length > 0)
		{
			dns += ":";
		}
		return xmlNamespaceManager;
	}

	public static string StripPrefix(string name)
	{
		int indexOf = name.IndexOf(":");
		if (indexOf > -1)
		{
			return name.Substring(checked(indexOf + 1));
		}
		return name;
	}

	public static string FormattedXML(string xmlText)
	{
		StringWriter stringWriter = new StringWriter();
		XmlFragmentWriter xmlFragmentWriter = new XmlFragmentWriter(stringWriter);
		xmlFragmentWriter.Formatting = Formatting.Indented;
		xmlFragmentWriter.Indentation = 2;
		XmlDocument xmlDocument = new XmlDocument();
		xmlDocument.LoadXml(xmlText);
		xmlDocument.Save(xmlFragmentWriter);
		return stringWriter.ToString();
	}

	public static string PrettyXML(ref string xmlText)
	{
		return FormattedXML(xmlText);
	}
}
