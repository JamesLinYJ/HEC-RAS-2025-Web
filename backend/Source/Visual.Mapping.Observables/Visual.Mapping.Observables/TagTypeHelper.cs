using System;

namespace Visual.Mapping.Observables;

public static class TagTypeHelper
{
	public static Type Type => typeof(TagType);

	public static TagType AsTagType(Type type)
	{
		if (type == typeof(string))
		{
			return TagType.String;
		}
		if (type == typeof(int))
		{
			return TagType.Integer;
		}
		if (type == typeof(double) || type == typeof(float))
		{
			return TagType.Numeric;
		}
		if (type == typeof(bool))
		{
			return TagType.Boolean;
		}
		throw new NotSupportedException();
	}

	public static Type AsType(this TagType type)
	{
		return type switch
		{
			TagType.String => typeof(string), 
			TagType.Integer => typeof(int), 
			TagType.Numeric => typeof(double), 
			TagType.Boolean => typeof(bool), 
			_ => throw new NotSupportedException(), 
		};
	}
}
