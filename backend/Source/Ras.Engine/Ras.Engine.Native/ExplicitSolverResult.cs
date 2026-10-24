using System;

namespace Ras.Engine.Native;

public class ExplicitSolverResult
{
	public Status Status { get; set; }

	public double MinTimestep { get; set; }

	public double MaxTimestep { get; set; }

	public double AverageTimestep { get; set; }

	public long TimestepCount { get; set; }

	public int TimestepRetries { get; set; }

	public string GetStatusString()
	{
		string newLine = Environment.NewLine;
		return $"Status {Status}{newLine}MinTimestep {MinTimestep}{newLine}MaxTimestep {MaxTimestep}{newLine}AverageTimestep {AverageTimestep}{newLine}TimestepCount {TimestepCount}{newLine}TimestepRetries {TimestepRetries}";
	}
}
