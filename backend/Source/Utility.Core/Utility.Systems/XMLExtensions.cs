using System.Linq;
using System.Xml.Linq;

namespace Utility.Systems;

public static class XMLExtensions
{
	public static XElement ElementCaseInsensitive(this XElement element, XName name)
	{
		XElement xElement = element.Element(name);
		if (xElement != null)
		{
			return xElement;
		}
		return (from e in element.Elements()
			where e.Name.LocalName.ToString().ToLowerInvariant() == name.ToString().ToLowerInvariant()
			select e).FirstOrDefault();
	}

	public static void CopyAttributesFrom(this XElement target, XElement source)
	{
		foreach (XAttribute attribute in source.Attributes())
		{
			target.SetAttributeValue(attribute.Name, attribute.Value);
		}
	}

	public static void CopyAttributesAndChildrenFrom(this XElement target, XElement source)
	{
		foreach (XAttribute attribute in source.Attributes())
		{
			target.SetAttributeValue(attribute.Name, attribute.Value);
		}
		target.RemoveNodes();
		foreach (XElement node in source.Nodes())
		{
			target.Add(new XElement(node));
		}
	}
}
