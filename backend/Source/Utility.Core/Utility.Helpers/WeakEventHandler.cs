using System;
using System.Reflection;

namespace Utility.Helpers;

public class WeakEventHandler<TModel> : WeakHandler where TModel : class
{
	private WeakReference<object> _publisherRef;

	private WeakReference<TModel> _modelRef;

	private Action<TModel> _action;

	private EventInfo _event;

	private Delegate _forwarderDelegate;

	private bool _isUnsubscribed;

	public WeakEventHandler(object publisher, string eventName, TModel model, Action<TModel> action)
	{
		_publisherRef = new WeakReference<object>(publisher);
		_modelRef = new WeakReference<TModel>(model);
		_action = action;
		_event = publisher.GetType().GetEvent(eventName);
		if (_event == null)
		{
			throw new ArgumentException("Event '" + eventName + "' not found on type " + publisher.GetType().Name);
		}
		MethodInfo method = GetType().GetMethod("Execute", BindingFlags.Instance | BindingFlags.NonPublic);
		_forwarderDelegate = Delegate.CreateDelegate(_event.EventHandlerType, this, method);
		_event.AddEventHandler(publisher, _forwarderDelegate);
	}

	private void Execute()
	{
		if (!_isUnsubscribed)
		{
			if (_modelRef.TryGetTarget(out var target))
			{
				_action(target);
			}
			else
			{
				Unsubscribe();
			}
		}
	}

	public override void Unsubscribe()
	{
		if (!_isUnsubscribed)
		{
			_isUnsubscribed = true;
			if (_publisherRef.TryGetTarget(out var target))
			{
				_event.RemoveEventHandler(target, _forwarderDelegate);
			}
			_publisherRef.SetTarget(null);
			_modelRef.SetTarget(null);
			_publisherRef = null;
			_modelRef = null;
			_action = null;
			_event = null;
			_forwarderDelegate = null;
		}
	}
}
public class WeakEventHandler<TModel, TArg1> : WeakHandler where TModel : class
{
	private WeakReference<object> _publisherRef;

	private WeakReference<TModel> _modelRef;

	private Action<TModel, TArg1> _action;

	private EventInfo _event;

	private Delegate _forwarderDelegate;

	private bool _isUnsubscribed;

	public WeakEventHandler(object publisher, string eventName, TModel model, Action<TModel, TArg1> action)
	{
		_publisherRef = new WeakReference<object>(publisher);
		_modelRef = new WeakReference<TModel>(model);
		_action = action;
		_event = publisher.GetType().GetEvent(eventName);
		if (_event == null)
		{
			throw new ArgumentException("Event '" + eventName + "' not found on type " + publisher.GetType().Name);
		}
		MethodInfo method = GetType().GetMethod("Execute", BindingFlags.Instance | BindingFlags.NonPublic);
		_forwarderDelegate = Delegate.CreateDelegate(_event.EventHandlerType, this, method);
		_event.AddEventHandler(publisher, _forwarderDelegate);
	}

	private void Execute(TArg1 tArg1)
	{
		if (!_isUnsubscribed)
		{
			if (_modelRef.TryGetTarget(out var target))
			{
				_action(target, tArg1);
			}
			else
			{
				Unsubscribe();
			}
		}
	}

	public override void Unsubscribe()
	{
		if (!_isUnsubscribed)
		{
			_isUnsubscribed = true;
			if (_publisherRef.TryGetTarget(out var target))
			{
				_event.RemoveEventHandler(target, _forwarderDelegate);
			}
			_publisherRef.SetTarget(null);
			_modelRef.SetTarget(null);
			_publisherRef = null;
			_modelRef = null;
			_action = null;
			_event = null;
			_forwarderDelegate = null;
		}
	}
}
public class WeakEventHandler<TModel, TArg1, TArg2> : WeakHandler where TModel : class
{
	private WeakReference<object> _publisherRef;

	private WeakReference<TModel> _modelRef;

	private Action<TModel, TArg1, TArg2> _action;

	private EventInfo _event;

	private Delegate _forwarderDelegate;

	private bool _isUnsubscribed;

	public WeakEventHandler(object publisher, string eventName, TModel model, Action<TModel, TArg1, TArg2> action)
	{
		_publisherRef = new WeakReference<object>(publisher);
		_modelRef = new WeakReference<TModel>(model);
		_action = action;
		_event = publisher.GetType().GetEvent(eventName);
		if (_event == null)
		{
			throw new ArgumentException("Event '" + eventName + "' not found on type " + publisher.GetType().Name);
		}
		MethodInfo method = GetType().GetMethod("Execute", BindingFlags.Instance | BindingFlags.NonPublic);
		_forwarderDelegate = Delegate.CreateDelegate(_event.EventHandlerType, this, method);
		_event.AddEventHandler(publisher, _forwarderDelegate);
	}

	private void Execute(TArg1 tArg1, TArg2 tArg2)
	{
		if (!_isUnsubscribed)
		{
			if (_modelRef.TryGetTarget(out var target))
			{
				_action(target, tArg1, tArg2);
			}
			else
			{
				Unsubscribe();
			}
		}
	}

	public override void Unsubscribe()
	{
		if (!_isUnsubscribed)
		{
			_isUnsubscribed = true;
			if (_publisherRef.TryGetTarget(out var target))
			{
				_event.RemoveEventHandler(target, _forwarderDelegate);
			}
			_publisherRef.SetTarget(null);
			_modelRef.SetTarget(null);
			_publisherRef = null;
			_modelRef = null;
			_action = null;
			_event = null;
			_forwarderDelegate = null;
		}
	}
}
public class WeakEventHandler<TModel, TArg1, TArg2, TArg3> : WeakHandler where TModel : class
{
	private WeakReference<object> _publisherRef;

	private WeakReference<TModel> _modelRef;

	private Action<TModel, TArg1, TArg2, TArg3> _action;

	private EventInfo _event;

	private Delegate _forwarderDelegate;

	private bool _isUnsubscribed;

	public WeakEventHandler(object publisher, string eventName, TModel model, Action<TModel, TArg1, TArg2, TArg3> action)
	{
		_publisherRef = new WeakReference<object>(publisher);
		_modelRef = new WeakReference<TModel>(model);
		_action = action;
		_event = publisher.GetType().GetEvent(eventName);
		if (_event == null)
		{
			throw new ArgumentException("Event '" + eventName + "' not found on type " + publisher.GetType().Name);
		}
		MethodInfo method = GetType().GetMethod("Execute", BindingFlags.Instance | BindingFlags.NonPublic);
		_forwarderDelegate = Delegate.CreateDelegate(_event.EventHandlerType, this, method);
		_event.AddEventHandler(publisher, _forwarderDelegate);
	}

	private void Execute(TArg1 tArg1, TArg2 tArg2, TArg3 tArg3)
	{
		if (!_isUnsubscribed)
		{
			if (_modelRef.TryGetTarget(out var target))
			{
				_action(target, tArg1, tArg2, tArg3);
			}
			else
			{
				Unsubscribe();
			}
		}
	}

	public override void Unsubscribe()
	{
		if (!_isUnsubscribed)
		{
			_isUnsubscribed = true;
			if (_publisherRef.TryGetTarget(out var target))
			{
				_event.RemoveEventHandler(target, _forwarderDelegate);
			}
			_publisherRef.SetTarget(null);
			_modelRef.SetTarget(null);
			_publisherRef = null;
			_modelRef = null;
			_action = null;
			_event = null;
			_forwarderDelegate = null;
		}
	}
}
