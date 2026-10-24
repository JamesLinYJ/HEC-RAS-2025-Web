using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class Reflector
{
	private string m_ns;

	private Assembly m_asmb;

	public Reflector(string ns)
		: this(ns, ns)
	{
	}

	public Reflector(string an__1, string ns)
	{
		m_ns = ns;
		m_asmb = null;
		AssemblyName[] referencedAssemblies = Assembly.GetExecutingAssembly().GetReferencedAssemblies();
		foreach (AssemblyName assemblyName in referencedAssemblies)
		{
			if (assemblyName.FullName.StartsWith(an__1))
			{
				m_asmb = Assembly.Load(assemblyName);
				break;
			}
		}
	}

	public Type GetType(string typeName)
	{
		Type type = null;
		string[] parts = typeName.Split('.');
		if (parts.Length > 0)
		{
			type = m_asmb.GetType(m_ns + Convert.ToString(".") + parts[0]);
		}
		checked
		{
			int count = parts.Length - 1;
			for (int i = 1; i <= count; i++)
			{
				type = type.GetNestedType(parts[i], BindingFlags.NonPublic);
			}
			return type;
		}
	}

	public object New(string name, params object[] parameters)
	{
		ConstructorInfo[] constructors = GetType(name).GetConstructors();
		foreach (ConstructorInfo constructorInfo in constructors)
		{
			try
			{
				return constructorInfo.Invoke(parameters);
			}
			catch (Exception projectError)
			{
				ProjectData.SetProjectError(projectError);
				ProjectData.ClearProjectError();
			}
		}
		return null;
	}

	public object Call(object localValue, string func, params object[] parameters)
	{
		return Call2(RuntimeHelpers.GetObjectValue(localValue), func, parameters);
	}

	public object Call2(object localValue, string func, object[] parameters)
	{
		return CallAs2(localValue.GetType(), RuntimeHelpers.GetObjectValue(localValue), func, parameters);
	}

	public object CallAs(Type type, object localValue, string func, params object[] parameters)
	{
		return CallAs2(type, RuntimeHelpers.GetObjectValue(localValue), func, parameters);
	}

	public object CallAs2(Type type, object localValue, string func, object[] parameters)
	{
		return type.GetMethod(func, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(RuntimeHelpers.GetObjectValue(localValue), parameters);
	}

	public object Get(object localValue, string prop)
	{
		return GetAs(localValue.GetType(), RuntimeHelpers.GetObjectValue(localValue), prop);
	}

	public object GetAs(Type type, object localValue, string prop)
	{
		return type.GetProperty(prop, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(RuntimeHelpers.GetObjectValue(localValue), null);
	}

	public object GetEnum(string typeName, string name)
	{
		return GetType(typeName).GetField(name).GetValue(null);
	}
}
