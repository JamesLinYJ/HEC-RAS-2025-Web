using System;
using System.Diagnostics;

namespace Geospatial.Vectors;

[DebuggerDisplay("Count = {Velocity.Count}, ({Velocity(0).X}, {Velocity(0).Y})")]
public struct FacePointVels(Memory<Vector> vel)
{
	public Memory<Vector> Velocity = vel;
}
