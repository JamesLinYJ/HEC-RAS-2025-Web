using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;
using Utility.Attributes;
using Utility.Logging;
using Utility.ORM;

namespace H5Assist.ORM;

public static class H5Metadata
{
	private static readonly ILogger _logger = LogHelper.GetLogger("H5Metadata");

	public static void Write<T>(T instance, H5Writer hw, string rootH5Group, string prefix = "")
	{
		if (instance == null)
		{
			throw new ArgumentNullException("instance");
		}
		if (hw == null)
		{
			throw new ArgumentNullException("hw");
		}
		if (string.IsNullOrEmpty(rootH5Group))
		{
			throw new ArgumentNullException("rootH5Group");
		}
		if (!rootH5Group.EndsWith("/"))
		{
			rootH5Group += "/";
		}
		bool condition = false;
		Type type = instance.GetType();
		PropertyInfo[] allProperties = GetAllProperties(type);
		foreach (PropertyInfo propertyInfo in allProperties)
		{
			foreach (Attribute customAttribute in propertyInfo.GetCustomAttributes())
			{
				if (customAttribute is MetadataAttribute metadataAttribute)
				{
					if (!condition)
					{
						if (!hw.ExistsAsGroup(rootH5Group))
						{
							hw.CreateGroup(rootH5Group);
						}
						condition = true;
					}
					Type propertyType = propertyInfo.PropertyType;
					object getValue = propertyInfo.GetValue(instance);
					if (!string.IsNullOrEmpty(metadataAttribute.PathRelativeTarget))
					{
						if (getValue == null && !metadataAttribute.Optional && propertyType != typeof(string))
						{
							_logger.LogDebug("Cannot write null property {typeName}.{propName}", type.FullName, propertyInfo.Name);
						}
						else
						{
							string rootH5Group2 = H5Reader.PathCombine(rootH5Group, metadataAttribute.PathRelativeTarget);
							Write(getValue, hw, rootH5Group2);
						}
						continue;
					}
					string prefixPlusKeyName = prefix + metadataAttribute.KeyName;
					if (getValue == null)
					{
						if (!metadataAttribute.Optional && propertyType != typeof(string))
						{
							_logger.LogDebug("Cannot write null property {typeName}.{propName}", type.FullName, propertyInfo.Name);
						}
					}
					else if (propertyType == typeof(string) || propertyType == typeof(StringBuilder))
					{
						string getValue2 = ((!(propertyType == typeof(StringBuilder))) ? (getValue as string) : ((getValue as StringBuilder)?.ToString() ?? ""));
						if (!string.IsNullOrEmpty(getValue2))
						{
							hw.WriteAttribute(rootH5Group, prefixPlusKeyName, getValue2);
						}
						else if (hw.ExistsAsAttribute(rootH5Group, prefixPlusKeyName))
						{
							hw.DeleteAttribute(rootH5Group, prefixPlusKeyName);
						}
					}
					else if (propertyType.IsEnum)
					{
						if (AliasAttribute.GetSerializeMap(propertyType).TryGetValue(getValue, out var localValue))
						{
							hw.WriteAttribute(rootH5Group, prefixPlusKeyName, localValue);
							continue;
						}
						_logger.LogWarning("Cannot serialize Enum: {value} (Is this a valid enumerated state?)", getValue);
					}
					else if (propertyType.IsPrimitive)
					{
						if (propertyType == typeof(bool))
						{
							hw.WriteAttribute(rootH5Group, prefixPlusKeyName, (getValue as bool?).Value);
							continue;
						}
						if (propertyType == typeof(byte))
						{
							hw.WriteAttribute(rootH5Group, prefixPlusKeyName, (getValue as byte?).Value);
							continue;
						}
						if (propertyType == typeof(short))
						{
							hw.WriteAttribute(rootH5Group, prefixPlusKeyName, (getValue as short?).Value);
							continue;
						}
						if (propertyType == typeof(int))
						{
							hw.WriteAttribute(rootH5Group, prefixPlusKeyName, (getValue as int?).Value);
							continue;
						}
						if (propertyType == typeof(long))
						{
							hw.WriteAttribute(rootH5Group, prefixPlusKeyName, (getValue as long?).Value);
							continue;
						}
						if (propertyType == typeof(float))
						{
							hw.WriteAttribute(rootH5Group, prefixPlusKeyName, (getValue as float?).Value);
							continue;
						}
						if (propertyType == typeof(double))
						{
							hw.WriteAttribute(rootH5Group, prefixPlusKeyName, (getValue as double?).Value);
							continue;
						}
						_logger.LogWarning("Type not supported for auto serialization: {propName}", propertyType.FullName);
					}
					else if (propertyType == typeof(DateTime))
					{
						DateTime getValueDateTimeTerm = (getValue as DateTime?).Value;
						hw.WriteAttribute(rootH5Group, prefixPlusKeyName, getValueDateTimeTerm.Ticks);
						hw.WriteAttribute(rootH5Group, prefixPlusKeyName + " (DESC)", getValueDateTimeTerm.ToString());
					}
					else if (propertyType == typeof(TimeSpan))
					{
						TimeSpan getValueTimeSpanTerm = (getValue as TimeSpan?).Value;
						hw.WriteAttribute(rootH5Group, prefixPlusKeyName, getValueTimeSpanTerm.Ticks);
						hw.WriteAttribute(rootH5Group, prefixPlusKeyName + " (DESC)", getValueTimeSpanTerm.ToString());
					}
					else
					{
						(H5Serializers.TryGet(propertyType) ?? throw new NotSupportedException("Auto serialized properties must be primitives, strings, or enums. If you have a custom type, you can call H5SerializersRegister")).Write(hw, rootH5Group, prefixPlusKeyName, getValue);
					}
				}
				else if (customAttribute is ComplexMetadataAttribute complexMetadataAttribute)
				{
					_ = propertyInfo.PropertyType;
					Write(propertyInfo.GetValue(instance), hw, rootH5Group, complexMetadataAttribute.UsableColumnNamePrefix);
				}
			}
		}
	}

