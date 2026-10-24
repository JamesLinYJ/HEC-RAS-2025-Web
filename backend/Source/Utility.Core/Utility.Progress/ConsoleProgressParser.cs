using System.Text;
using Utility.Logging;

namespace Utility.Progress;

public class ConsoleProgressParser
{
	public StringBuilder StandardOutText = new StringBuilder();

	public StringBuilder StandardErrorText = new StringBuilder();

	private StringBuilder _stdOutLine = new StringBuilder();

	private StringBuilder _stdErrLine = new StringBuilder();

	public event MessageReportedEventHandler MessageReported;

	public event ProgressReportedEventHandler ProgressReported;

	public virtual void ReportStdOut(char c)
	{
		StandardOutText.Append(c);
		ScanForProgress(c);
		ReportLineIfComplete(c, _stdOutLine);
	}

	public virtual void ReportStdErr(char c)
	{
		StandardErrorText.Append(c);
		ReportLineIfComplete(c, _stdErrLine);
	}

	private void ScanForProgress(char mruCharacter)
	{
		if (mruCharacter != '%')
		{
			return;
		}
		int length = "Progress: 100%".Length;
		int length2 = "Progress: 0%".Length;
		if (StandardOutText.Length < length2)
		{
			return;
		}
		int lengthDifference = StandardOutText.Length - length;
		if (lengthDifference < 0)
		{
			lengthDifference = 0;
		}
		int ordinal = -1;
		for (int i = lengthDifference; i <= lengthDifference + 2; i++)
		{
			if (StandardOutText[i] == 'P')
			{
				ordinal = i;
				break;
			}
		}
		if (ordinal == -1)
		{
			return;
		}
		for (int j = ordinal; j < ordinal + "Progress: ".Length; j++)
		{
			if (StandardOutText[j] != "Progress: "[j - ordinal])
			{
				return;
			}
		}
		ordinal += "Progress: ".Length;
		if (int.TryParse(StandardOutText.ToString(ordinal, StandardOutText.Length - ordinal - 1), out var result))
		{
			ProgressReported?.Invoke(null, result);
		}
	}

	private void ReportLineIfComplete(char c, StringBuilder sb)
	{
		if (c == '\r' || c == '\n')
		{
			string sb2 = sb.ToString();
			if (sb2.Length > 0)
			{
				ReportMessage(sb2);
			}
			sb.Clear();
		}
		else
		{
			sb.Append(c);
		}
	}

	private void ReportMessage(string msg)
	{
		MessageReported?.Invoke(null, new Message(msg));
	}

	public void Reset()
	{
		StandardOutText.Clear();
		StandardErrorText.Clear();
		_stdOutLine.Clear();
		_stdErrLine.Clear();
	}
}
