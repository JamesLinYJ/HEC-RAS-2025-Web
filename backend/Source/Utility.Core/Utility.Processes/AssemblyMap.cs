using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Utility.DGML;

namespace Utility.Processes;

public class AssemblyMap
{
	public List<AssemblyNode> Nodes { get; }

	public AssemblyMap(IEnumerable<AssemblyNode> nodes)
	{
		Nodes = nodes.ToList();
	}

	public List<AssemblyNode> Flatten()
	{
		List<AssemblyNode> assemblyNodes = new List<AssemblyNode>();
		foreach (AssemblyNode node in Nodes)
		{
			assemblyNodes.Add(node);
			assemblyNodes.AddRange(node.FlattenDependencies());
		}
		return assemblyNodes;
	}

	public void ExportDGML(DGMLWriter writer, bool groupSystemNodes = true)
	{
		ContainerNode owner = writer.AddUniqueContainerNode(".Net Assembiles");
		ContainerNode containerNode = writer.AddUniqueContainerNode("Microsoft", isExpanded: false);
		writer.AddToGroup(owner, containerNode);
		foreach (AssemblyNode node in Nodes)
		{
			AssemblyNodeWrapper assemblyNodeWrapper = new AssemblyNodeWrapper(node);
			writer.AddNode(assemblyNodeWrapper);
			if (IsMicrosoftLibrary(node))
			{
				writer.AddToGroup(containerNode, assemblyNodeWrapper);
			}
			else
			{
				writer.AddToGroup(owner, assemblyNodeWrapper);
			}
			foreach (AssemblyNode dependency in node.Dependencies)
			{
				if (IsMicrosoftLibrary(dependency) && !IsMicrosoftLibrary(node))
				{
					writer.AddLink(assemblyNodeWrapper, containerNode);
				}
				else
				{
					writer.AddLink(new Link(node.ASMName, dependency.ASMName, ""));
				}
			}
		}
	}

	public static bool IsMicrosoftLibrary(AssemblyNode node)
	{
		return IsMicrosoftLibrary(node.VersionInfo);
	}

	public static bool IsMicrosoftLibrary(FileVersionInfo info)
	{
		return info.CompanyName?.ToLower().Contains("microsoft") ?? false;
	}
}
