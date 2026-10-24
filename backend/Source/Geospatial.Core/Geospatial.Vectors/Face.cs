using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Utility.Hashing;

namespace Geospatial.Vectors;

[DebuggerDisplay("(Cell: {cellA}->{cellB}, FacePoint: {fpA}->{fpB})")]
public struct Face : IDataHash
{
	private const int NullIndex = int.MinValue;

	public static readonly Face NullFace = new Face(int.MinValue, int.MinValue);

	public int fpA;

	public int fpB;

	public int cellA;

	public int cellB;

	public Face()
	{
		fpA = -1;
		fpB = -1;
		cellA = -1;
		cellB = -1;
	}

	public Face(int cellA, int cellB)
	{
		this.cellA = cellA;
		this.cellB = cellB;
		fpA = -1;
		fpB = -1;
	}

	public Face Clone()
	{
		Face result = new Face();
		result.cellA = cellA;
		result.cellB = cellB;
		result.fpA = fpA;
		result.fpB = fpB;
		return result;
	}

	public int GetFPNext(int cellIdx)
	{
		if (cellA == cellIdx)
		{
			return fpB;
		}
		if (cellB == cellIdx)
		{
			return fpA;
		}
		throw new ArgumentException("Err: Cell Index '" + cellIdx + "' is not a member of this face.");
	}

	public int GetFPPrev(int cellIdx)
	{
		if (cellA == cellIdx)
		{
			return fpA;
		}
		if (cellB == cellIdx)
		{
			return fpB;
		}
		throw new ArgumentException("Err: Cell Index '" + cellIdx + "' is not a member of this face.");
	}

	public void SetFPPrev(int cellIdx, int setTo)
	{
		if (cellA == cellIdx)
		{
			fpA = setTo;
			return;
		}
		if (cellB == cellIdx)
		{
			fpB = setTo;
			return;
		}
		throw new ArgumentException("Err: Cell Index '" + cellIdx + "' is not a member of this face.");
	}

	public void SetFPNext(int cellIdx, int setTo)
	{
		if (cellA == cellIdx)
		{
			fpB = setTo;
			return;
		}
		if (cellB == cellIdx)
		{
			fpA = setTo;
			return;
		}
		throw new ArgumentException("Err: Cell Index '" + cellIdx + "' is not a member of this face.");
	}

	public int OtherCell(int cellIdx)
	{
		if (cellA == cellIdx)
		{
			return cellB;
		}
		if (cellB == cellIdx)
		{
			return cellA;
		}
		throw new ArgumentException("Err: Cell Index '" + cellIdx + "' is not a member of this face.");
	}

	public bool IsEitherCell(int cellIdx)
	{
		if (cellA == cellIdx)
		{
			return true;
		}
		if (cellB == cellIdx)
		{
			return true;
		}
		return false;
	}

	public int OtherFacepoint(int fpIdx)
	{
		if (fpA == fpIdx)
		{
			return fpB;
		}
		if (fpB == fpIdx)
		{
			return fpA;
		}
		throw new ArgumentException("Err: Cell Index '" + fpIdx + "' is not a member of this face.");
	}

	public int CommonCell(Face otherFace)
	{
		if (cellA == otherFace.cellA || cellA == otherFace.cellB)
		{
			return cellA;
		}
		if (cellB == otherFace.cellB || cellB == otherFace.cellA)
		{
			return cellB;
		}
		return -1;
	}

	public bool IsNull()
	{
		if (cellA == int.MinValue)
		{
			return cellB == int.MinValue;
		}
		return false;
	}

	public static void Stream(BinaryWriter bw, IList<Face> fps)
	{
		bw.Write(fps.Count);
		for (int i = 0; i <= fps.Count - 1; i++)
		{
			Face face = fps[i];
			bw.Write(face.cellA);
			bw.Write(face.cellB);
			bw.Write(face.fpA);
			bw.Write(face.fpB);
		}
	}

	public static Face[] Stream(BinaryReader br)
	{
		int readInt32 = br.ReadInt32();
		Face[] faces = new Face[readInt32];
		if (readInt32 == 0)
		{
			return faces;
		}
		for (int i = 0; i < readInt32; i++)
		{
			Face face = new Face();
			face.cellA = br.ReadInt32();
			face.cellB = br.ReadInt32();
			face.fpA = br.ReadInt32();
			face.fpB = br.ReadInt32();
			faces[i] = face;
		}
		return faces;
	}

	public int GetDataHash()
	{
		return StableHash.Combine(cellA, cellB, fpA, fpB);
	}
}
