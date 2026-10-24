using System;

namespace Utility.Extensions.Attributes;

public static class AttributeExtensions
{
	public static TAttribute GetAttribute<TAttribute>(this object localValue) where TAttribute : Attribute
	{
		object[] customAttributes = localValue.GetType().GetMember(localValue.ToString())[0].GetCustomAttributes(typeof(TAttribute), inherit: false);
		if (customAttributes.Length == 0)
		{
			return null;
		}
		return (TAttribute)customAttributes[0];
	}
}
