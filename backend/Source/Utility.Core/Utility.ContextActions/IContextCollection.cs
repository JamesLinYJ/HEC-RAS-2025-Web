using Utility.DataStructures;

namespace Utility.ContextActions;

public interface IContextCollection : IContextBase
{
	bool FlattenHierarchy { get; }

	bool SeparateWhenFlattened { get; }

	bool ShowHeaderWhenFlattened { get; }

	AdvancedCollection<IContextBase> ContextElements { get; }
}
