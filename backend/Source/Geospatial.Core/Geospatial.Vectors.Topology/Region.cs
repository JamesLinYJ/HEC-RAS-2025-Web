using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Utility.Systems;

namespace Geospatial.Vectors.Topology;

[DebuggerDisplay("{DebuggerDisplayString(),nq}")]
public class Region : IEquatable<Region>, IStreamableObject
{
	private List<int> _controlPointIndices = new List<int>();

	public RegionRing OuterRing;

	public List<RegionRing> InnerRings = new List<RegionRing>();

	public List<int> ContainedArcs;

	public int ControlPointIndex
	{
		get
		{
			if (ControlPointIndices.Count != 0)
			{
				return ControlPointIndices[0];
			}
			return -1;
		}
	}

	public IReadOnlyList<int> ControlPointIndices => _controlPointIndices;

	public int InnerRingCount => InnerRings?.Count ?? 0;

	public Region()
	{
	}

	public Region(int ctrlPtIdx)
	{
		_controlPointIndices.Add(ctrlPtIdx);
	}

	public bool HasEqualInnerAndOuterRings(Region other)
	{
		int innerRingCount = ((InnerRings != null) ? InnerRings.Count : 0);
		int innerRingCount2 = ((other.InnerRings != null) ? other.InnerRings.Count : 0);
		if (innerRingCount != innerRingCount2)
		{
			return false;
		}
		if (!OuterRing.Equals(other.OuterRing))
		{
			return false;
		}
		if (InnerRings != null)
		{
			foreach (RegionRing innerRing in InnerRings)
			{
				if (!other.InnerRings.Contains(innerRing))
				{
					return false;
				}
			}
		}
		return true;
	}

	public void AddInnerRing(RegionRing ring)
	{
		if (InnerRings == null)
		{
			InnerRings = new List<RegionRing>();
		}
		InnerRings.Add(ring);
	}

	public bool Equals(Region other)
	{
		if (!HasEqualInnerAndOuterRings(other))
		{
			return false;
		}
		if (ControlPointIndices.Count != other.ControlPointIndices.Count)
		{
			return false;
		}
		for (int i = 0; i < ControlPointIndices.Count; i++)
		{
			if (ControlPointIndices[i] != other.ControlPointIndices[i])
			{
				return false;
			}
		}
		return true;
	}

	public string DebuggerDisplayString()
	{
		return "Ctrl Pts: " + string.Join(',', (object)ControlPointIndex) + ", OuterRing: " + OuterRing.DebuggerDisplayString();
	}

	public void StreamOut(BinaryWriter bw)
	{
		bw.Write(ControlPointIndices.Count);
		for (int i = 0; i < ControlPointIndices.Count; i++)
		{
			bw.Write(ControlPointIndices[i]);
		}
		OuterRing.StreamOut(bw);
		bw.StreamCollectionOut(InnerRings);
	}

	public void StreamIn(BinaryReader br)
	{
		int readInt32 = br.ReadInt32();
		_controlPointIndices = new List<int>();
		for (int i = 0; i < readInt32; i++)
		{
			TryAddControlPoint(br.ReadInt32());
		}
		OuterRing = new RegionRing();
		OuterRing.StreamIn(br);
		RegionRing[] streamCollectionIn = br.StreamCollectionIn<RegionRing>();
		if (streamCollectionIn != null && streamCollectionIn.Length != 0)
		{
			InnerRings = new List<RegionRing>(streamCollectionIn);
		}
		else
		{
			InnerRings = new List<RegionRing>();
		}
	}

	public Region Clone()
	{
		Region region = new Region();
		region._controlPointIndices = new List<int>(ControlPointIndices);
		region.OuterRing = OuterRing.Clone();
		if (InnerRings != null)
		{
			region.InnerRings = new List<RegionRing>();
			foreach (RegionRing innerRing in InnerRings)
			{
				region.InnerRings.Add(innerRing.Clone());
			}
		}
		if (ContainedArcs != null)
		{
			region.ContainedArcs = new List<int>(ContainedArcs);
		}
		return region;
	}

	public bool TryAddControlPoint(int ctrlPtFid)
	{
		if (_controlPointIndices.Contains(ctrlPtFid))
		{
			return false;
		}
		_controlPointIndices.Add(ctrlPtFid);
		return true;
	}

	public void RemoveControlPoint(int fid)
	{
		_controlPointIndices.Remove(fid);
	}

	public void ClearControlPoints()
	{
		_controlPointIndices.Clear();
	}

	public void RemoveControlPointAt(int idx)
	{
		_controlPointIndices.RemoveAt(idx);
	}

	public void IncrementOrDecrementControlPointValue(int fid, bool increment)
	{
		if (increment)
		{
			_controlPointIndices[fid]++;
		}
		else
		{
			_controlPointIndices[fid]--;
		}
	}
}
