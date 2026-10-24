using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Utility.Systems;

namespace Geospatial.Vectors.Topology;

[DebuggerDisplay("{DebugDisplay(),nq}")]
public struct NodeArcConnection : IStreamableObject
{
	public int ArcID;

	public bool IsStart;

	public NodeArcConnection()
	{
		ArcID = 0;
		IsStart = false;
	}

	public NodeArcConnection(int arcId, bool isStart)
	{
		ArcID = arcId;
		IsStart = isStart;
	}

	public bool Equals(NodeArcConnection otherNac)
	{
		if (ArcID != otherNac.ArcID)
		{
			return false;
		}
		if (IsStart != otherNac.IsStart)
		{
			return false;
		}
		return true;
	}

	public void StreamIn(BinaryReader br)
	{
		ArcID = br.ReadInt32();
		IsStart = br.ReadBoolean();
	}

	public void StreamOut(BinaryWriter bw)
	{
		bw.Write(ArcID);
		bw.Write(IsStart);
	}

	public string DebugDisplay()
	{
		return $"Arc: {ArcID}, IsStart: {IsStart}";
	}

	public override bool Equals([NotNullWhen(true)] object obj)
	{
		if (!(obj is NodeArcConnection nodeArcConnection))
		{
			return false;
		}
		if (nodeArcConnection.IsStart == IsStart)
		{
			return nodeArcConnection.ArcID == ArcID;
		}
		return false;
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(IsStart, ArcID);
	}
}
