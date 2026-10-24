using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Utility.Attributes;

public static class AttributeHelpers
{
	public static bool HasAttribute(this Type classType, Type classAttributeType, bool inherit = true)
	{
		return (from att in classType.GetCustomAttributes(inherit)
			where att.GetType().Equals(classAttributeType)
			select att).Count() > 0;
	}

	public static bool HasAttribute<T>(this Type classType, bool inherit = true)
	{
		return classType.HasAttribute(typeof(T), inherit);
	}

	public static bool HasAttribute(this MemberInfo member, Type memberAttributeType, bool inherit = true)
	{
		return (from att in member.GetCustomAttributes(inherit)
			where att.GetType().Equals(memberAttributeType)
			select att).Count() > 0;
	}

	public static bool HasAttribute<T>(this MemberInfo member, bool inherit = true) where T : Attribute
	{
		return member.HasAttribute(typeof(T), inherit);
	}

	public static bool HasAttribute<T>(this Enum enu, bool inherit = true) where T : Attribute
	{
		return (from att in enu.GetType().GetField(enu.ToString()).GetCustomAttributes(inherit)
			where att.GetType().Equals(typeof(T))
			select att).Count() > 0;
	}

	public static List<Type> GetClassesWithAttribute(this Assembly assembly, Type classAttributeType, bool inherit = true)
	{
		return (from type in assembly.GetTypes()
			where type.IsClass && type.HasAttribute(classAttributeType, inherit)
			select type).ToList();
	}

	public static List<MemberInfo> GetMembersWithAttribute(this Type classType, Type memberAttributeType, bool inherit = true, params Type[] allowedMemberTypes)
	{
		return classType.GetMembersWithAttribute(memberAttributeType, inherit, memberAttributeIsWhitelist: true, allowedMembersIsWhitelist: true, allowedMemberTypes);
	}

	public static List<MemberInfo> GetMembersWithoutAttribute(this Type classType, Type memberAttributeType, bool inherit = true, params Type[] allowedMemberTypes)
	{
		return classType.GetMembersWithAttribute(memberAttributeType, inherit, memberAttributeIsWhitelist: false, allowedMembersIsWhitelist: true, allowedMemberTypes);
	}

	private static List<MemberInfo> GetMembersWithAttribute(this Type classType, Type memberAttributeType, bool inherit = true, bool memberAttributeIsWhitelist = true, bool allowedMembersIsWhitelist = true, params Type[] allowedMemberTypes)
	{
		if (!classType.IsClass)
		{
			throw new ArgumentException(classType.ToString() + " is not a class.");
		}
		return classType.GetFields().Cast<MemberInfo>().Union(classType.GetProperties())
			.Where((MemberInfo member) =>
			{
				bool hasAttribute = member.HasAttribute(memberAttributeType, inherit);
				bool allowedMemberTypeLengthIsZero = allowedMemberTypes.Length == 0;
				Type underlyingType = member.GetUnderlyingType();
				Type[] allowedMemberTypesArray = allowedMemberTypes;
				foreach (Type targetType in allowedMemberTypesArray)
				{
					if (underlyingType.IsAssignableTo(targetType))
					{
						allowedMemberTypeLengthIsZero = true;
						break;
					}
				}
				return (memberAttributeIsWhitelist ? hasAttribute : (!hasAttribute)) & (allowedMembersIsWhitelist ? allowedMemberTypeLengthIsZero : (!allowedMemberTypeLengthIsZero));
			})
			.ToList();
	}

	public static List<MemberInfo> GetMembersWithAttribute(this Assembly assembly, Type memberAttributeType, Type classAttributeType = null, bool inheritMember = true, bool inheritClass = true, params Type[] allowedMemberTypes)
	{
		return assembly.GetMembersWithAttribute(memberAttributeType, classAttributeType, inheritMember, inheritClass, memberAttributeIsWhitelist: true, allowedMembersIsWhitelist: true, allowedMemberTypes);
	}

	public static List<MemberInfo> GetMembersWithoutAttribute(this Assembly assembly, Type memberAttributeType, Type classAttributeType = null, bool inheritMember = true, bool inheritClass = true, params Type[] allowedMemberTypes)
	{
		return assembly.GetMembersWithAttribute(memberAttributeType, classAttributeType, inheritMember, inheritClass, memberAttributeIsWhitelist: false, allowedMembersIsWhitelist: true, allowedMemberTypes);
	}

	private static List<MemberInfo> GetMembersWithAttribute(this Assembly assembly, Type memberAttributeType, Type classAttributeType = null, bool inheritMember = true, bool inheritClass = true, bool memberAttributeIsWhitelist = true, bool allowedMembersIsWhitelist = true, params Type[] allowedMemberTypes)
	{
		List<Type> getTypes = ((classAttributeType == null) ? assembly.GetTypes().ToList() : assembly.GetClassesWithAttribute(classAttributeType, inheritClass));
		List<MemberInfo> memberInfos = new List<MemberInfo>();
		foreach (Type getType in getTypes)
		{
			memberInfos.AddRange(getType.GetMembersWithAttribute(memberAttributeType, inheritMember, memberAttributeIsWhitelist, allowedMembersIsWhitelist, allowedMemberTypes));
		}
		return memberInfos;
	}

	private static Type GetUnderlyingType(this MemberInfo member)
	{
		return member.MemberType switch
		{
			MemberTypes.Event => ((EventInfo)member).EventHandlerType, 
			MemberTypes.Field => ((FieldInfo)member).FieldType, 
			MemberTypes.Method => ((MethodInfo)member).ReturnType, 
			MemberTypes.Property => ((PropertyInfo)member).PropertyType, 
			_ => throw new ArgumentException("Input MemberInfo must be if type EventInfo, FieldInfo, MethodInfo, or PropertyInfo"), 
		};
	}

	public static T GetAttribute<T>(Enum enumValue) where T : Attribute
	{
		if (enumValue == null)
		{
			throw new ArgumentNullException("enumValue");
		}
		MemberInfo[] member = enumValue.GetType().GetMember(enumValue.ToString());
		if (member.Length == 0)
		{
			return null;
		}
		return member[0].GetCustomAttributes(typeof(T), inherit: false).OfType<T>().FirstOrDefault();
	}

	public static T GetAttribute<T>(PropertyInfo propInfo) where T : Attribute
	{
		if (propInfo == null)
		{
			throw new ArgumentNullException("propInfo");
		}
		return propInfo.GetCustomAttributes(typeof(T), inherit: false).OfType<T>().FirstOrDefault();
	}
}
