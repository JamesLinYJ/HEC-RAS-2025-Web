using System.Collections.Generic;
using Utility.DataStructures;

namespace Visual.Observables.Tree;

public class TreeNodeDefinition : ITreeNodeV2<TreeNodeDefinition>, IMutableTreeNodeChildV2<TreeNodeDefinition>, ITreeNodeChildV2<TreeNodeDefinition>
{
	public IList<TreeNodeDefinition> Children { get; set; } = new List<TreeNodeDefinition>();

	public ITreeNodeV2<TreeNodeDefinition> Parent { get; set; }
}
