using System;

namespace Utility.Progress;

public static class ConsoleHelpers
{
	private static bool CanJumpUpConsole = true;

	private static bool CanClearConsoleLine = true;

	public static void JumpUpOneLine()
	{
		try
		{
			if (CanJumpUpConsole)
			{
				int top = Console.GetCursorPosition().Top;
				if (top > 0)
				{
					Console.SetCursorPosition(0, top - 1);
				}
				else
				{
					OperatingSystem.IsLinux();
				}
			}
		}
		catch
		{
			CanJumpUpConsole = false;
			Console.WriteLine("Environment does not allow jumping up console lines.");
		}
	}

	public static void ClearCurrentConsoleLine()
	{
		try
		{
			if (CanClearConsoleLine)
			{
				int cursorTop = Console.CursorTop;
				Console.SetCursorPosition(0, Console.CursorTop);
				Console.Write(new string(' ', Console.BufferWidth));
				Console.SetCursorPosition(0, cursorTop);
			}
		}
		catch
		{
			CanClearConsoleLine = false;
			Console.WriteLine("Environment does not allow clearing the console line, messages will be appended from now on.");
		}
	}
}
