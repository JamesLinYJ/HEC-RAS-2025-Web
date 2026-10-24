namespace Ras.Migration.Native;

public class JobControlInformation
{
	public float StandardStepTol;

	public float CriticalTol;

	public int NumStdStepTrials;

	public float MaxErrorTol;

	public float FlowTolRatio;

	public int NumSplitFlowTrials;

	public float SplitFlowTol;

	public float SplitFlowRatio;

	public bool AlwaysCompCrit;
}
