using System.Runtime.InteropServices;

namespace Ras.Engine.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct CUDACompatibilityResult
{
	public bool compatible;

	public CudaCompatibilityErrorType error;

	public int driverVersion;

	public int runtimeVersion;

	public int computeCapabilityMajor;

	public int computeCapabilityMinor;

	public int requiredComputeCapabilityMajor;

	public int requiredComputeCapabilityMinor;

	public int queryError;

	public unsafe fixed byte deviceName[256];
}
