using System;
using Utility.Extensions;

namespace Utility;

[Serializable]
public class BufferSizeException : Exception
{
	public BufferSizeException(string varName, int expectedCount)
		: base(MakeMessage(varName, expectedCount))
	{
	}

	private static string MakeMessage(string varName, int expectedCount)
	{
		return "Buffer " + varName.InSingleQuotes() + " was incorrectly sized. Expected " + expectedCount + " elements.";
	}
}
