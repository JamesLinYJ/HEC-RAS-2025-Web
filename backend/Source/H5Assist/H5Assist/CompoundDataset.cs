using System;
using System.Collections.Generic;
using H5Assist.Identifiers;

namespace H5Assist;

public class CompoundDataset
{
	private Dictionary<CompoundFieldDefinition, Array> _dictionary;

	public CompoundDataset(Dictionary<CompoundFieldDefinition, Array> dic)
	{
		_dictionary = dic;
	}

	public T[] GetColumn<T>(string name)
	{
		foreach (KeyValuePair<CompoundFieldDefinition, Array> localValue in _dictionary)
		{
			CompoundFieldDefinition key = localValue.Key;
			if (key.Name == name && key.Type == typeof(T))
			{
				return localValue.Value as T[];
			}
		}
		return null;
	}
}
