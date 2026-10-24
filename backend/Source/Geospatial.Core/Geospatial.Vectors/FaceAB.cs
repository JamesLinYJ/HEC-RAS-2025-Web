using System.Runtime.InteropServices;

namespace Geospatial.Vectors;

[StructLayout(LayoutKind.Explicit)]
public struct FaceAB(double a, double b)
{
	[FieldOffset(0)]
	public double ValueA = a;

	[FieldOffset(8)]
	public double ValueB = b;

	[FieldOffset(0)]
	public unsafe fixed double LR[2];
}
