using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using Utility.Extensions;
using Utility.Progress;

namespace Ras.Migration.Native;

public class Parser
{
	private const FileState BAD = FileState.Malformed;

	private const FileState GOOD = FileState.Good;

	private const StringSplitOptions RM = StringSplitOptions.RemoveEmptyEntries;

	public string[] Lines;

	public int LineIndex;

	private int _ct;

	public ProgressReporter _pr { get; set; }

	public bool EOF => LineIndex >= _ct;

	public string CurrentLine
	{
		get
		{
			if (!EOF)
			{
				return Lines[LineIndex];
			}
			return "";
		}
	}

	public string PriorLine => Lines[LineIndex - 1];

	public Parser(string[] lines, ProgressReporter p)
	{
		Lines = lines;
		_ct = lines.Length;
		LineIndex = 0;
		_pr = p;
	}

	public void UndoLine()
	{
		LineIndex--;
	}

	public bool GetNextLine(out string l, bool skipBlankLines = true)
	{
		if (LineIndex >= _ct)
		{
			l = "";
			return false;
		}
		if (skipBlankLines)
		{
			while (LineIndex < _ct && Lines[LineIndex] == "")
			{
				LineIndex++;
			}
			if (LineIndex >= _ct)
			{
				l = "";
				return false;
			}
		}
		l = Lines[LineIndex];
		LineIndex++;
		return true;
	}

	public bool AssertGetNextLine(out string l, bool skipBlankLines = true, string msg = "Unexpected EOF")
	{
		if (!GetNextLine(out l, skipBlankLines))
		{
			_pr.ReportError(msg);
			return false;
		}
		return true;
	}

	public void SkipLines(Func<string, bool> skipIf)
	{
		string l;
		while (GetNextLine(out l))
		{
			if (!skipIf(l))
			{
				UndoLine();
				break;
			}
		}
	}

	public bool AssertKey(string key, out string stringValue)
	{
		if (!AssertGetNextLine(out var l))
		{
			stringValue = "";
			return false;
		}
		if (MatchesKey(l, key, out stringValue))
		{
			_pr.ReportMessage(key + ": " + stringValue, LogLevel.Debug);
			return true;
		}
		stringValue = "";
		_pr.ReportError("Expected Key " + key.InDoubleQuotes() + " not found in " + l.InDoubleQuotes());
		return false;
	}

	public bool AssertKey(string key, out float naN)
	{
		if (!AssertKey(key, out string textValue))
		{
			naN = float.NaN;
			return false;
		}
		if (string.IsNullOrEmpty(textValue))
		{
			naN = float.NaN;
			return true;
		}
		if (float.TryParse(textValue, out naN))
		{
			return true;
		}
		_pr.ReportError("Could not parse " + textValue.InDoubleQuotes() + " as float.");
		naN = float.NaN;
		return false;
	}

	public bool AssertKey(string key, out int intValue)
	{
		if (!AssertKey(key, out string textValue))
		{
			intValue = 0;
			return false;
		}
		if (string.IsNullOrEmpty(textValue))
		{
			intValue = 0;
			return true;
		}
		if (int.TryParse(textValue, out intValue))
		{
			return true;
		}
		_pr.ReportError("Could not parse " + textValue.InDoubleQuotes() + " as int.");
		intValue = 0;
		return false;
	}

	public bool AssertKey(string key, out bool parseAsBool)
	{
		if (!AssertKey(key, out string textValue))
		{
			parseAsBool = false;
			return false;
		}
		parseAsBool = ParseAsBool(textValue);
		return true;
	}

	public bool AssertKey(string key, Action<string> assign)
	{
		if (AssertKey(key, out string textValue))
		{
			assign(textValue);
			return true;
		}
		return false;
	}

	public bool AssertKey(string key, Action<float> assign)
	{
		if (AssertKey(key, out float computed))
		{
			assign(computed);
			return true;
		}
		return false;
	}

