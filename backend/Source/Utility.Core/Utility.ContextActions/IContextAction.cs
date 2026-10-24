using System;

namespace Utility.ContextActions;

public interface IContextAction : IContextBase
{
	Action Action { get; }
}
