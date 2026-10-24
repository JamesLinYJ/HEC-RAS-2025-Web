using System.Runtime.InteropServices;

namespace Ras.Engine.Native;

public struct CudaDeviceProperties
{
	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
	public char[] name;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
	public byte[] uuid;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
	public char[] luid;

	public uint luidDeviceNodeMask;

	public long totalGlobalMem;

	public long sharedMemPerBlock;

	public int regsPerBlock;

	public int warpSize;

	public long memPitch;

	public int maxThreadsPerBlock;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
	public int[] maxThreadsDim;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
	public int[] maxGridSize;

	public int clockRate;

	public long totalConstMem;

	public int major;

	public int minor;

	public long textureAlignment;

	public long texturePitchAlignment;

	public int deviceOverlap;

	public int multiProcessorCount;

	public int kernelExecTimeoutEnabled;

	public int integrated;

	public int canMapHostMemory;

	public int computeMode;

	public int maxTexture1D;

	public int maxTexture1DMipmap;

	public int maxTexture1DLinear;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
	public int[] maxTexture2D;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
	public int[] maxTexture2DMipmap;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
	public int[] maxTexture2DLinear;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
	public int[] maxTexture2DGather;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
	public int[] maxTexture3D;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
	public int[] maxTexture3DAlt;

	public int maxTextureCubemap;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
	public int[] maxTexture1DLayered;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
	public int[] maxTexture2DLayered;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
	public int[] maxTextureCubemapLayered;

	public int maxSurface1D;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
	public int[] maxSurface2D;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
	public int[] maxSurface3D;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
	public int[] maxSurface1DLayered;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
	public int[] maxSurface2DLayered;

	public int maxSurfaceCubemap;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
	public int[] maxSurfaceCubemapLayered;

	public long surfaceAlignment;

	public int concurrentKernels;

	public int ECCEnabled;

	public int pciBusID;

	public int pciDeviceID;

	public int pciDomainID;

	public int tccDriver;

	public int asyncEngineCount;

	public int unifiedAddressing;

	public int memoryClockRate;

	public int memoryBusWidth;

	public int l2CacheSize;

	public int persistingL2CacheMaxSize;

	public int maxThreadsPerMultiProcessor;

	public int streamPrioritiesSupported;

	public int globalL1CacheSupported;

	public int localL1CacheSupported;

	public long sharedMemPerMultiprocessor;

	public int regsPerMultiprocessor;

	public int managedMemory;

	public int isMultiGpuBoard;

	public int multiGpuBoardGroupID;

	public int hostNativeAtomicSupported;

	public int singleToDoublePrecisionPerfRatio;

	public int pageableMemoryAccess;

	public int concurrentManagedAccess;

	public int computePreemptionSupported;

	public int canUseHostPointerForRegisteredMem;

	public int cooperativeLaunch;

	public int cooperativeMultiDeviceLaunch;

	public long sharedMemPerBlockOptin;

	public int pageableMemoryAccessUsesHostPageTables;

	public int directManagedMemAccessFromHost;

	public int maxBlocksPerMultiProcessor;

	public int accessPolicyMaxWindowSize;

	public long reservedSharedMemPerBlock;
}
