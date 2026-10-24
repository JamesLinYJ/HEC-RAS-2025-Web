using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Xml;
using System.Xml.Serialization;
using Utility.Processes;

namespace Utility.DGML;

public class DGMLWriter
{
	public Dictionary<string, Node> Nodes { get; protected set; }

	public List<Link> Links { get; protected set; }

	public DGMLWriter()
	{
		Nodes = new Dictionary<string, Node>();
		Links = new List<Link>();
	}

	public void AddNode(Node n)
	{
		Nodes[n.Id] = n;
	}

	public void AddLink(Link l)
	{
		Links.Add(l);
	}

	public void AddLink(Node fromNode, Node toNode, string label = "")
	{
		Links.Add(new Link(fromNode.Id, toNode.Id, label));
	}

	public Node AddUniqueNode(string nodeLabel)
	{
		Node node = new Node(GetUniqueNodeID(nodeLabel), nodeLabel);
		AddNode(node);
		return node;
	}

	public ContainerNode AddUniqueContainerNode(string nodeLabel, bool isExpanded = true)
	{
		ContainerNode containerNode = new ContainerNode(GetUniqueNodeID(nodeLabel), nodeLabel, isExpanded);
		AddNode(containerNode);
		return containerNode;
	}

	public ContainerNode AddGroupedNodes<TNode>(IEnumerable<TNode> runs, string groupName) where TNode : Node
	{
		if (runs.Count() == 0)
		{
			return null;
		}
		ContainerNode containerNode = AddUniqueContainerNode(groupName);
		foreach (TNode run in runs)
		{
			AddNode(run);
			AddLink(new ContainerLink(containerNode, run));
		}
		return containerNode;
	}

	public void AddToGroup(ContainerNode owner, IEnumerable<Node> subNodes)
	{
		foreach (Node subNode in subNodes)
		{
			AddNode(subNode);
			AddLink(new ContainerLink(owner, subNode));
		}
	}

	public void AddToGroup(ContainerNode owner, Node subNode)
	{
		AddNode(subNode);
		AddLink(new ContainerLink(owner, subNode));
	}

	[SupportedOSPlatform("windows")]
	private void ExportLinkedDGML(IList<VCRuntime> runs)
	{
		for (int i = 0; i < runs.Count; i++)
		{
		}
		throw new NotImplementedException();
	}

	public string GetUniqueNodeID(string targetNodeName)
	{
		if (Nodes.Values.FirstOrDefault((Node n) => n.Id == targetNodeName) == null)
		{
			return targetNodeName;
		}
		int unity = 1;
		string nextNodeName;
		while (true)
		{
			nextNodeName = targetNodeName + "(" + unity + ")";
			if (Nodes.Values.FirstOrDefault((Node n) => n.Id == nextNodeName) == null)
			{
				break;
			}
			unity++;
		}
		return nextNodeName;
	}

	public void Serialize(string xmlFilename)
	{
		Graph graph = new Graph();
		graph.Nodes = Nodes.Values.ToArray();
		graph.Links = Links.ToArray();
		XmlRootAttribute xmlRootAttribute = new XmlRootAttribute("DirectedGraph");
		xmlRootAttribute.Namespace = "http://schemas.microsoft.com/vs/2009/dgml";
		XmlSerializer xmlSerializer = new XmlSerializer(typeof(Graph), xmlRootAttribute);
		new XmlSerializer(typeof(Graph), xmlRootAttribute);
		XmlWriter xmlWriter = XmlWriter.Create(xmlFilename, new XmlWriterSettings
		{
			Indent = true
		});
		xmlSerializer.Serialize(xmlWriter, graph);
	}
}