	public bool AssertKey(string key, Action<int> assign)
	{
		if (AssertKey(key, out int ordinal))
		{
			assign(ordinal);
			return true;
		}
		return false;
	}

	public bool AssertKey(string key, Action<bool> assign)
	{
		if (AssertKey(key, out bool condition))
		{
			assign(condition);
			return true;
		}
		return false;
	}

	public bool AssertKey(string key, string altKey, out string stringValue)
	{
		if (!AssertGetNextLine(out var l))
		{
			stringValue = "";
			return false;
		}
		if (MatchesKey(l, key, out stringValue))
		{
			_pr.ReportMessage(key + ": " + stringValue);
			return true;
		}
		if (MatchesKey(l, altKey, out stringValue))
		{
			_pr.ReportMessage(key + ": " + stringValue);
			return true;
		}
		stringValue = "";
		_pr.ReportError("Expected Key " + key.InDoubleQuotes() + " or alt-key " + altKey.InDoubleQuotes() + " not found in " + l.InDoubleQuotes());
		return false;
	}

	public bool AssertKey(string key, string altKey, Action<string> assign)
	{
		if (AssertKey(key, altKey, out var textValue))
		{
			assign(textValue);
			return true;
		}
		return false;
	}

	public bool CheckKey(string key, out string stringValue)
	{
		if (!GetNextLine(out var l))
		{
			stringValue = "";
			return false;
		}
		if (MatchesKey(l, key, out stringValue))
		{
			_pr.ReportMessage(key + ": " + stringValue);
			return true;
		}
		stringValue = "";
		UndoLine();
		return false;
	}

	public bool CheckKey(string key, Action<string> invokeValue)
	{
		if (CheckKey(key, out string textValue))
		{
			invokeValue(textValue);
			return true;
		}
		return false;
	}

	public bool CheckKey(string key, Action<float> invokeValue)
	{
		if (CheckKey(key, out float computed))
		{
			invokeValue(computed);
			return true;
		}
		return false;
	}

	public bool CheckKey(string key, Action<int> invokeValue)
	{
		if (CheckKey(key, out int ordinal))
		{
			invokeValue(ordinal);
			return true;
		}
		return false;
	}

	public bool CheckKey(string key, Action<bool> invokeValue)
	{
		if (CheckKey(key, out bool condition))
		{
			invokeValue(condition);
			return true;
		}
		return false;
	}

	public bool CheckKey(string key, out bool parseAsBool)
	{
		if (!CheckKey(key, out string textValue))
		{
			parseAsBool = false;
			return false;
		}
		parseAsBool = ParseAsBool(textValue);
		return true;
	}

	public bool CheckKey(string key, out float naN)
	{
		if (!CheckKey(key, out string textValue))
		{
			naN = float.NaN;
			return false;
		}
		if (string.IsNullOrEmpty(textValue))
		{
			naN = float.NaN;
			return true;
		}
		if (float.TryParse(textValue, out naN))
		{
			return true;
		}
		_pr.ReportError("Could not parse " + textValue.InDoubleQuotes() + " as float.");
		naN = float.NaN;
		return false;
	}

	public bool CheckKey(string key, out int intValue)
	{
		if (!CheckKey(key, out string textValue))
		{
			intValue = 0;
			return false;
		}
		if (string.IsNullOrEmpty(textValue))
		{
			intValue = 0;
			return true;
		}
		if (int.TryParse(textValue, out intValue))
		{
			return true;
		}
		_pr.ReportError("Could not parse " + textValue.InDoubleQuotes() + " as integer.");
		intValue = 0;
		return false;
	}

