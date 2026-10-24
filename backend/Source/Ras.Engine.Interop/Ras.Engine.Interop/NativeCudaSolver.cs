using System.Runtime.InteropServices;

namespace Ras.Engine.Interop;

public static class NativeCudaSolver
{
	public const string DLLName = "Ras.Engine.CUDA.dll";

	[DllImport("Ras.Engine.CUDA.dll", CallingConvention = CallingConvention.Cdecl)]
	internal unsafe static extern void* CreateSolver(int numCells, int numFaces);

	[DllImport("Ras.Engine.CUDA.dll", CallingConvention = CallingConvention.Cdecl)]
	internal unsafe static extern void DestroySolver(void* solver);

	[DllImport("Ras.Engine.CUDA.dll", CallingConvention = CallingConvention.Cdecl)]
	internal unsafe static extern void SetControl(void* solver, double gravity, double lToSI, double dzMinAdvection, double nSIToUnits, double fCoriolis, double checkMaxElevationChange, double timestepSafetyFactor, double minAdvDt, double maxRatioWetAdvDt, bool turbulenceEnabled, bool advectionEnabled, bool dtLimitDiffusion);

	[DllImport("Ras.Engine.CUDA.dll", CallingConvention = CallingConvention.Cdecl)]
	internal unsafe static extern void SetTurbulence(void* solver, double baseEddyViscosity, double longMixCoeff, double transMixCoeff, double horizMixCoeff);

	[DllImport("Ras.Engine.CUDA.dll", CallingConvention = CallingConvention.Cdecl)]
	internal unsafe static extern void FinishBufferWrites(void* solver);

	[DllImport("Ras.Engine.CUDA.dll", CallingConvention = CallingConvention.Cdecl)]
	internal unsafe static extern void DoPrecalculations(void* solver);

	[DllImport("Ras.Engine.CUDA.dll", CallingConvention = CallingConvention.Cdecl)]
	internal unsafe static extern void WriteToBuffer(void* solver, BufferCollectionTarget target, string bufferName, void* data, int size, ConversionBase conversionBase);

	[DllImport("Ras.Engine.CUDA.dll", CallingConvention = CallingConvention.Cdecl)]
	internal unsafe static extern void CopyBuffer(void* solver, void* dst, BufferCollectionTarget target, string bufferName, ConversionBase conversionBase);

	[DllImport("Ras.Engine.CUDA.dll", CallingConvention = CallingConvention.Cdecl)]
	internal unsafe static extern void ApplyBoundaryConditions(void* solver, double absTime);

	[DllImport("Ras.Engine.CUDA.dll", CallingConvention = CallingConvention.Cdecl)]
	internal unsafe static extern void SolveForwardBack(void* solver, long absTimeMicro, long dtMicro);

	[DllImport("Ras.Engine.CUDA.dll", CallingConvention = CallingConvention.Cdecl)]
	internal unsafe static extern void AddBoundaryCondition(void* solver, NativeBoundaryConditionType type, int* faces, double* faceFractions, int numFaces, double* timesteps, double* values, int numTimesteps, double egSlope);

	[DllImport("Ras.Engine.CUDA.dll", CallingConvention = CallingConvention.Cdecl)]
	internal unsafe static extern double ComputeMaxTimestep(void* solver);

	[DllImport("Ras.Engine.CUDA.dll", CallingConvention = CallingConvention.Cdecl)]
	internal unsafe static extern long ComputeInitTimestep(void* solver, long initDt);

	[DllImport("Ras.Engine.CUDA.dll", CallingConvention = CallingConvention.Cdecl)]
	internal static extern CUDACompatibilityResult GetCompatiblity();

	[DllImport("Ras.Engine.CUDA.dll", CallingConvention = CallingConvention.Cdecl)]
	internal unsafe static extern void InitStepIntegratedBC(StepIntegratedBCType type, void* solver, double* timesteps, int numTimesteps, void* cachedScalarForTimestep);

	[DllImport("Ras.Engine.CUDA.dll", CallingConvention = CallingConvention.Cdecl)]
	internal unsafe static extern void UpdateStepIntegratedBoundaryConditions(void* solver, double absTime, double dt);
}
