using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Utility.Attributes;

namespace Utility.Reflection;

public static class ReflectionHelpers
{
	public static Assembly[] GetLoadedAssemblies(AssemblyExcludeCriteria exclude = AssemblyExcludeCriteria.None)
	{
		if (exclude == AssemblyExcludeCriteria.None)
		{
			return CurrentAssemblies();
		}
		return CurrentAssemblies().WhereNotMsft().ToArray();
	}

	public static Assembly[] GetLoadedAssembliesThatReference(Assembly asm, AssemblyExcludeCriteria exclude = AssemblyExcludeCriteria.None)
	{
		if (exclude == AssemblyExcludeCriteria.None)
		{
			return (from a in CurrentAssemblies()
				where References(a, asm)
				select a).ToArray();
		}
		return CurrentAssemblies().WhereNotMsft().ToArray();
	}

	private static Assembly[] CurrentAssemblies()
	{
		return AppDomain.CurrentDomain.GetAssemblies();
	}

	private static bool References(Assembly src, Assembly target)
	{
		return src.GetReferencedAssemblies().Any((AssemblyName asm) => asm.FullName.Equals(target.FullName));
	}

	private static IEnumerable<Assembly> WhereNotMsft(this IEnumerable<Assembly> assemblies)
	{
		return assemblies.Where((Assembly asm) => !asm.FullName.StartsWith("System.") && !asm.FullName.StartsWith("Microsoft.") && !asm.FullName.StartsWith("netstandard"));
	}

	public static List<Type> FindAllImplementationsOf(Type tType, AssemblyExcludeCriteria exclude = AssemblyExcludeCriteria.None)
	{
		Assembly assembly = Assembly.GetAssembly(tType);
		Assembly[] loadedAssembliesThatReference = GetLoadedAssembliesThatReference(assembly, exclude);
		List<Type> allImplementationsOf = new List<Type>();
		if (!loadedAssembliesThatReference.Contains(assembly, null))
		{
			allImplementationsOf.AddRange(FindImplInternal(assembly, tType));
		}
		Assembly[] loadedAssembliesThatReferenceArray = loadedAssembliesThatReference;
		foreach (Assembly asm in loadedAssembliesThatReferenceArray)
		{
			allImplementationsOf.AddRange(FindImplInternal(asm, tType));
		}
		return allImplementationsOf;
	}

	public static List<Type> FindAllImplementationsOf<T>(AssemblyExcludeCriteria exclude = AssemblyExcludeCriteria.None)
	{
		AssertIsInterface<T>(out var tType);
		Assembly assembly = Assembly.GetAssembly(tType);
		Assembly[] loadedAssembliesThatReference = GetLoadedAssembliesThatReference(assembly, exclude);
		List<Type> allImplementationsOf = new List<Type>();
		if (!loadedAssembliesThatReference.Contains(assembly, null))
		{
			allImplementationsOf.AddRange(FindImplInternal(assembly, tType));
		}
		Assembly[] loadedAssembliesThatReferenceArray = loadedAssembliesThatReference;
		foreach (Assembly asm in loadedAssembliesThatReferenceArray)
		{
			allImplementationsOf.AddRange(FindImplInternal(asm, tType));
		}
		return allImplementationsOf;
	}

	public static List<Type> FindAllImplementationsOf<T>(Assembly asm)
	{
		AssertIsInterface<T>(out var tType);
		return FindImplInternal(asm, tType).ToList();
	}

	public static List<Type> FindAllImplementationsOf<T>(IEnumerable<Assembly> asms)
	{
		AssertIsInterface<T>(out var tType);
		List<Type> allImplementationsOf = new List<Type>();
		foreach (Assembly asm in asms)
		{
			allImplementationsOf.AddRange(FindImplInternal(asm, tType));
		}
		return allImplementationsOf;
	}

	private static void AssertIsInterface<T>(out Type tType)
	{
		tType = typeof(T);
		if (!tType.IsInterface)
		{
			throw new Exception("Given Type " + tType.FullName + " must be an interface.");
		}
	}

	private static IEnumerable<Type> FindImplInternal(Assembly asm, Type tType)
	{
		return asm.ExportedTypes.Where((Type t) => !t.IsInterface && t.IsAssignableTo(tType));
	}

	public static object EvaluatePath(this object localValue, string[] path)
	{
		object path2 = localValue;
		foreach (string pathPart in path)
		{
			path2 = EvaluatePathInfo(path2, pathPart).GetValue(path2);
		}
		return path2;
	}

