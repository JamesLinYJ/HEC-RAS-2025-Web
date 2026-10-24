using System.Collections.Generic;

namespace RasMapperLib;

public class SingleString
{
	private Dictionary<string, string> _dictionary;

	public SingleString()
	{
		_dictionary = new Dictionary<string, string>();
	}

	public string GetFirstInstance(string s)
	{
		string textValue = s;
		if (_dictionary.TryGetValue(textValue, out s))
		{
			return s;
		}
		_dictionary.Add(textValue, textValue);
		return textValue;
	}
}
