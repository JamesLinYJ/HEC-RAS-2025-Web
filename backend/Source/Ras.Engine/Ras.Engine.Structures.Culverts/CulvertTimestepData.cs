using System.Diagnostics;
using System.Linq;

namespace Ras.Engine.Structures.Culverts;

[DebuggerDisplay("{DebuggerDisplay,nq}")]
public class CulvertTimestepData
{
	public float[] Flow;

	public float[] OutsideDepth;

	public float[] OutletVelocity;

	public int BarrelCount => Flow?.Length ?? 0;

	[DebuggerBrowsable(DebuggerBrowsableState.Never)]
	private string DebuggerDisplay
	{
		get
		{
			if (BarrelCount == 0)
			{
				return "Empty";
			}
			float dischargeSumTerm = Flow?.Sum() ?? 0f;
			float outsideDepthTerm = OutsideDepth?.Max() ?? 0f;
			if (BarrelCount != 1)
			{
				return $"{BarrelCount} barrels: TotalQ={dischargeSumTerm:F2}, MaxDepth={outsideDepthTerm:F2}";
			}
			return $"1 barrel: Q={Flow[0]:F2}, Depth US={OutsideDepth[0]:F2} DS={OutsideDepth[1]:F2}";
		}
	}

	public CulvertTimestepData Clone()
	{
		return new CulvertTimestepData
		{
			Flow = Flow?.ToArray(),
			OutsideDepth = OutsideDepth?.ToArray(),
			OutletVelocity = OutletVelocity?.ToArray()
		};
	}
}
