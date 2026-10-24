using System.Runtime.InteropServices;

namespace H5Assist.Identifiers;

[StructLayout(LayoutKind.Explicit)]
public struct VLenType
{
	[FieldOffset(0)]
	public nint Length;

	[FieldOffset(8)]
	public nint Pointer;
}
