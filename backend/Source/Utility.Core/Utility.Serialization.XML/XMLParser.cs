using System;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Utility.Serialization.XML;

public class XMLParser
{
	private static readonly ILogger _logger = LogHelper.GetLogger("XMLParser");

	public static T Parse<T>(XElement element) where T : class
	{
		return Parse(typeof(T).GetConstructor(Type.EmptyTypes).Invoke(null) as T, element);
	}

	public static T Parse<T>(T parseTo, XElement element, bool forceResetToDefaults = false) where T : class
	{
		PropertyInfo[] properties = parseTo.GetType().GetProperties();
		foreach (PropertyInfo propertyInfo in properties)
		{
			XmlTagAttribute customAttribute = propertyInfo.GetCustomAttribute<XmlTagAttribute>(inherit: true);
			propertyInfo.GetCustomAttributes();
			object parseMember = ParseMember(element, customAttribute, propertyInfo.Name, propertyInfo.PropertyType, forceResetToDefaults);
			if (parseMember != null)
			{
				propertyInfo.SetValue(parseTo, parseMember);
			}
		}
		FieldInfo[] fields = parseTo.GetType().GetFields();
		foreach (FieldInfo fieldInfo in fields)
		{
			XmlTagAttribute customAttribute2 = fieldInfo.GetCustomAttribute<XmlTagAttribute>();
			object parseMember2 = ParseMember(element, customAttribute2, fieldInfo.Name, fieldInfo.FieldType, forceResetToDefaults);
			if (parseMember2 != null)
			{
				fieldInfo.SetValue(parseTo, parseMember2);
			}
		}
		return parseTo;
	}

	private static object ParseMember(XElement element, XmlTagAttribute xmlTag, string memberName, Type memberType, bool forceDefault = false)
	{
		if (xmlTag == null)
		{
			return null;
		}
		if (forceDefault)
		{
			return xmlTag.DefaultValue;
		}
		XAttribute xAttribute = element.Attribute(xmlTag.Tag);
		if (xAttribute == null)
		{
			return xmlTag.DefaultValue;
		}
		try
		{
			if (memberType.IsAssignableTo(typeof(bool)))
			{
				return bool.Parse(xAttribute.Value);
			}
			if (memberType.IsAssignableTo(typeof(string)))
			{
				return xAttribute.Value.ToString();
			}
			if (memberType.IsAssignableTo(typeof(byte)))
			{
				return byte.Parse(xAttribute.Value);
			}
			if (memberType.IsAssignableTo(typeof(short)))
			{
				return short.Parse(xAttribute.Value);
			}
			if (memberType.IsAssignableTo(typeof(int)))
			{
				return int.Parse(xAttribute.Value);
			}
			if (memberType.IsAssignableTo(typeof(long)))
			{
				return long.Parse(xAttribute.Value);
			}
			if (memberType.IsEnum)
			{
				try
				{
					if (!Enum.TryParse(memberType, xAttribute.Value.ToString(), out object result))
					{
						throw new ArgumentException($"The value '{xAttribute.Value}' is not a valid name for enum '{memberType.Name}'.");
					}
					return result;
				}
				catch (Exception innerException)
				{
					throw new ArgumentException($"Failed to parse '{xAttribute.Value}' to the enum '{memberType.Name}'", innerException);
				}
			}
			throw new NotImplementedException();
		}
		catch (Exception)
		{
		}
		_logger.LogWarning("Failed to parse xml member {member} with value {value} because it does not match the required type {type}", memberName, xAttribute.Value, memberType.ToString());
		return xmlTag.DefaultValue;
	}

	public static void Serialize<T>(T toSerialize, XElement element)
	{
		PropertyInfo[] properties = toSerialize.GetType().GetProperties();
		foreach (PropertyInfo propertyInfo in properties)
		{
			XmlTagAttribute customAttribute = propertyInfo.GetCustomAttribute<XmlTagAttribute>(inherit: true);
			if (customAttribute != null)
			{
				object getValue = propertyInfo.GetValue(toSerialize);
				if (getValue != null && !getValue.Equals(customAttribute.DefaultValue))
				{
					element.SetAttributeValue(customAttribute.Tag, getValue);
				}
			}
		}
		FieldInfo[] fields = toSerialize.GetType().GetFields();
		foreach (FieldInfo fieldInfo in fields)
		{
			XmlTagAttribute customAttribute2 = fieldInfo.GetCustomAttribute<XmlTagAttribute>();
			if (customAttribute2 != null)
			{
				object getValue2 = fieldInfo.GetValue(toSerialize);
				if (getValue2 != null && !getValue2.Equals(customAttribute2.DefaultValue))
				{
					element.SetAttributeValue(customAttribute2.Tag, getValue2);
				}
			}
		}
	}
}
