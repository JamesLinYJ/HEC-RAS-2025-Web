using System;

namespace Utility;

public struct InjectableAction<TInputs, KLocalVariables>
{
	private Action<TInputs, KLocalVariables> _action;

	private KLocalVariables _localVars;

	public InjectableAction(Action<TInputs, KLocalVariables> action, KLocalVariables localVars)
	{
		if (!action.Method.IsStatic)
		{
			throw new InvalidOperationException("Action must be static, or it defeats the purpose if InjectableAction - state can get injected.");
		}
		_action = action;
		_localVars = localVars;
	}

	public void Execute(TInputs inputs)
	{
		_action?.Invoke(inputs, _localVars);
	}
}
