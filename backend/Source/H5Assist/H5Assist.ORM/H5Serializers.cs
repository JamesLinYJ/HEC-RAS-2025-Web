using System;
using System.Collections.Generic;

namespace H5Assist.ORM;

public static class H5Serializers
{
	private static Dictionary<Type, H5Serializer> _serializers;

	static H5Serializers()
	{
		_serializers = new Dictionary<Type, H5Serializer>();
	}

	public static void Register(Type t, H5Serializer serializer)
	{
		_serializers[t] = serializer;
	}

	public static H5Serializer TryGet(Type t)
	{
		_serializers.TryGetValue(t, out var localValue);
		return localValue;
	}
}
