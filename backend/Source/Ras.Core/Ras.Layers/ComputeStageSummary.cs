using System;

namespace Ras.Layers;

public class ComputeStageSummary
{
	public string EngineFriendlyName { get; set; }

	public string EngineFilename { get; set; }

	public string EngineFileTimestamp { get; set; }

	public string EngineFileHash { get; set; }

	public string EngineName { get; set; }

	public ComputeStageType StageType { get; set; }

	public ComputeStatus Status { get; set; }

	public string StatusMessage { get; set; }

	public DateTime StartTime { get; set; }

	public DateTime EndTime { get; set; }

	public TimeSpan ComputeDuration { get; set; }

	public string[] ReadComputeMessages()
	{
		throw new NotImplementedException();
	}
}
