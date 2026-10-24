using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RasMapperLib.Progress;

public class ConsoleDisplayProgress : IDisplayProgress
{
	public void Run(IAmComputable computable)
	{
		throw new NotImplementedException();
	}

	void IDisplayProgress.Run(IAmComputable computable)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Run
		this.Run(computable);
	}

	public void PercentProgress(int prog)
	{
	}

	void IDisplayProgress.PercentProgress(int prog)
	{
		//ILSpy generated this explicit interface implementation from .override directive in PercentProgress
		this.PercentProgress(prog);
	}

	public void GDALProgressReporter(char c)
	{
	}

	void IDisplayProgress.GDALProgressReporter(char c)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GDALProgressReporter
		this.GDALProgressReporter(c);
	}

	public void CleanFilesPrintStatus(List<string> files)
	{
	}

	void IDisplayProgress.CleanFilesPrintStatus(List<string> files)
	{
		//ILSpy generated this explicit interface implementation from .override directive in CleanFilesPrintStatus
		this.CleanFilesPrintStatus(files);
	}

	public void SetHeaderText(string stringValue)
	{
	}

	void IDisplayProgress.SetHeaderText(string textValue)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetHeaderText
		this.SetHeaderText(textValue);
	}

	public void AddBarrier()
	{
	}

	void IDisplayProgress.AddBarrier()
	{
		//ILSpy generated this explicit interface implementation from .override directive in AddBarrier
		this.AddBarrier();
	}

	public void AddTimeFormattedLine(string desc, string timeMsg, Color? color = null)
	{
	}

	void IDisplayProgress.AddTimeFormattedLine(string desc,string timeMsg,Color? color )
	{
		//ILSpy generated this explicit interface implementation from .override directive in AddTimeFormattedLine
		this.AddTimeFormattedLine(desc, timeMsg, color);
	}

	public void StepStart(Stopwatch sw, string stringValue)
	{
	}

	void IDisplayProgress.StepStart(Stopwatch sw,string textValue)
	{
		//ILSpy generated this explicit interface implementation from .override directive in StepStart
		this.StepStart(sw, textValue);
	}

	public void StepStop(Stopwatch sw)
	{
	}

	void IDisplayProgress.StepStop(Stopwatch sw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in StepStop
		this.StepStop(sw);
	}

	public void TimeIt(string label, Action action)
	{
		action();
	}

	void IDisplayProgress.TimeIt(string label,Action action)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TimeIt
		this.TimeIt(label, action);
	}

	public void SetActiveProcess(Process proc)
	{
	}

	void IDisplayProgress.SetActiveProcess(Process proc)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetActiveProcess
		this.SetActiveProcess(proc);
	}

	public void AddLine(string line, Color? color = null)
	{
		throw new NotImplementedException();
	}

	void IDisplayProgress.AddLine(string line,Color? color )
	{
		//ILSpy generated this explicit interface implementation from .override directive in AddLine
		this.AddLine(line, color);
	}

	public void AddChar(char c, Color? color = null)
	{
		throw new NotImplementedException();
	}

	void IDisplayProgress.AddChar(char c,Color? color )
	{
		//ILSpy generated this explicit interface implementation from .override directive in AddChar
		this.AddChar(c, color);
	}
}
