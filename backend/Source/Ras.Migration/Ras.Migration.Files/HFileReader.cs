using System.Collections.Generic;
using System.IO;

namespace Ras.Migration.Files;

public class HFileReader : ISimpleFileReader
{
	private string _fileName;

	public HFileReader(string fileName)
	{
		_fileName = fileName;
	}

	public string ReadValue(string path)
	{
		using (StreamReader streamReader = new StreamReader(_fileName))
		{
			while (!streamReader.EndOfStream)
			{
				string line = streamReader.ReadLine();
				if (line.StartsWith(path))
				{
					return line.Replace(path, "");
				}
			}
		}
		return null;
	}

	public List<string> ReadList(string path)
	{
		List<string> stringList = new List<string>();
		using StreamReader streamReader = new StreamReader(_fileName);
		while (!streamReader.EndOfStream)
		{
			string line = streamReader.ReadLine();
			if (!line.StartsWith(path))
			{
				continue;
			}
			string s = line.Replace(path, "");
			int result = 0;
			int.TryParse(s, out result);
			for (int i = 0; i < result; i++)
			{
				if (streamReader.EndOfStream)
				{
					break;
				}
				stringList.Add(streamReader.ReadLine());
			}
			return stringList;
		}
		return stringList;
	}

	public bool ContainsPath(string stringValue, bool requireEntireLineEquality = false)
	{
		using (StreamReader streamReader = new StreamReader(_fileName))
		{
			while (!streamReader.EndOfStream)
			{
				string line = streamReader.ReadLine();
				if (requireEntireLineEquality ? line.Equals(stringValue) : line.StartsWith(stringValue))
				{
					return true;
				}
			}
		}
		return false;
	}
}
