using System;

namespace Visual.Observables.Linking;

public interface ILinked
{
	Link Link { get; set; }

	bool IsLinkable { get; }

	object GetLinkContext();

	void SetLinkContext(object localValue);
}
public interface ILinked<T> : ILinked where T : class
{
	T LinkContext { get; set; }

	T ValidatedContextCast(object localValue)
	{
		if (localValue != null && !(localValue is T))
		{
			throw new ArgumentException(localValue.GetType()?.ToString() + " is an invalid assignment to " + typeof(T));
		}
		if (localValue != null)
		{
			return (T)localValue;
		}
		return null;
	}

	void ILinked.SetLinkContext(object localValue)
	{
		if (LinkContext != localValue)
		{
			LinkContext = ValidatedContextCast(localValue);
		}
	}

	object ILinked.GetLinkContext()
	{
		return LinkContext;
	}
}
