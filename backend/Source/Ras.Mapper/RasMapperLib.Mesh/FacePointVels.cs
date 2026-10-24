using System.Diagnostics;
using System.Linq;

namespace RasMapperLib.Mesh;

[DebuggerDisplay("Count = {Velocity.Count}, ({Velocity(0).X}, {Velocity(0).Y})")]
public struct FacePointVels
{
	public Vector2D[] Velocity;

	public FacePointVels(Vector2D[] vel)
	{
		this = default;
		Velocity = vel;
	}

	public FacePointVels Copy()
	{
		checked
		{
			Vector2D[] vector2Ds = new Vector2D[Velocity.Count() - 1 + 1];
			int count = vector2Ds.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				vector2Ds[i] = Velocity[i];
			}
			return new FacePointVels(vector2Ds);
		}
	}
}
