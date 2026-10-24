using System;

namespace Utility.Serialization.XML;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field)]
public class XmlTagAttribute : Attribute
{
	public string Tag { get; set; }

	public object DefaultValue { get; set; }

	public XmlTagAttribute(string tag)
	{
		Tag = tag;
	}
}
