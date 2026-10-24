namespace Ras.Engine.Interop;

internal enum CudaCompatibilityErrorType
{
	NO_CUDA_DEVICE,
	NO_DRIVER,
	DRIVER_OUT_OF_DATE,
	COMPUTE_INCAPABLE,
	QUERY_ERROR
}
