using System;
using System.Diagnostics;
using System.IO;
using Utility.Systems;

namespace Geospatial.Vectors.Topology;

[DebuggerDisplay("{DebugDisplay(),nq}")]
public struct ArcWithDirection : IStreamableObject, IEquatable<ArcWithDirection>
{
	public int ArcID;

	public bool IsForward;

	public ArcWithDirection()
	{
		ArcID = 0;
		IsForward = false;
	}

	public ArcWithDirection(int arcId, bool isForward)
	{
		ArcID = arcId;
		IsForward = isForward;
	}

	public bool Equals(ArcWithDirection otherNac)
	{
		if (ArcID != otherNac.ArcID)
		{
			return false;
		}
		if (IsForward != otherNac.IsForward)
		{
			return false;
		}
		return true;
	}

	public override bool Equals(object obj)
	{
		if (obj is ArcWithDirection otherNac)
		{
			return Equals(otherNac);
		}
		return false;
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(ArcID, IsForward);
	}

	public void StreamIn(BinaryReader br)
	{
		ArcID = br.ReadInt32();
		IsForward = br.ReadBoolean();
	}

	public void StreamOut(BinaryWriter bw)
	{
		bw.Write(ArcID);
		bw.Write(IsForward);
	}

	public string DebugDisplay()
	{
		return $"Arc: {ArcID}, IsForward: {IsForward}";
	}
}
