using System;

namespace Utility;

public class SynchronizationContext : ISynchronizationContext
{
	public static SynchronizationContext None = new SynchronizationContext();

	private Action<Action> Context { get; }

	public SynchronizationContext(Action<Action> context)
	{
		Context = context;
	}

	public SynchronizationContext()
	{
		Context = (Action act) =>
		{
			act?.Invoke();
		};
	}

	public void Sync(Action action)
	{
		Context?.Invoke(action);
	}

	public void Sync()
	{
		Context(null);
	}

	public static implicit operator SynchronizationContext(Action<Action> action)
	{
		return new SynchronizationContext(action);
	}

	public static implicit operator Action<Action>(SynchronizationContext ctx)
	{
		return ctx.Context;
	}
}
