using System;
using System.Xml.Serialization;

namespace Utility.DGML;

[Serializable]
[XmlInclude(typeof(AssemblyNodeWrapper))]
[XmlInclude(typeof(VCRuntimeNodeWrapper))]
[XmlInclude(typeof(ModuleNodeWrapper))]
[XmlInclude(typeof(ContainerNode))]
[XmlInclude(typeof(ContainerLink))]
public class Graph
{
	public Node[] Nodes;

	public Link[] Links;
}
