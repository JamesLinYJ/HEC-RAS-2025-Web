using System.Collections.Generic;
using System.IO;
using Utility.Files;

namespace Ras.Migration.Files;

public class HFileWriter : ISimpleFileWriter
{
	private string _fileName { get; set; }

	public HFileWriter(string filename)
	{
		_fileName = filename;
	}

	public void WriteValue(string path, string stringValue)
	{
		string allLines2 = path + stringValue;
		int ordinal = -1;
		string[] allLines = (FileHelpers.CanReadFile(_fileName) ? File.ReadAllLines(_fileName) : new string[0]);
		for (int i = 0; i < allLines.Length; i++)
		{
			if (allLines[i].StartsWith(path))
			{
				ordinal = i;
				break;
			}
		}
		if (ordinal == -1)
		{
			string[] contents = new string[1] { allLines2 };
			File.AppendAllLines(_fileName, contents);
		}
		else
		{
			allLines[ordinal] = allLines2;
			File.WriteAllLines(_fileName, allLines);
		}
	}

	public void WriteList(string path, List<string> values)
	{
		int ordinal = -1;
		string[] allLines = (FileHelpers.CanReadFile(_fileName) ? File.ReadAllLines(_fileName) : new string[0]);
		for (int i = 0; i < allLines.Length; i++)
		{
			if (allLines[i].StartsWith(path))
			{
				ordinal = i;
				break;
			}
		}
		if (ordinal == -1)
		{
			List<string> stringList = new List<string> { path + values.Count };
			stringList.AddRange(values);
			File.AppendAllLines(_fileName, stringList);
			return;
		}
		List<string> allLinesList = new List<string>(allLines);
		int.TryParse(allLines[ordinal].Replace(path, ""), out var result);
		result++;
		do
		{
			allLinesList.RemoveAt(ordinal);
			result--;
		}
		while (result > 0);
		allLinesList.Insert(ordinal, path + values.Count);
		for (int j = 1; j <= values.Count; j++)
		{
			allLinesList.Insert(ordinal + j, values[j - 1]);
		}
		File.WriteAllLines(_fileName, allLinesList);
	}

	public void CreatePath(string stringValue)
	{
		File.AppendAllLines(_fileName, new string[1] { stringValue });
	}
}
