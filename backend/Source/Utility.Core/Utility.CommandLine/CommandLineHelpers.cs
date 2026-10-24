using System.Linq;

namespace Utility.CommandLine;

public static class CommandLineHelpers
{
	public static string[] SplitIntoCLIArgs(string args)
	{
		int ordinal = 0;
		string[] strings = new string[args.Length];
		bool condition = false;
		int i = 0;
		while (true)
		{
			if (i != args.Length)
			{
				for (; i != args.Length && (args[i] == ' ' || args[i] == '\t'); i++)
				{
				}
			}
			if (i == args.Length)
			{
				break;
			}
			string textValue = "";
			while (true)
			{
				bool condition2 = true;
				int ordinal2 = 0;
				while (i != args.Length && args[i] == '\\')
				{
					i++;
					ordinal2++;
				}
				if (i != args.Length && args[i] == '"')
				{
					if (ordinal2 % 2 == 0)
					{
						if (condition && args.Length != i + 1 && args[i + 1] == '"')
						{
							i++;
						}
						else
						{
							condition2 = false;
							condition = !condition;
						}
					}
					ordinal2 /= 2;
				}
				while (ordinal2 != 0)
				{
					ordinal2--;
					textValue += "\\";
				}
				if (i == args.Length || (!condition && (args[i] == ' ' || args[i] == '\t')))
				{
					break;
				}
				if (condition2)
				{
					textValue += args[i];
				}
				i++;
			}
			strings[ordinal] = textValue;
			ordinal++;
		}
		return strings.TakeWhile((string n) => n != null).ToArray();
	}
}
