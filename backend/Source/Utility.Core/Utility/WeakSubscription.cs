using System;

namespace Utility;

public static class WeakSubscription
{
	public static WeakSubscriptionHandle Subscribe(object publisher, string eventName, Action action)
	{
		return new WeakSubscriptionHandle(new WeakActionSubscription(publisher, eventName, action), action);
	}

	public static WeakSubscriptionHandle TrySubscribe(object publisher, string eventName, Action action)
	{
		if (publisher == null)
		{
			return null;
		}
		return Subscribe(publisher, eventName, action);
	}
}
