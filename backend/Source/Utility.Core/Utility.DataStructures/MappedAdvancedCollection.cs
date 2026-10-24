using System;
using System.Collections;
using System.Collections.Specialized;
using System.Linq;

namespace Utility.DataStructures;

public class MappedAdvancedCollection<T, TResult> : AdvancedCollection<TResult>, INotifyCollectionChanged
{
	private Func<T, TResult> _forwardTranslate;

	private Func<TResult, T> _reverseTranslate;

	private INotifyList<T> _baseCollection;

	private bool _baseCollectionChanging;

	private bool _mappedCollectionChanging;

	public MappedAdvancedCollection(INotifyList<T> baseCollection, Func<T, TResult> forwardTranslate, Func<TResult, T> reverseTranslate)
	{
		_forwardTranslate = forwardTranslate;
		_reverseTranslate = reverseTranslate;
		_baseCollection = baseCollection;
		foreach (T baseCollection2 in baseCollection)
		{
			Add(forwardTranslate(baseCollection2));
		}
		_baseCollection.CollectionChanged += OnBaseCollectionChanged;
	}

	protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
	{
		if (_baseCollectionChanging)
		{
			base.OnCollectionChanged(e);
			return;
		}
		_mappedCollectionChanging = true;
		if (e.Action == NotifyCollectionChangedAction.Reset)
		{
			((IList)_baseCollection).Clear();
		}
		else
		{
			if (e.NewItems != null)
			{
				_baseCollection.AddAll(from TResult t in e.NewItems
					select _reverseTranslate(t));
			}
			if (e.OldItems != null)
			{
				_baseCollection.RemoveAll(from TResult t in e.OldItems
					select _reverseTranslate(t));
			}
		}
		_mappedCollectionChanging = false;
	}

	private void OnBaseCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		if (_mappedCollectionChanging)
		{
			return;
		}
		_baseCollectionChanging = true;
		if (e.Action == NotifyCollectionChangedAction.Reset)
		{
			Clear();
		}
		else
		{
			if (e.NewItems != null)
			{
				AddAll(from T t in e.NewItems
					select _forwardTranslate(t));
			}
			if (e.OldItems != null)
			{
				RemoveAll(from T t in e.OldItems
					select _forwardTranslate(t));
			}
		}
		_baseCollectionChanging = false;
	}
}
