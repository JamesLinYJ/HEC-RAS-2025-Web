using System;
using Utility.Extensions.Attributes;

namespace Utility.Extensions;

public sealed class DiskNameAttribute : Attribute
{
	public string DiskName { get; private set; }

	public DiskNameAttribute(string diskName)
	{
		DiskName = diskName;
	}

	public static string Serialize<T>(T localValue, string fallbackName = "")
	{
		if (localValue == null)
		{
			return fallbackName;
		}
		DiskNameAttribute attribute = localValue.GetAttribute<DiskNameAttribute>();
		if (attribute == null)
		{
			return fallbackName;
		}
		return attribute.DiskName;
	}

	public static T Deserialize<T>(string s, T defaultReturn = default(T)) where T : struct, Enum
	{
		if (string.IsNullOrEmpty(s))
		{
			return defaultReturn;
		}
		foreach (T enumerate in EnumHelpers.Enumerate<T>())
		{
			DiskNameAttribute attribute = enumerate.GetAttribute<DiskNameAttribute>();
			if (attribute != null && attribute.DiskName == s)
			{
				return enumerate;
			}
		}
		return defaultReturn;
	}

	public static bool TryDeserialize<T>(string s, out T retn) where T : struct, Enum
	{
		if (string.IsNullOrEmpty(s))
		{
			retn = default;
			return false;
		}
		foreach (T enumerate in EnumHelpers.Enumerate<T>())
		{
			DiskNameAttribute attribute = enumerate.GetAttribute<DiskNameAttribute>();
			if (attribute != null && attribute.DiskName == s)
			{
				retn = enumerate;
				return true;
			}
		}
		retn = default;
		return false;
	}

	public static void GetDefaultEnumSerializers<T>(out Func<object, string> serializer, out Func<string, object> deserializer, out int maxStringLength, T fallbackValue) where T : struct, Enum
	{
		serializer = (object localValue) => Serialize(localValue, fallbackValue.GetAttribute<DiskNameAttribute>().DiskName);
		deserializer = (string textValue) => Deserialize(textValue, fallbackValue);
		maxStringLength = 0;
		foreach (T enumerate in EnumHelpers.Enumerate<T>())
		{
			DiskNameAttribute attribute = enumerate.GetAttribute<DiskNameAttribute>();
			if (attribute != null)
			{
				maxStringLength = Math.Max(attribute.DiskName.Length, maxStringLength);
			}
		}
	}
}
