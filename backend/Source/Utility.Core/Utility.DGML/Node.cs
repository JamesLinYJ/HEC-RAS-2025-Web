using System.Diagnostics;
using System.Xml.Serialization;

namespace Utility.DGML;

[DebuggerDisplay("{Label}")]
[XmlRoot(Namespace = "DGML")]
public class Node
{
	[XmlAttribute]
	public string Id;

	[XmlAttribute]
	public string Label;

	public Node(string id, string label)
	{
		Id = id;
		Label = label;
	}

	public Node()
	{
	}
}
