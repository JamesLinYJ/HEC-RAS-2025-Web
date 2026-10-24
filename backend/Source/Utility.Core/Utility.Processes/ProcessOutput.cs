using Microsoft.Extensions.Logging;

namespace Utility.Processes;

public readonly struct ProcessOutput(int exitCode, string standardOutput, string standardError, bool timedOut = false, string commandLine = "", string workingDirectory = "")
{
	public int ExitCode { get; } = exitCode;

	public string StandardOutput { get; } = standardOutput;

	public string StandardError { get; } = standardError;

	public string CommandLine { get; } = commandLine;

	public string WorkingDirectory { get; } = workingDirectory;

	public bool TimedOut { get; } = timedOut;

	public bool Succeeded
	{
		get
		{
			if (!TimedOut)
			{
				return ExitCode == 0;
			}
			return false;
		}
	}

	public string Detail
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(StandardError))
			{
				return StandardError.Trim();
			}
			if (!string.IsNullOrWhiteSpace(StandardOutput))
			{
				return StandardOutput.Trim();
			}
			return string.Empty;
		}
	}

	public string FailureSummary
	{
		get
		{
			if (TimedOut)
			{
				return "timed out";
			}
			if (ExitCode != 0)
			{
				return $"exit code {ExitCode}";
			}
			return string.Empty;
		}
	}

	public string DescribeFailure(string operation)
	{
		string detail = Detail;
		if (string.IsNullOrWhiteSpace(detail))
		{
			detail = "See the log for details.";
		}
		return $"{operation} failed ({FailureSummary}): {detail}";
	}

	public void LogFailure(ILogger logger, string operation)
	{
		logger.LogError("{Operation} failed - {Failure}. Command: {CommandLine} | working directory: {WorkingDirectory} | stdout: {StdOut} | stderr: {StdErr}", operation, FailureSummary, CommandLine, WorkingDirectory, StandardOutput, StandardError);
	}
}
