using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RasMapperLib.Progress;

public interface IDisplayProgress
{
	void Run(IAmComputable computable);

	void AddLine(string line, Color? color = null);

	void AddChar(char c, Color? color = null);

	void PercentProgress(int prog);

	void GDALProgressReporter(char c);

	void CleanFilesPrintStatus(List<string> files);

	void SetHeaderText(string stringValue);

	void AddBarrier();

	void AddTimeFormattedLine(string desc, string timeMsg, Color? color = null);

	void StepStart(Stopwatch sw, string stringValue);

	void StepStop(Stopwatch sw);

	void TimeIt(string label, Action action);

	void SetActiveProcess(Process proc);
}