	public static MemberInfo EvaluatePathInfo(this object localValue, string[] path)
	{
		MemberInfo memberInfo = null;
		object getValueMemberInfoTerm = localValue;
		foreach (string pathPart in path)
		{
			memberInfo = EvaluatePathInfo(getValueMemberInfoTerm, pathPart);
			getValueMemberInfoTerm = memberInfo.GetValue(getValueMemberInfoTerm);
		}
		return memberInfo;
	}

	public static MemberInfo EvaluatePathInfo(object localValue, string pathPart)
	{
		MemberInfo[] member = localValue.GetType().GetMember(pathPart);
		if (member.Length == 0)
		{
			throw new InvalidOperationException(pathPart + " is not a valid field/property on " + localValue.GetType().ToString());
		}
		return member[0];
	}

	public static object EvaluatePath(object localValue, string pathPart)
	{
		return EvaluatePathInfo(localValue, pathPart).GetValue(localValue);
	}

	public static object GetValue(this MemberInfo mem, object localValue)
	{
		return mem.MemberType switch
		{
			MemberTypes.Field => (mem as FieldInfo).GetValue(localValue), 
			MemberTypes.Property => (mem as PropertyInfo).GetValue(localValue), 
			_ => throw new InvalidOperationException(mem.Name + " is not a valid field/property on " + localValue.GetType().ToString()), 
		};
	}

	public static bool HasDefaultConstructor(this Type type)
	{
		return type.GetConstructor(Type.EmptyTypes) != null;
	}

	public static bool TryGetEnumDisplayName(object enumObj, out string displayName)
	{
		Type type = enumObj.GetType();
		if (!type.IsEnum)
		{
			throw new ArgumentException(type.FullName + " is not an enumeration type.");
		}
		string name = enumObj.ToString();
		MemberInfo memberInfo = type.GetMember(name).FirstOrDefault();
		if (memberInfo == null)
		{
			displayName = "";
			return false;
		}
		if (!(memberInfo.GetCustomAttribute(typeof(EnumDisplayName)) is EnumDisplayName enumDisplayName))
		{
			displayName = "";
			return false;
		}
		displayName = enumDisplayName.DisplayName;
		return true;
	}

	public static void InvokeStatic<TArg>(string targetTypeName, string targetMethodName, TArg arg, bool allowNonPublic = true)
	{
		if (string.IsNullOrWhiteSpace(targetTypeName))
		{
			throw new ArgumentException("Required.", "targetTypeName");
		}
		if (string.IsNullOrWhiteSpace(targetMethodName))
		{
			throw new ArgumentException("Required.", "targetMethodName");
		}
		if (arg == null)
		{
			throw new ArgumentNullException("arg");
		}
		BindingFlags bindingAttr = (BindingFlags)(0x18 | (allowNonPublic ? 32 : 0));
		Type type = AppDomain.CurrentDomain.GetAssemblies().SelectMany(SafeGetTypes).FirstOrDefault((Type t) => string.Equals(t.Name, targetTypeName, StringComparison.Ordinal));
		if (type == null)
		{
			throw new InvalidOperationException("Could not find a loaded type named '" + targetTypeName + "'.");
		}
		MethodInfo methodInfo = type.GetMethod(targetMethodName, bindingAttr, null, new Type[1] { arg.GetType() }, null);
		if ((object)methodInfo == null)
		{
			methodInfo = (from m in type.GetMethods(bindingAttr)
				where string.Equals(m.Name, targetMethodName, StringComparison.Ordinal)
				select new
				{
					Method = m,
					Params = m.GetParameters()
				} into x
				where x.Params.Length == 1
				where x.Params[0].ParameterType.IsAssignableFrom(arg.GetType())
				select x.Method).FirstOrDefault();
		}
		if (methodInfo == null)
		{
			throw new InvalidOperationException($"Type '{type.FullName}' does not define a static method '{targetMethodName}' with a single parameter compatible with '{arg.GetType().FullName}'.");
		}
		try
		{
			methodInfo.Invoke(null, new object[1] { arg });
		}
		catch (TargetInvocationException ex) when (ex.InnerException != null)
		{
			throw new InvalidOperationException($"Invocation of {type.FullName}.{targetMethodName} failed.", ex.InnerException);
		}
	}

