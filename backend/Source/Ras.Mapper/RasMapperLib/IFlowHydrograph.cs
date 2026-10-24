namespace RasMapperLib;

public interface IFlowHydrograph
{
	float[,] ReadSegmentedFlowHydrograph(int fid, string outputBlock = "");

	float[] ReadAggregatedFlowHydrograph(int fid, string outputBlock = "");

	float GetOutputTimestepInSeconds(string outputBlock = "");
}
