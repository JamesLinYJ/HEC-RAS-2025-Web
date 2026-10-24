using System;
using System.Collections.Generic;

namespace Ras.Engine;

public class TransportBoundaryConditionCollection
{
	private readonly List<TransportBoundaryCondition> _bcs = new List<TransportBoundaryCondition>();

	public IReadOnlyList<TransportBoundaryCondition> Items => _bcs;

	public void Add(TransportBoundaryCondition bc)
	{
		_bcs.Add(bc);
	}

	public void Clear()
	{
		_bcs.Clear();
	}

	public TransportBoundaryCondition[] BuildFaceLookup(int faceCount)
	{
		int[] faceSlot;
		return BuildFaceLookup(faceCount, out faceSlot);
	}

	public TransportBoundaryCondition[] BuildFaceLookup(int faceCount, out int[] faceSlot)
	{
		TransportBoundaryCondition[] transportBoundaryConditions = new TransportBoundaryCondition[faceCount];
		faceSlot = new int[faceCount];
		FillFaceLookup(transportBoundaryConditions, faceSlot);
		return transportBoundaryConditions;
	}

	internal void FillFaceLookup(TransportBoundaryCondition[] lookup, int[] faceSlot)
	{
		if (lookup == null)
		{
			throw new ArgumentNullException("lookup");
		}
		if (faceSlot == null)
		{
			throw new ArgumentNullException("faceSlot");
		}
		if (lookup.Length != faceSlot.Length)
		{
			throw new ArgumentException("Boundary lookup buffers must have matching lengths.");
		}
		Array.Clear(lookup);
		Array.Fill(faceSlot, -1);
		foreach (TransportBoundaryCondition bc in _bcs)
		{
			if (bc.Faces == null)
			{
				continue;
			}
			for (int i = 0; i < bc.Faces.Length; i++)
			{
				int faceIndex = bc.Faces[i];
				if (faceIndex >= 0 && faceIndex < lookup.Length)
				{
					lookup[faceIndex] = bc;
					faceSlot[faceIndex] = i;
				}
			}
		}
	}
}