	public static void InvokeStaticByFullTypeName<TArg>(string targetFullTypeName, string targetMethodName, TArg arg, bool allowNonPublic = true)
	{
		if (string.IsNullOrWhiteSpace(targetFullTypeName))
		{
			throw new ArgumentException("Required.", "targetFullTypeName");
		}
		if (string.IsNullOrWhiteSpace(targetMethodName))
		{
			throw new ArgumentException("Required.", "targetMethodName");
		}
		if (arg == null)
		{
			throw new ArgumentNullException("arg");
		}
		BindingFlags flags = (BindingFlags)(0x18 | (allowNonPublic ? 32 : 0));
		Type? type = AppDomain.CurrentDomain.GetAssemblies().SelectMany(SafeGetTypes).FirstOrDefault((Type t) => string.Equals(t.FullName, targetFullTypeName, StringComparison.Ordinal));
		if (type == null)
		{
			throw new InvalidOperationException("Could not find a loaded type named '" + targetFullTypeName + "'.");
		}
		InvokeOnType(type, targetMethodName, arg, flags);
	}

	private static void InvokeOnType<TArg>(Type targetType, string targetMethodName, TArg tArg, BindingFlags flags)
	{
		MethodInfo methodInfo = targetType.GetMethod(targetMethodName, flags, null, new Type[1] { tArg.GetType() }, null);
		if ((object)methodInfo == null)
		{
			methodInfo = (from m in targetType.GetMethods(flags)
				where string.Equals(m.Name, targetMethodName, StringComparison.Ordinal)
				select new
				{
					Method = m,
					Params = m.GetParameters()
				} into x
				where x.Params.Length == 1
				where x.Params[0].ParameterType.IsAssignableFrom(tArg.GetType())
				select x.Method).FirstOrDefault();
		}
		if (methodInfo == null)
		{
			throw new InvalidOperationException($"Type '{targetType.FullName}' does not define a static method '{targetMethodName}' with a single parameter compatible with '{tArg.GetType().FullName}'.");
		}
		try
		{
			methodInfo.Invoke(null, new object[1] { tArg });
		}
		catch (TargetInvocationException ex) when (ex.InnerException != null)
		{
			throw new InvalidOperationException($"Invocation of {targetType.FullName}.{targetMethodName} failed.", ex.InnerException);
		}
	}

	public static object InvokeStatic(string targetTypeName, string targetMethodName, object[] args = null, bool allowNonPublic = true)
	{
		if (string.IsNullOrWhiteSpace(targetTypeName))
		{
			throw new ArgumentException("Required.", "targetTypeName");
		}
		if (string.IsNullOrWhiteSpace(targetMethodName))
		{
			throw new ArgumentException("Required.", "targetMethodName");
		}
		if (args == null)
		{
			args = Array.Empty<object>();
		}
		BindingFlags bindingAttr = (BindingFlags)(0x18 | (allowNonPublic ? 32 : 0));
		Type type = AppDomain.CurrentDomain.GetAssemblies().SelectMany(SafeGetTypes).FirstOrDefault((Type t) => string.Equals(t.Name, targetTypeName, StringComparison.Ordinal));
		if (type == null)
		{
			throw new InvalidOperationException("Could not find a loaded type named '" + targetTypeName + "'.");
		}
		Type[] argTypes = args.Select((object a) => a?.GetType()).ToArray();
		MethodInfo methodInfo = (from m in type.GetMethods(bindingAttr)
			where string.Equals(m.Name, targetMethodName, StringComparison.Ordinal)
			select new
			{
				Method = m,
				Params = m.GetParameters()
			} into x
			where x.Params.Length == args.Length
			where ParametersMatchArguments(x.Params, argTypes)
			select x.Method).FirstOrDefault();
		if (methodInfo == null)
		{
			string argTypesFullNameTerm = string.Join(", ", argTypes.Select((Type t) => t?.FullName ?? "null"));
			throw new InvalidOperationException($"Type '{type.FullName}' does not define a static method '{targetMethodName}({argTypesFullNameTerm})' with compatible parameter types.");
		}
		try
		{
			return methodInfo.Invoke(null, args);
		}
		catch (TargetInvocationException ex) when (ex.InnerException != null)
		{
			throw new InvalidOperationException($"Invocation of {type.FullName}.{targetMethodName} failed.", ex.InnerException);
		}
	}

	private static bool ParametersMatchArguments(ParameterInfo[] parameters, Type[] argTypes)
	{
		for (int i = 0; i < parameters.Length; i++)
		{
			Type parameterType = parameters[i].ParameterType;
			Type type = argTypes[i];
			if (type == null)
			{
				if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == null)
				{
					return false;
				}
			}
			else if (!parameterType.IsAssignableFrom(type))
			{
				return false;
			}
		}
		return true;
	}

	private static Type[] SafeGetTypes(Assembly assembly)
	{
		try
		{
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException ex)
		{
			return ex.Types.Where((Type t) => t != null).ToArray();
		}
	}
}
