using System;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Utility.Helpers;

public abstract class WeakHandler
{
	public abstract void Unsubscribe();

	public static WeakHandler TrySubscribe<TModel>(object publisher, string eventName, TModel model, Action<TModel> action) where TModel : class
	{
		if (publisher == null)
		{
			return null;
		}
		return new WeakEventHandler<TModel>(publisher, eventName, model, action);
	}

	public static WeakHandler TrySubscribe<TModel, TArg1>(object publisher, string eventName, TModel model, Action<TModel, TArg1> action) where TModel : class
	{
		if (publisher == null)
		{
			return null;
		}
		return new WeakEventHandler<TModel, TArg1>(publisher, eventName, model, action);
	}

	public static WeakHandler TrySubscribe<TModel, TArg1, TArg2>(object publisher, string eventName, TModel model, Action<TModel, TArg1, TArg2> action) where TModel : class
	{
		if (publisher == null)
		{
			return null;
		}
		return new WeakEventHandler<TModel, TArg1, TArg2>(publisher, eventName, model, action);
	}

	public static WeakHandler TrySubscribe<TModel, TArg1, TArg2, TArg3>(object publisher, string eventName, TModel model, Action<TModel, TArg1, TArg2, TArg3> action) where TModel : class
	{
		if (publisher == null)
		{
			return null;
		}
		return new WeakEventHandler<TModel, TArg1, TArg2, TArg3>(publisher, eventName, model, action);
	}

	public static WeakHandler TrySubscribePropertyChanged<TModel>(object publisher, TModel model, Action<TModel, object, PropertyChangedEventArgs> action) where TModel : class
	{
		if (publisher == null)
		{
			return null;
		}
		return new WeakEventHandler<TModel, object, PropertyChangedEventArgs>(publisher, "PropertyChanged", model, action);
	}

	public static WeakHandler TrySubscribeCollectionChanged<TModel>(object publisher, TModel model, Action<TModel, object, NotifyCollectionChangedEventArgs> action) where TModel : class
	{
		if (!(publisher is INotifyCollectionChanged))
		{
			return null;
		}
		return new WeakEventHandler<TModel, object, NotifyCollectionChangedEventArgs>(publisher, "CollectionChanged", model, action);
	}
}
