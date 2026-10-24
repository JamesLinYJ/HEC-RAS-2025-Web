using System;
using System.Reflection;
using System.Xml.Linq;
using Utility.Reflection;

namespace Utility.Serialization.XML;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class XmlNodeExpectedTagAttribute : Attribute
{
	public string Tag { get; }

	public string[] ValuePath { get; }

	public XmlNodeExpectedTagAttribute(string tag, string valuePath)
	{
		Tag = tag;
		ValuePath = valuePath.Split(".");
	}

	public object GetValue(object context)
	{
		return context.EvaluatePath(ValuePath);
	}

	public static bool Validate(object context, XElement element)
	{
		foreach (XmlNodeExpectedTagAttribute customAttribute in context.GetType().GetCustomAttributes<XmlNodeExpectedTagAttribute>(inherit: true))
		{
			XAttribute xAttribute = element.Attribute(customAttribute.Tag);
			if (xAttribute == null)
			{
				return false;
			}
			object getValue = customAttribute.GetValue(context);
			if (getValue == null)
			{
				return false;
			}
			if (!getValue.ToString().Equals(xAttribute.Value, StringComparison.CurrentCultureIgnoreCase))
			{
				return false;
			}
		}
		return true;
	}

	public static int Serialize(object context, XElement serializeTo)
	{
		int ordinal = 0;
		foreach (XmlNodeExpectedTagAttribute customAttribute in context.GetType().GetCustomAttributes<XmlNodeExpectedTagAttribute>())
		{
			object getValue = customAttribute.GetValue(context);
			if (getValue != null)
			{
				ordinal++;
			}
			serializeTo.SetAttributeValue(customAttribute.Tag, getValue);
		}
		return ordinal;
	}
}
