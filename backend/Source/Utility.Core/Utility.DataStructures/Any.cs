using System;

namespace Utility.DataStructures;

public class Any<T> where T : class
{
	private readonly T _item;

	private readonly Lazy<T> _lazy;

	private readonly T _defaultValue;

	private readonly Func<T> _getValue;

	public T Value
	{
		get
		{
			T localValue = _item;
			if (localValue == null)
			{
				Lazy<T> lazy = _lazy;
				localValue = ((lazy != null) ? lazy.Value : null);
				if (localValue == null)
				{
					Func<T> getValue = _getValue;
					localValue = ((getValue != null) ? getValue() : null) ?? _defaultValue;
				}
			}
			return localValue;
		}
	}

	public T ValueIfAlreadyEvaluated
	{
		get
		{
			if (_getValue != null)
			{
				return _getValue();
			}
			if (_item != null)
			{
				return _item;
			}
			if (_lazy != null && _lazy.IsValueCreated)
			{
				return _lazy.Value;
			}
			return _defaultValue;
		}
	}

	public Any(T localValue, T defaultValue = null)
	{
		_item = localValue;
		_defaultValue = defaultValue;
	}

	public Any(Func<T> accessor, T defaultValue = null)
	{
		_getValue = accessor;
		_defaultValue = null;
	}

	public Any(Lazy<T> lazy, T defaultValue = null)
	{
		_lazy = lazy;
		_defaultValue = defaultValue;
	}

	public static implicit operator Any<T>(T localValue)
	{
		return new Any<T>(localValue);
	}

	public static implicit operator Any<T>(Lazy<T> lazy)
	{
		return new Any<T>(lazy);
	}

	public static implicit operator Any<T>(Func<T> accessor)
	{
		return new Any<T>(new Lazy<T>(accessor));
	}

	public static implicit operator T(Any<T> any)
	{
		return any.Value;
	}
}
