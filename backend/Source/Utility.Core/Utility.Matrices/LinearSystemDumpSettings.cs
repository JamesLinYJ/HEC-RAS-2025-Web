using System;

namespace Utility.Matrices;

public class LinearSystemDumpSettings
{
	public LinearSystemDumpMode Mode;

	public string Directory = string.Empty;

	public DateTime StartTime;

	public long OutputIntervalMicros;
}
