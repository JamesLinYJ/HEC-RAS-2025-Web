using System;
using System.Reflection;
using System.Xml.Linq;
using Utility.Reflection;

namespace Utility.Serialization.XML;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class XmlNodeExpectedAncestorAttribute : Attribute
{
	public string NodeName { get; }

	public int AncestorIndex { get; }

	public string ExpectedTag { get; set; }

	public string ExpectedTagValuePath { get; set; }

	public string ConditionPath { get; set; }

	public XmlNodeExpectedAncestorAttribute(string ancestorNodeName, int ancestorIndex)
	{
		NodeName = ancestorNodeName;
		AncestorIndex = ancestorIndex;
	}

	public bool MeetsCondition(object context)
	{
		if (ConditionPath != null && !ConditionPath.Equals(""))
		{
			object path = context.EvaluatePath(ConditionPath.Split(""));
			if (path is bool && !(bool)path)
			{
				return true;
			}
		}
		return false;
	}

	public static bool Validate(object context, XElement[] parents)
	{
		foreach (XmlNodeExpectedAncestorAttribute customAttribute in context.GetType().GetCustomAttributes<XmlNodeExpectedAncestorAttribute>(inherit: true))
		{
			if (customAttribute.AncestorIndex >= parents.Length)
			{
				return false;
			}
			if (customAttribute.MeetsCondition(context))
			{
				return true;
			}
			XElement xElement = parents[customAttribute.AncestorIndex];
			if (!xElement.Name.LocalName.Equals(customAttribute.NodeName))
			{
				return false;
			}
			if (customAttribute.ExpectedTag == null || customAttribute.ExpectedTag.Equals("") || customAttribute.ExpectedTagValuePath == null || customAttribute.ExpectedTagValuePath.Equals(""))
			{
				return true;
			}
			XAttribute xAttribute = xElement.Attribute(customAttribute.ExpectedTag);
			if (xAttribute == null)
			{
				return false;
			}
			object path = context.EvaluatePath(customAttribute.ExpectedTagValuePath.Split(""));
			if (path == null || !path.ToString().Equals(xAttribute.Value, StringComparison.CurrentCultureIgnoreCase))
			{
				return false;
			}
		}
		return true;
	}
}
