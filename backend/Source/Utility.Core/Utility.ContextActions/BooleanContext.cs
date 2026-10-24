using System;

namespace Utility.ContextActions;

public class BooleanContext : ContextBase, IContextBooleanState, IContextBase
{
	private Func<bool> _getter;

	private Action<bool> _setter;

	public bool State
	{
		get
		{
			return _getter();
		}
		set
		{
			_setter(value);
		}
	}

	public BooleanContext(string name, bool enabled, Func<bool> getter, Action<bool> setter)
		: base(name, enabled)
	{
		_getter = getter;
		_setter = setter;
	}
}
