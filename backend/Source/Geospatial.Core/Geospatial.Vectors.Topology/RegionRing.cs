using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Utility.Systems;

namespace Geospatial.Vectors.Topology;

[DebuggerDisplay("{DebuggerDisplayString(),nq}")]
public class RegionRing : IEquatable<RegionRing>, IStreamableObject
{
	public List<ArcWithDirection> Arcs;

	public RegionRing(int capacity = 0)
	{
		Arcs = new List<ArcWithDirection>(capacity);
	}

	internal void Add(ArcWithDirection connection)
	{
		Arcs.Add(connection);
	}

	public bool ContainsArc(int arcId)
	{
		for (int i = 0; i < Arcs.Count; i++)
		{
			if (Arcs[i].ArcID == arcId)
			{
				return true;
			}
		}
		return false;
	}

	public RegionRing Reverse()
	{
		RegionRing regionRing = new RegionRing(Arcs.Count);
		for (int i = 0; i < Arcs.Count; i++)
		{
			ArcWithDirection arcWithDirection = Arcs[Arcs.Count - i - 1];
			regionRing.Add(new ArcWithDirection(arcWithDirection.ArcID, !arcWithDirection.IsForward));
		}
		return regionRing;
	}

	public bool Equals(RegionRing other)
	{
		if (Arcs.Count != other.Arcs.Count)
		{
			return false;
		}
		if (Arcs.Count == 0 && other.Arcs.Count == 0)
		{
			return true;
		}
		int indexOf = other.Arcs.IndexOf(Arcs[0]);
		if (indexOf == -1)
		{
			return false;
		}
		for (int i = 0; i < Arcs.Count; i++)
		{
			if (!Arcs[i].Equals(other.Arcs[(indexOf + i) % Arcs.Count]))
			{
				return false;
			}
		}
		return true;
	}

	public RegionRing Clone()
	{
		RegionRing regionRing = new RegionRing(Arcs.Count);
		foreach (ArcWithDirection arc in Arcs)
		{
			regionRing.Add(new ArcWithDirection(arc.ArcID, arc.IsForward));
		}
		return regionRing;
	}

	public string DebuggerDisplayString()
	{
		if (Arcs.Count > 20)
		{
			return "20+ node/arc connections";
		}
		return "Arc: [" + string.Join(", ", Arcs.Select((ArcWithDirection arc) => arc.ArcID)) + "]";
	}

	public void StreamOut(BinaryWriter bw)
	{
		bw.StreamCollectionOut(Arcs);
	}

	public void StreamIn(BinaryReader br)
	{
		Arcs = new List<ArcWithDirection>(br.StreamCollectionIn<ArcWithDirection>());
	}
}
