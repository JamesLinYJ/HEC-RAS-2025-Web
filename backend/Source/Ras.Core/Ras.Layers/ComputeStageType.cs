namespace Ras.Layers;

public enum ComputeStageType
{
	PerturbationPreprocessor,
	GeometryPreprocessor,
	SteadyFlowHydraulics,
	QuasiUnsteadyFlowHydraulics,
	UnsteadyFlowHydraulics,
	PostProcessor,
	Sediment,
	WaterQuality
}
