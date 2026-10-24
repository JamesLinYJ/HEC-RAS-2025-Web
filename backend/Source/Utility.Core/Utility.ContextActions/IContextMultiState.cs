using System.Collections.Generic;

namespace Utility.ContextActions;

public interface IContextMultiState : IContextBase
{
	List<object> AvailableStates { get; set; }

	object State { get; set; }
}
