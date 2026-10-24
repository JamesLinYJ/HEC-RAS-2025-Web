using System.Runtime.InteropServices;

namespace Utility.Logging;

[StructLayout(LayoutKind.Explicit, Size = 128)]
internal struct PaddedPosition
{
	[FieldOffset(64)]
	public int Value;
}
