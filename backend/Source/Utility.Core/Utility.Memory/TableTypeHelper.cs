using System;

namespace Utility.Memory;

public static class TableTypeHelper
{
	public static Type Type => typeof(TableType);

	public static TableType AsTableType(Type type)
	{
		if (type == typeof(string))
		{
			return TableType.String;
		}
		if (type == typeof(int))
		{
			return TableType.Integer;
		}
		if (type == typeof(double) || type == typeof(float))
		{
			return TableType.Numeric;
		}
		if (type == typeof(bool))
		{
			return TableType.Boolean;
		}
		throw new NotSupportedException();
	}

	public static Type AsType(this TableType type)
	{
		return type switch
		{
			TableType.String => typeof(string), 
			TableType.Integer => typeof(int), 
			TableType.Numeric => typeof(double), 
			TableType.Boolean => typeof(bool), 
			_ => throw new NotSupportedException(), 
		};
	}
}
