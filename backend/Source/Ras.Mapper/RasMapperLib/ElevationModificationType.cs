using Utility.Extensions;

namespace RasMapperLib;

public enum ElevationModificationType
{
	[DiskName("SetValue")]
	SetValue,
	[DiskName("SetIfHigher")]
	TakeHigher,
	[DiskName("SetIfLower")]
	TakeLower,
	[DiskName("Add")]
	AddValue
}
