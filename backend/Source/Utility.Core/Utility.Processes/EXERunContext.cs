using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Utility.Processes;

public class EXERunContext
{
	private readonly StringBuilder _captured = new StringBuilder();

	public ManualResetEventSlim ProcessCompletedEvent;

	public StreamReader Stream;

	public Action<char> ReportCharacter;

	public string CapturedText => _captured.ToString();

	public EXERunContext(StreamReader sr, Action<char> reportCharacter)
	{
		Stream = sr;
		ReportCharacter = reportCharacter;
		ProcessCompletedEvent = new ManualResetEventSlim();
	}

	public void Accept(char c)
	{
		_captured.Append(c);
		ReportCharacter?.Invoke(c);
	}

	public void MarkCompleted()
	{
		ProcessCompletedEvent.Set();
	}
}
