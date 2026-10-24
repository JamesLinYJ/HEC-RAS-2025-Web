using System;

namespace Utility.DataStructures;

public sealed class CloneAnyWrapper<T> where T : class
{
	private readonly Func<T, T> _clone;

	private T _value;

	public T Value => _value;

	public CloneAnyWrapper(T live, Func<T, T> clone)
	{
		if (live == null)
		{
			throw new ArgumentNullException("live");
		}
		if (clone == null)
		{
			throw new ArgumentNullException("clone");
		}
		_clone = clone;
		_value = clone(live);
	}

	public Any<T> AsAny()
	{
		return new Any<T>(() => _value);
	}

	public void Commit(T clone)
	{
		if (clone == null)
		{
			throw new ArgumentNullException("clone");
		}
		_value = clone;
	}

	public void Refresh(T live)
	{
		if (live == null)
		{
			throw new ArgumentNullException("live");
		}
		Commit(_clone(live));
	}
}
