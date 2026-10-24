using System.Xml.Serialization;

namespace Utility.DGML;

public class ContainerNode : Node
{
	[XmlAttribute]
	public string Group = "Expanded";

	public ContainerNode(string id, string label, bool expanded = true)
		: base(id, label)
	{
		if (expanded)
		{
			Group = "Expanded";
		}
		else
		{
			Group = "Collapsed";
		}
	}

	public ContainerNode()
	{
	}
}
