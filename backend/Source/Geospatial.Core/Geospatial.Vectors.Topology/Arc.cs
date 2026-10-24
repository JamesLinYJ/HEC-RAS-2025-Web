using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Utility.Systems;

namespace Geospatial.Vectors.Topology;

[DebuggerDisplay("{DebugDisplay()}")]
public class Arc : BaseArc, ISpatialVersion, IStreamableObject, IEquatable<Arc>
{
	public int RegionA;

	public int RegionB;

	public Arc()
	{
	}

	public Arc(int startNode, int endNode, List<Point> internalPoints)
		: base(startNode, endNode, internalPoints)
	{
		UnsetRegions();
	}

	public void UnsetRegions()
	{
		RegionA = -1;
		RegionB = -1;
	}

	public override void StreamOut(BinaryWriter bw)
	{
		base.StreamOut(bw);
		bw.Write(RegionA);
		bw.Write(RegionB);
	}

	public override void StreamIn(BinaryReader br)
	{
		base.StreamIn(br);
		RegionA = br.ReadInt32();
		RegionB = br.ReadInt32();
	}

	public bool Equals(Arc otherArc)
	{
		if (StartNode != otherArc.StartNode)
		{
			return false;
		}
		if (EndNode != otherArc.EndNode)
		{
			return false;
		}
		if (RegionA != otherArc.RegionA)
		{
			return false;
		}
		if (RegionB != otherArc.RegionB)
		{
			return false;
		}
		if (InternalPointCount != otherArc.InternalPointCount)
		{
			return false;
		}
		if (InternalPointCount != 0 && !InternalPoints.EqualsXY(otherArc.InternalPoints))
		{
			return false;
		}
		return true;
	}

	public Arc Clone()
	{
		List<Point> internalPoints = ((InternalPoints == null) ? null : new List<Point>(InternalPoints));
		return new Arc(StartNode, EndNode, internalPoints)
		{
			RegionA = RegionA,
			RegionB = RegionB,
			ExternallyTracked_DateOfGenesis = ExternallyTracked_DateOfGenesis
		};
	}
}
