using System;

namespace Utility.Logging;

public class ToastMessage
{
	public string Message { get; set; }

	public MessageSeverity Severity { get; set; }

	public Action DefaultAction { get; set; }

	public ToastMessage(string msg, MessageSeverity sev)
	{
		Message = msg;
		Severity = sev;
		DefaultAction = null;
	}
}
