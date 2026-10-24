using System.Xml.Serialization;

namespace Utility.DGML;

public class ContainerLink : Link
{
	[XmlAttribute]
	public string Category = "Contains";

	public ContainerLink(string source, string target, string label = "")
		: base(source, target, label)
	{
	}

	public ContainerLink(Node source, Node target, string label = "")
		: base(source.Id, target.Id, label)
	{
	}

	public ContainerLink()
	{
	}
}
