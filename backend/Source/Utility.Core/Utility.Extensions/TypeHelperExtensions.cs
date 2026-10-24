using System;

namespace Utility.Extensions;

public static class TypeHelperExtensions
{
	public static Type GetEnumeratedType(this Type type)
	{
		Type elementType = type.GetElementType();
		if (null != elementType)
		{
			return elementType;
		}
		Type[] genericArguments = type.GetGenericArguments();
		if (genericArguments.Length != 0)
		{
			return genericArguments[0];
		}
		return null;
	}

	public static bool IsSubclassOfRawGeneric(this Type toCheck, Type targetParent)
	{
		while (toCheck != null && toCheck != typeof(object))
		{
			Type type = (toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck);
			if (targetParent == type)
			{
				return true;
			}
			toCheck = toCheck.BaseType;
		}
		return false;
	}
}
