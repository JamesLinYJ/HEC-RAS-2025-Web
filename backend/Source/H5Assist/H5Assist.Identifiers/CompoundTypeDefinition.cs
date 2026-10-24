using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace H5Assist.Identifiers;

public class CompoundTypeDefinition
{
	public List<CompoundFieldDefinition> Fields;

	private static readonly Dictionary<Type, int> _typeSizes = new Dictionary<Type, int>();

	public CompoundTypeDefinition()
	{
		Fields = new List<CompoundFieldDefinition>();
	}

	public int SumSize()
	{
		return Fields.Sum((CompoundFieldDefinition f) => f.Size);
	}

	public void SortFields()
	{
		Fields = Fields.OrderBy((CompoundFieldDefinition f) => f.Offset).ToList();
	}

	public int ColumnIndex(string fieldName)
	{
		for (int i = 0; i < Fields.Count; i++)
		{
			if (Fields[i].Name == fieldName)
			{
				return i;
			}
		}
		return -1;
	}

	public void AddField<T>(string name) where T : struct
	{
		if (typeof(T) == typeof(bool))
		{
			AddBooleanField(name);
			return;
		}
		if (typeof(T) == typeof(string))
		{
			throw new Exception("Use AddStringField instead");
		}
		AddFieldInternal(typeof(T), name, Marshal.SizeOf<T>(default));
	}

	public static int GetTypeSize(Type t)
	{
		lock (_typeSizes)
		{
			if (_typeSizes.TryGetValue(t, out var typeSizes))
			{
				return typeSizes;
			}
			typeSizes = ((!t.IsEnum) ? Marshal.SizeOf(t) : Marshal.SizeOf(t.GetEnumUnderlyingType()));
			_typeSizes[t] = typeSizes;
			return typeSizes;
		}
	}

	public void AddField(Type t, string name)
	{
		if (t.IsPrimitive)
		{
			if (t == typeof(bool))
			{
				AddBooleanField(name);
			}
			else
			{
				AddFieldInternal(t, name, GetTypeSize(t));
			}
			return;
		}
		if (t == typeof(string))
		{
			throw new Exception("Use AddStringField instead.");
		}
		throw new NotSupportedException("Given type must be a primitive. Actual: " + t.FullName);
	}

	public void AddField(CompoundFieldDefinition cfd)
	{
		AddFieldInternal(cfd.Type, cfd.Name, cfd.Size);
	}

	private void AddBooleanField(string name)
	{
		AddFieldInternal(typeof(bool), name, 1);
	}

	public void AddStringField(string name, int maxStrLen)
	{
		AddFieldInternal(typeof(string), name, Math.Max(1, maxStrLen));
	}

	private void AddFieldInternal(Type t, string name, int size)
	{
		int offset = 0;
		CompoundFieldDefinition compoundFieldDefinition = Fields.LastOrDefault();
		if (compoundFieldDefinition != null)
		{
			offset = compoundFieldDefinition.Offset + compoundFieldDefinition.Size;
		}
		Fields.Add(new CompoundFieldDefinition(t, name, size, offset));
	}
}