	public bool CheckTag(string key)
	{
		if (!GetNextLine(out var l))
		{
			return false;
		}
		if (l.Equals(key, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		UndoLine();
		return false;
	}

	public bool CheckCompoundKey(string key1, out string stringValue, string key2, out string stringValue2)
	{
		if (!GetNextLine(out var l))
		{
			stringValue = "";
			stringValue2 = "";
			return false;
		}
		int indexOf = l.IndexOf('|');
		if (indexOf == -1)
		{
			UndoLine();
			stringValue = "";
			stringValue2 = "";
			return false;
		}
		string line = l.Substring(0, indexOf);
		string line2 = l.Substring(indexOf + 1);
		if (MatchesKey(line, key1, out stringValue) && MatchesKey(line2, key2, out stringValue2))
		{
			return true;
		}
		UndoLine();
		stringValue = "";
		stringValue2 = "";
		return false;
	}

	public bool CheckCompoundKey(string key1, out string stringValue, out string key2, out string stringValue2)
	{
		if (!GetNextLine(out var l))
		{
			stringValue = "";
			stringValue2 = "";
			key2 = "";
			return false;
		}
		int indexOf = l.IndexOf('|');
		if (indexOf == -1)
		{
			UndoLine();
			stringValue = "";
			stringValue2 = "";
			key2 = "";
			return false;
		}
		string line = l.Substring(0, indexOf);
		if (!SplitKVP(l.Substring(indexOf + 1), out key2, out stringValue2))
		{
			UndoLine();
			stringValue = "";
			stringValue2 = "";
			key2 = "";
			return false;
		}
		if (MatchesKey(line, key1, out stringValue))
		{
			return true;
		}
		UndoLine();
		stringValue = "";
		stringValue2 = "";
		key2 = "";
		return false;
	}

	public bool CheckCompoundKey(string key1, out string stringValue, string key2, out float naN)
	{
		if (!CheckCompoundKey(key1, out stringValue, key2, out string textValue))
		{
			naN = float.NaN;
			return false;
		}
		if (!float.TryParse(textValue, out naN))
		{
			naN = float.NaN;
			return false;
		}
		return true;
	}

	public bool GetKVP(out string key, out string stringValue)
	{
		if (!GetNextLine(out var l))
		{
			key = "";
			stringValue = "";
			return false;
		}
		return SplitKVP(l, out key, out stringValue);
	}

	public static bool SplitKVP(string line, out string key, out string stringValue)
	{
		int indexOf = line.IndexOf('=');
		if (indexOf == -1)
		{
			key = "";
			stringValue = "";
			return false;
		}
		key = line.Substring(0, indexOf).Trim();
		if (indexOf == line.Length - 1)
		{
			stringValue = "";
		}
		else
		{
			stringValue = line.Substring(indexOf + 1).Trim();
		}
		return true;
	}

	public string Last(int nLines)
	{
		StringBuilder stringBuilder = new StringBuilder();
		int lineIndexAboveNLines = Math.Max(0, LineIndex - nLines);
		for (int i = 0; i < nLines; i++)
		{
			int ordinal = i + lineIndexAboveNLines;
			stringBuilder.AppendLine(ordinal.ToString().InSquareBrackets() + ": " + Lines[ordinal]);
		}
		return stringBuilder.ToString();
	}

	public string Nearby(int nLinesPrior = 10, int nLinesNext = 10)
	{
		StringBuilder stringBuilder = new StringBuilder();
		int lineIndex = LineIndex;
		int lineIndexAboveNLinesPrior = Math.Max(0, lineIndex - nLinesPrior);
		int count = Math.Min(_ct - 1, lineIndex + nLinesNext);
		for (int i = lineIndexAboveNLinesPrior; i <= count; i++)
		{
			string inSquareBrackets = ((i != lineIndex) ? i.ToString().InSquareBrackets() : new string(Enumerable.Repeat('>', lineIndex.ToString().Length + 2).ToArray()));
			stringBuilder.AppendLine(inSquareBrackets + ": " + Lines[i]);
		}
		return stringBuilder.ToString();
	}

	public string TryPeekKey()
	{
		if (EOF)
		{
			return "";
		}
		if (SplitKVP(Lines[LineIndex], out var key, out var _))
		{
			return key;
		}
		return "";
	}

	public bool OptionalGuardedMultiline(string startGuard, string endGuard, out string stringValue)
	{
		if (!GetNextLine(out var l))
		{
			stringValue = "";
			return true;
		}
		if (l.Trim() != startGuard)
		{
			UndoLine();
			stringValue = "";
			return true;
		}
		bool condition = false;
		StringBuilder stringBuilder = new StringBuilder();
		while (GetNextLine(out l))
		{
			if (l.Trim() == endGuard)
			{
				condition = true;
				break;
			}
			stringBuilder.Append(l);
		}
		stringValue = stringBuilder.ToString();
		if (!condition)
		{
			_pr.ReportError("Did not find expected exit-guard clause: " + endGuard);
			return false;
		}
		return true;
	}

	public bool ReadFloatLines(int expectedValueCt, out float[] data)
	{
		int count = (int)Math.Ceiling((float)expectedValueCt / 10f);
		List<float> floatList = new List<float>(expectedValueCt);
		data = null;
		for (int i = 0; i < count; i++)
		{
			if (!GetNextLine(out var l))
			{
				_pr.ReportError("Unexpected EOF");
				return false;
			}
			if (!ReadFloatLine(l, _pr, out var values))
			{
				return false;
			}
			floatList.AddRange(values);
		}
		if (floatList.Count != expectedValueCt)
		{
			_pr.ReportError("Unexpected hydrograph value length.");
			return false;
		}
		data = floatList.ToArray();
		return true;
	}

	public bool ReadDoubleLines(int expectedValueCt, out double[] data)
	{
		int count = (int)Math.Ceiling((float)expectedValueCt / 4f);
		List<double> doubleList = new List<double>(expectedValueCt);
		data = null;
		for (int i = 0; i < count; i++)
		{
			if (!GetNextLine(out var l))
			{
				_pr.ReportError("Unexpected EOF");
				return false;
			}
			if (!ReadDoubleLine(l, _pr, out var values))
			{
				return false;
			}
			doubleList.AddRange(values);
		}
		if (doubleList.Count != expectedValueCt)
		{
			_pr.ReportError("Unexpected hydrograph value length.");
			return false;
		}
		data = doubleList.ToArray();
		return true;
	}

	public bool ReadFloatLinesXY(int expectedValueCt, out float[] xvals, out float[] yvals)
	{
		int count = (int)Math.Ceiling((float)(expectedValueCt * 2) / 10f);
		List<float> floatList = new List<float>(expectedValueCt);
		List<float> floatListList = new List<float>(expectedValueCt);
		xvals = null;
		yvals = null;
		for (int i = 0; i < count; i++)
		{
			if (!GetNextLine(out var l))
			{
				_pr.ReportError("Unexpected EOF");
				return false;
			}
			if (!ReadFloatLine(l, _pr, out var values))
			{
				return false;
			}
			if (values.Length % 2 != 0)
			{
				throw new Exception();
			}
			for (int j = 0; j < values.Length / 2; j++)
			{
				floatList.Add(values[j * 2]);
				floatListList.Add(values[j * 2 + 1]);
			}
		}
		if (floatList.Count != expectedValueCt)
		{
			_pr.ReportError("Unexpected hydrograph value length.");
			return false;
		}
		xvals = floatList.ToArray();
		yvals = floatListList.ToArray();
		return true;
	}

	public bool ReadDoubleLinesXY(int expectedValueCt, out double[] xvals, out double[] yvals)
	{
		int count = (int)Math.Ceiling((float)(expectedValueCt * 2) / 4f);
		List<double> doubleList = new List<double>(expectedValueCt);
		List<double> doubleListList = new List<double>(expectedValueCt);
		xvals = null;
		yvals = null;
		for (int i = 0; i < count; i++)
		{
			if (!GetNextLine(out var l))
			{
				_pr.ReportError("Unexpected EOF");
				return false;
			}
			if (!ReadDoubleLine(l, _pr, out var values))
			{
				return false;
			}
			if (values.Length % 2 != 0)
			{
				throw new Exception();
			}
			for (int j = 0; j < values.Length / 2; j++)
			{
				doubleList.Add(values[j * 2]);
				doubleListList.Add(values[j * 2 + 1]);
			}
		}
		if (doubleList.Count != expectedValueCt)
		{
			_pr.ReportError("Unexpected hydrograph value length.");
			return false;
		}
		xvals = doubleList.ToArray();
		yvals = doubleListList.ToArray();
		return true;
	}

	private static bool ReadFloatLine(string line, ProgressReporter pr, out float[] values)
	{
		if (line.Length > 80)
		{
			pr.ReportError("Expected float line exceeded 80 chars");
			values = null;
			return false;
		}
		if (line.Length % 8 != 0)
		{
			pr.ReportError("Expected float line should be a multiple of 8 chars.");
			values = null;
			return false;
		}
		int count = line.Length / 8;
		values = new float[count];
		for (int i = 0; i < count; i++)
		{
			string substring = line.Substring(i * 8, 8);
			if (string.IsNullOrWhiteSpace(substring))
			{
				values[i] = float.NaN;
				continue;
			}
			if (float.TryParse(substring, out var result))
			{
				values[i] = result;
				continue;
			}
			pr.ReportError("Could not parse " + substring.InDoubleQuotes() + " as a float value.");
			values = null;
			return false;
		}
		return true;
	}

	private static bool ReadDoubleLine(string line, ProgressReporter pr, out double[] values)
	{
		if (line.Length > 64)
		{
			pr.ReportError("Expected dbl line exceeded 64 chars");
			values = null;
			return false;
		}
		if (line.Length % 16 != 0)
		{
			pr.ReportError("Expected double line should be a multiple of 16 chars.");
			values = null;
			return false;
		}
		int count = line.Length / 16;
		values = new double[count];
		for (int i = 0; i < count; i++)
		{
			string substring = line.Substring(i * 16, 16);
			if (double.TryParse(substring, out var result))
			{
				values[i] = result;
				continue;
			}
			pr.ReportError("Could not parse " + substring + " as a double value.");
			values = null;
			return false;
		}
		return true;
	}

	public static bool MatchesKey(string line, string key, out string stringValue)
	{
		int indexOf = line.IndexOf('=');
		if (indexOf == -1)
		{
			stringValue = "";
			return false;
		}
		if (line.Substring(0, indexOf) == key)
		{
			stringValue = line.Substring(indexOf + 1).Trim();
			return true;
		}
		stringValue = "";
		return false;
	}

	public static bool ParseAsBool(string stringValue)
	{
		string trim = stringValue.Trim();
		if (trim == "0")
		{
			return false;
		}
		if (int.TryParse(trim, out var _))
		{
			return true;
		}
		if (bool.TryParse(trim, out var result2))
		{
			return result2;
		}
		throw new Exception("Could not parse " + trim.InDoubleQuotes() + " as boolean.");
	}

	public static bool ParseCSV(string stringValue, out double[] values, bool skipEmpties = true)
	{
		string[] parts = ((!skipEmpties) ? stringValue.Split(',') : stringValue.Split(',', StringSplitOptions.RemoveEmptyEntries));
		values = new double[parts.Length];
		for (int i = 0; i < values.Length; i++)
		{
			string part = parts[i];
			if (string.IsNullOrEmpty(part))
			{
				values[i] = double.NaN;
				continue;
			}
			if (double.TryParse(part, out var result))
			{
				values[i] = result;
				continue;
			}
			return false;
		}
		return true;
	}

	public static bool ParseCSV(string csv, out int[] parsed)
	{
		string[] parts = csv.Split(',');
		parsed = new int[parts.Length];
		for (int i = 0; i < parts.Length; i++)
		{
			if (!int.TryParse(parts[i], out var result))
			{
				return false;
			}
			parsed[i] = result;
		}
		return true;
	}
}
