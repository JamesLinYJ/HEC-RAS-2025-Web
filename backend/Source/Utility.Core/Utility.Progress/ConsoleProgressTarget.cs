using System;
using System.Text;
using Utility.Logging;

namespace Utility.Progress;

public class ConsoleProgressTarget : IProgressTarget
{
	private bool _lastMsgWasProgress;

	private readonly bool _progOverwritesLine;

	private const string TaskPrefix = "    ";

	public ConsoleProgressTarget(bool progressOverwritesLine, bool tabbedTasks)
	{
		_progOverwritesLine = progressOverwritesLine;
	}

	private static string Prefix(int taskLvl)
	{
		if (taskLvl != 0)
		{
			return new StringBuilder(taskLvl * "    ".Length).Insert(0, "    ", taskLvl).ToString();
		}
		return "";
	}

	public void WriteMessage(ProgressReporter sender, Message message)
	{
		try
		{
			string evaluatedMessage = message.EvaluatedMessage;
			if (message.MessageType.DefaultSeverity == MessageSeverity.Error)
			{
				Console.Error.WriteLine(Prefix(sender.TaskLevel) + evaluatedMessage);
			}
			else if (message.MessageType.DefaultSeverity == MessageSeverity.Warning)
			{
				Console.WriteLine(Prefix(sender.TaskLevel) + "WARNING: " + evaluatedMessage);
			}
			else
			{
				Console.WriteLine(Prefix(sender.TaskLevel) + evaluatedMessage);
			}
			_lastMsgWasProgress = false;
		}
		catch
		{
		}
	}

	public void WriteProgress(ProgressReporter sender, int localProgress, int globalProgress)
	{
		try
		{
			string globalProgressTerm = "Progress: " + globalProgress + "%";
			if (_progOverwritesLine)
			{
				if (_lastMsgWasProgress)
				{
					ConsoleHelpers.JumpUpOneLine();
					ConsoleHelpers.ClearCurrentConsoleLine();
					Console.WriteLine(Prefix(sender.TaskLevel) + globalProgressTerm);
				}
				else
				{
					Console.WriteLine(Prefix(sender.TaskLevel) + globalProgressTerm);
				}
			}
			else
			{
				Console.WriteLine(Prefix(sender.TaskLevel) + globalProgressTerm);
			}
			_lastMsgWasProgress = true;
		}
		catch
		{
		}
	}
}
