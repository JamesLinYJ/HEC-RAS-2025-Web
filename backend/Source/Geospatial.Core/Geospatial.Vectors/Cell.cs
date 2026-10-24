using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Utility.Hashing;
using Utility.Systems;

namespace Geospatial.Vectors;

[DebuggerDisplay("({Point.X}, {Point.Y})")]
public struct Cell : IDataHash
{
	public Memory<int> Faces = null;

	public bool IsInitialized => !Faces.IsEmpty;

	public Cell()
	{
	}

	public Cell Clone()
	{
		Cell result = new Cell();
		result.Faces = Faces.ToArray();
		return result;
	}

	public int NextCCWFaceByGlobalIndex(int face)
	{
		int indexOfFacesTerm = ((ReadOnlySpan<int>)Faces.Span).IndexOf(face) + 1;
		if (indexOfFacesTerm == Faces.Length)
		{
			indexOfFacesTerm = 0;
		}
		return Faces.Span[indexOfFacesTerm];
	}

	public int NextCCWFaceByLocalIndex(int face)
	{
		int faceOffset = face + 1;
		if (faceOffset == Faces.Length)
		{
			faceOffset = 0;
		}
		return Faces.Span[faceOffset];
	}

	public int NextCWFaceByGlobalIndex(int face)
	{
		int lengthOffset = ((ReadOnlySpan<int>)Faces.Span).IndexOf(face) - 1;
		if (lengthOffset == -1)
		{
			lengthOffset = Faces.Length - 1;
		}
		return Faces.Span[lengthOffset];
	}

	public int NextCWFaceByLocalIndex(int face)
	{
		int faceOffset = face - 1;
		if (faceOffset == -1)
		{
			faceOffset = Faces.Length - 1;
		}
		return Faces.Span[faceOffset];
	}

	public static void Stream(BinaryWriter bw, IList<Cell> cells)
	{
		bw.Write(cells.Count);
		for (int i = 0; i < cells.Count; i++)
		{
			Pipe.WriteArray(bw, cells[i].Faces);
		}
	}

	public static Cell[] Stream(BinaryReader br)
	{
		int readInt32 = br.ReadInt32();
		Cell[] cells = new Cell[readInt32];
		if (readInt32 == 0)
		{
			return cells;
		}
		for (int i = 0; i < readInt32; i++)
		{
			int[] faces = Pipe.ReadArray<int>(br);
			Cell cell = new Cell();
			cell.Faces = faces;
			cells[i] = cell;
		}
		return cells;
	}

	public int GetDataHash()
	{
		int combine = 0;
		Span<int> faces = Faces.Span;
		for (int i = 0; i < faces.Length; i++)
		{
			combine = StableHash.Combine(combine, faces[i]);
		}
		return combine;
	}
}
