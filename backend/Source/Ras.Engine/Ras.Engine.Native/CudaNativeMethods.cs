using System.Runtime.InteropServices;

namespace Ras.Engine.Native;

public static class CudaNativeMethods
{
	public const string CudaDLL = "Ras.Engine.CUDA.dll";

	[DllImport("Ras.Engine.CUDA.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "set_device")]
	public static extern CudaResult SetDevice(int device);

	[DllImport("Ras.Engine.CUDA.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "get_device_props")]
	public static extern CudaResult GetDeviceProperties(int device, ref CudaDeviceProperties prop);

	[DllImport("Ras.Engine.CUDA.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "gpu_allocate")]
	public static extern CudaResult GpuAllocate(int sizeInBytes, ref nint ptr);

	[DllImport("Ras.Engine.CUDA.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "gpu_free")]
	public static extern CudaResult GpuFree(nint ptr);

	[DllImport("Ras.Engine.CUDA.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "gpu_copyToDevice")]
	public static extern CudaResult CopyToDevice(nint hostPtr, nint devicePtr, int sizeInBytes);

	[DllImport("Ras.Engine.CUDA.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "gpu_copyToHost")]
	public static extern CudaResult CopyToHost(nint hostPtr, nint devicePtr, int sizeInBytes);

	[DllImport("Ras.Engine.CUDA.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "gpu_vectorMultiplyFP32")]
	public static extern CudaResult GpuVectorMultiply(nint deviceAPtr, nint deviceBPtr, nint deviceOutPtr, int elementCount);
}