	public static bool Read<T>(T instance, H5Reader hr, string rootH5Group, string prefix = "")
	{
		if (instance == null)
		{
			throw new ArgumentNullException("instance");
		}
		if (hr == null)
		{
			throw new ArgumentNullException("hr");
		}
		if (!rootH5Group.EndsWith("/"))
		{
			rootH5Group += "/";
		}
		if (!hr.ExistsAsGroup(rootH5Group))
		{
			return false;
		}
		Dictionary<string, Type> attributeNamesAndTypes = hr.GetAttributeNamesAndTypes(rootH5Group);
		Dictionary<string, object> allAttributes = hr.ReadAllAttributes(rootH5Group);
		PropertyInfo[] allProperties = GetAllProperties(instance.GetType());
		foreach (PropertyInfo propertyInfo in allProperties)
		{
			if (!propertyInfo.CanWrite)
			{
				continue;
			}
			foreach (Attribute customAttribute in propertyInfo.GetCustomAttributes())
			{
				if (customAttribute is MetadataAttribute metadataAttribute)
				{
					Type propertyType = propertyInfo.PropertyType;
					if (!string.IsNullOrEmpty(metadataAttribute.PathRelativeTarget))
					{
						string rootH5Group2 = H5Reader.PathCombine(rootH5Group, metadataAttribute.PathRelativeTarget);
						ConstructorInfo? constructor = propertyType.GetConstructor(Type.EmptyTypes);
						if (constructor == null)
						{
							throw new Exception("No empty constructor found for " + propertyType.FullName);
						}
						object constructorTerm = constructor.Invoke(null);
						propertyInfo.SetValue(instance, constructorTerm);
						Read(constructorTerm, hr, rootH5Group2);
						continue;
					}
					string prefixPlusKeyName = prefix + metadataAttribute.KeyName;
					bool tryGetValue = attributeNamesAndTypes.TryGetValue(prefixPlusKeyName, out var localValue);
					if (!tryGetValue && metadataAttribute.LegacyKeyNames != null)
					{
						string[] legacyKeyNames = metadataAttribute.LegacyKeyNames;
						foreach (string legacyKeyName in legacyKeyNames)
						{
							string prefixPlusLegacyKeyName = prefix + legacyKeyName;
							if (attributeNamesAndTypes.TryGetValue(prefixPlusLegacyKeyName, out localValue))
							{
								prefixPlusKeyName = prefixPlusLegacyKeyName;
								tryGetValue = true;
								break;
							}
						}
					}
					if (!tryGetValue)
					{
						H5Serializer h5Serializer = H5Serializers.TryGet(propertyType);
						if (h5Serializer != null)
						{
							object read = h5Serializer.Read(hr, rootH5Group, prefixPlusKeyName);
							if (read != null)
							{
								propertyInfo.SetValue(instance, read);
								continue;
							}
						}
						if (!metadataAttribute.Optional)
						{
							MissingKeyError(rootH5Group, prefixPlusKeyName);
						}
						if (metadataAttribute.SetValueToDefaultIfKeyMissing)
						{
							propertyInfo.SetValue(instance, metadataAttribute.DefaultValue, null);
						}
					}
					else if (propertyType == typeof(string) || propertyType == typeof(StringBuilder))
					{
						if (localValue == typeof(string))
						{
							string allAttribute = allAttributes[prefixPlusKeyName] as string;
							if (propertyType == typeof(string))
							{
								propertyInfo.SetValue(instance, allAttribute);
							}
							else
							{
								propertyInfo.SetValue(instance, new StringBuilder(allAttribute));
							}
						}
						else
						{
							KeyTypeError(rootH5Group, prefixPlusKeyName, typeof(string), localValue);
						}
					}
					else if (propertyType.IsEnum)
					{
						if (localValue == typeof(string))
						{
							Dictionary<string, object> deserializeMap = AliasAttribute.GetDeserializeMap(propertyType);
							string allAttribute2 = allAttributes[prefixPlusKeyName] as string;
							if (deserializeMap.TryGetValue(allAttribute2, out var localValue2))
							{
								propertyInfo.SetValue(instance, localValue2);
							}
							else
							{
								OnEnumParseError(prefixPlusKeyName, allAttribute2, propertyType);
							}
						}
						else
						{
							KeyTypeError(rootH5Group, prefixPlusKeyName, typeof(string), localValue);
						}
					}
					else if (propertyType.IsPrimitive || propertyType == typeof(DateTime) || propertyType == typeof(TimeSpan))
					{
						if (localValue == propertyType)
						{
							object allAttribute3 = allAttributes[prefixPlusKeyName];
							propertyInfo.SetValue(instance, allAttribute3);
						}
						else if (localValue == typeof(long) && propertyType == typeof(DateTime))
						{
							long valueOrDefault = (allAttributes[prefixPlusKeyName] as long?).GetValueOrDefault();
							if (valueOrDefault < DateTime.MinValue.Ticks)
							{
								propertyInfo.SetValue(instance, DateTime.MinValue);
							}
							else if (valueOrDefault > DateTime.MaxValue.Ticks)
							{
								propertyInfo.SetValue(instance, DateTime.MaxValue);
							}
							else
							{
								propertyInfo.SetValue(instance, new DateTime(valueOrDefault));
							}
						}
						else if (localValue == typeof(long) && propertyType == typeof(TimeSpan))
						{
							long valueOrDefault2 = (allAttributes[prefixPlusKeyName] as long?).GetValueOrDefault();
							propertyInfo.SetValue(instance, new TimeSpan(valueOrDefault2));
						}
						else
						{
							KeyTypeError(rootH5Group, prefixPlusKeyName, propertyType, localValue);
						}
					}
					else
					{
						object read2 = (H5Serializers.TryGet(propertyType) ?? throw new NotSupportedException("Auto serialized properties must be primitives, strings, or enums. If you have a custom type, you can call Register")).Read(hr, rootH5Group, prefixPlusKeyName);
						propertyInfo.SetValue(instance, read2);
					}
				}
				else if (customAttribute is ComplexMetadataAttribute complexMetadataAttribute)
				{
					Type propertyType2 = propertyInfo.PropertyType;
					ConstructorInfo constructor2 = propertyType2.GetConstructor(Type.EmptyTypes);
					if (constructor2 == null)
					{
						throw new InvalidOperationException(propertyType2.ToString() + " must have a parameterless constructor to be build via the [ComplexColumn] attribute.");
					}
					propertyInfo.SetValue(instance, constructor2.Invoke(null));
					Read(propertyInfo.GetValue(instance), hr, rootH5Group, complexMetadataAttribute.UsableColumnNamePrefix);
				}
			}
		}
		return true;
	}

	private static void MissingKeyError(string grpName, string keyName)
	{
		_logger.LogDebug("Load Error: Non-Optional Attribute {grpName}[{keyName}] missing.", grpName, keyName);
	}

	private static void KeyTypeError(string grpName, string keyName, Type expected, Type actual)
	{
		_logger.LogDebug("Load Error: Non-Optional Attribute {grpName}[{keyName}] has an incorrect type. Expected: {expectedFullName}, Actual: {actualFullName}", grpName, keyName, expected.FullName, actual.FullName);
	}

	private static void OnEnumParseError(string colName, string textValue, Type enumType)
	{
		_logger.LogDebug("Loading Error: Column {colName} contains a value of {value} which could not be parsed as an instance of {enumName}", colName, textValue, enumType.FullName);
	}

	private static PropertyInfo[] GetAllProperties(Type type)
	{
		return type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	}
}
