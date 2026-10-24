using System;
using System.Diagnostics;

namespace Utility;

[DebuggerDisplay("{Event}")]
public class DebugStackFrame
{
	public StackTrace Trace;

	public DateTime Time;

	public string Event;

	public DebugStackFrame(StackTrace trace, DateTime dt, string ev)
	{
		Trace = trace;
		Time = dt;
		Event = ev;
	}
}
