using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Utility;

internal class WeakActionSubscription
{
	private WeakReference<Action> _actionRef;

	private WeakReference<object> _publisherRef;

	private EventInfo _event;

	private Delegate _forwarderDelegate;

	private DynamicMethod _forwarder;

	public WeakActionSubscription(object publisher, string eventName, Action action)
	{
		if (publisher != null)
		{
			_publisherRef = new WeakReference<object>(publisher);
			_actionRef = new WeakReference<Action>(action);
			_event = publisher.GetType().GetEvent(eventName);
			Type type = GetType();
			string name = "Execute";
			MethodInfo method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
			MethodInfo method2 = _event.EventHandlerType.GetMethod("Invoke");
			List<Type> typeValues = new List<Type>();
			typeValues.Add(type);
			typeValues.AddRange(from p in method2.GetParameters()
				select p.ParameterType);
			_forwarder = new DynamicMethod("Forward", method2.ReturnType, typeValues.ToArray());
			ILGenerator iLGenerator = _forwarder.GetILGenerator();
			iLGenerator.Emit(OpCodes.Ldarg_0);
			iLGenerator.Emit(OpCodes.Callvirt, method);
			iLGenerator.Emit(OpCodes.Ret);
			_forwarderDelegate = _forwarder.CreateDelegate(_event.EventHandlerType, this);
			_event.AddEventHandler(publisher, _forwarderDelegate);
		}
	}

	public void Unsubscribe()
	{
		_actionRef.SetTarget(null);
		if (_publisherRef.TryGetTarget(out var target))
		{
			_event.RemoveEventHandler(target, _forwarderDelegate);
		}
		_publisherRef.SetTarget(null);
		_forwarderDelegate = null;
		_forwarder = null;
	}

	private void Execute()
	{
		if (_actionRef.TryGetTarget(out var target))
		{
			target();
		}
		else
		{
			Unsubscribe();
		}
	}
}
