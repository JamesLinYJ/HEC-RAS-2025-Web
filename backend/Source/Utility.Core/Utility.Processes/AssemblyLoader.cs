using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Utility.Processes;

public class AssemblyLoader
{
	public static AssemblyMap TryLoadAssemblyRecursive(string asm)
	{
		AssemblyNode assemblyNode = AssemblyNode.Load(asm);
		if (assemblyNode.Assembly == null)
		{
			return new AssemblyMap(new AssemblyNode[1] { assemblyNode });
		}
		Dictionary<string, AssemblyNode> assemblyNodeByString = new Dictionary<string, AssemblyNode>();
		assemblyNodeByString.Add(assemblyNode.Assembly.FullName, assemblyNode);
		RecurseAssemblyInternal(assemblyNode, assemblyNodeByString);
		return new AssemblyMap(assemblyNodeByString.Select((KeyValuePair<string, AssemblyNode> kvp) => kvp.Value));
	}

	private static void RecurseAssemblyInternal(AssemblyNode parentNode, Dictionary<string, AssemblyNode> uniqueAssemblies)
	{
		if (parentNode.Assembly == null)
		{
			return;
		}
		AssemblyName[] referencedAssemblies = parentNode.Assembly.GetReferencedAssemblies();
		for (int i = 0; i < referencedAssemblies.Length; i++)
		{
			AssemblyNode assemblyNode = AssemblyNode.Load(referencedAssemblies[i]);
			parentNode.Dependencies.Add(assemblyNode);
			if (!(assemblyNode.Assembly == null) && !uniqueAssemblies.ContainsKey(assemblyNode.Assembly.FullName))
			{
				uniqueAssemblies.Add(assemblyNode.Assembly.FullName, assemblyNode);
				RecurseAssemblyInternal(assemblyNode, uniqueAssemblies);
			}
		}
	}
}
