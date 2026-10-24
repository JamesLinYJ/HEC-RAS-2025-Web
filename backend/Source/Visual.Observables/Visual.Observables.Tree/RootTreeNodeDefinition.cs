using System.Collections.Generic;
using Utility.DataStructures;

namespace Visual.Observables.Tree;

public class RootTreeNodeDefinition : ITreeNodeV2<TreeNodeDefinition>
{
	public IList<TreeNodeDefinition> Children { get; } = new List<TreeNodeDefinition>();

	IList<TreeNodeDefinition> ITreeNodeV2<TreeNodeDefinition>.Children => Children;
}
