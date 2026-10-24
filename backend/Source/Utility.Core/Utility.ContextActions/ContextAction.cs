using System;

namespace Utility.ContextActions;

public class ContextAction : ContextBase, IContextAction, IContextBase
{
	public Action Action { get; set; }

	public ContextAction(string name, bool enabled, Action action)
		: base(name, enabled)
	{
		Action = action;
	}
}
