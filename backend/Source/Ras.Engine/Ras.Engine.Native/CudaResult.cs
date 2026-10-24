namespace Ras.Engine.Native;

public enum CudaResult
{
	Success = 0,
	InvalidValue = 1,
	MemoryAllocation = 2,
	InitializationError = 3,
	ErrorUnloadingCudart = 4,
	ProfilerDisabled = 5,
	InvalidConfiguration = 9,
	InvalidPitchValue = 12,
	InvalidSymbol = 13,
	InvalidTexture = 18,
	InvalidTextureBinding = 19,
	InvalidChannelDescriptor = 20,
	InvalidMemcpyDirection = 21,
	InvalidFilterSetting = 26,
	InvalidNormSetting = 27,
	LaunchPendingCountExceeded = 69,
	InvalidDeviceFunction = 98,
	NoDevice = 100,
	InvalidDevice = 101,
	DeviceNotLicensed = 102,
	SoftwareValidityNotEstablished = 103,
	StartupFailure = 127,
	InvalidKernelImage = 200,
	DeviceUninitialized = 201
}
