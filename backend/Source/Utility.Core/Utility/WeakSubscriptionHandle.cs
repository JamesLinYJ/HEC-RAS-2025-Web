using System;

namespace Utility;

public class WeakSubscriptionHandle
{
	private WeakActionSubscription _subscription;

	private Action _action;

	internal WeakSubscriptionHandle(WeakActionSubscription subscription, Action action)
	{
		_subscription = subscription;
		_action = action;
	}

	public void Unsubscribe()
	{
		_subscription?.Unsubscribe();
		_subscription = null;
		_action = null;
	}
}
