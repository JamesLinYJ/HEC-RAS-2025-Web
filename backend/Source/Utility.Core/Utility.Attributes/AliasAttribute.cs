using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Utility.Attributes;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class AliasAttribute : Attribute
{
	public string Alias { get; set; }

	public AliasAttribute(string alias)
	{
		Alias = alias;
	}

	public static Dictionary<object, string> GetSerializeMap(Type enumType)
	{
		if (!enumType.IsEnum)
		{
			throw new ArgumentException(enumType.FullName + " is not an enumeration type.");
		}
		Dictionary<object, string> serializeMap = new Dictionary<object, string>();
		foreach (object getValue in Enum.GetValues(enumType))
		{
			if (enumType.GetMember(getValue.ToString()).FirstOrDefault().GetCustomAttribute(typeof(AliasAttribute)) is AliasAttribute aliasAttribute)
			{
				serializeMap[getValue] = aliasAttribute.Alias;
			}
			else
			{
				serializeMap[getValue] = getValue.ToString();
			}
		}
		return serializeMap;
	}

	public static Dictionary<string, object> GetDeserializeMap(Type enumType)
	{
		Dictionary<string, object> deserializeMap = new Dictionary<string, object>();
		foreach (object getValue in Enum.GetValues(enumType))
		{
			string getValue2 = getValue.ToString();
			MemberInfo? element = enumType.GetMember(getValue2).FirstOrDefault();
			deserializeMap[getValue2] = getValue;
			if (element.GetCustomAttribute(typeof(AliasAttribute)) is AliasAttribute aliasAttribute)
			{
				deserializeMap[aliasAttribute.Alias] = getValue;
			}
		}
		return deserializeMap;
	}
}
