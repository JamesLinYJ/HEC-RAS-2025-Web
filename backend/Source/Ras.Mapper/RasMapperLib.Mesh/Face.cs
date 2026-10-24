using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace RasMapperLib.Mesh;

[DebuggerDisplay("(Cell: {cellA}->{cellB}, FacePoint: {fpA}->{fpB})")]
[ComVisible(false)]
public class Face
{
	public int fpB;

	public int fpA;

	public PointMs InternalPoints;

	public int cellA;

	public int cellB;

	public float MinimumElevation;

	public static Face NullFace()
	{
		return new Face(int.MinValue, int.MinValue);
	}

	public Face()
	{
		fpA = -1;
		fpB = -1;
		InternalPoints = null;
		cellA = -1;
		cellB = -1;
	}

	public Face(int cellA, int cellB)
	{
		this.cellA = cellA;
		this.cellB = cellB;
		InternalPoints = null;
		fpA = -1;
		fpB = -1;
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
		throw new ArgumentException("Err: myIdx '" + cellIdx + "' is not a member of this face.");
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
		throw new ArgumentException("Err: myIdx '" + cellIdx + "' is not a member of this face.");
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
		throw new ArgumentException("Err: myIdx '" + cellIdx + "' is not a member of this face.");
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
		throw new ArgumentException("Err: myIdx '" + cellIdx + "' is not a member of this face.");
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
		throw new ArgumentException("Err: myIdx '" + cellIdx + "' is not a member of this face.");
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
		throw new ArgumentException("Err: myIdx '" + fpIdx + "' is not a member of this face.");
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
		Face face = NullFace();
		if (cellA == face.cellA && cellB == face.cellB)
		{
			return true;
		}
		return false;
	}

	public static void Stream(BinaryWriter bw, IList<Face> fps)
	{
		bw.Write(fps.Count);
		checked
		{
			int count = fps.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Face face = fps[i];
				bw.Write(face.cellA);
				bw.Write(face.cellB);
				bw.Write(face.fpA);
				bw.Write(face.fpB);
				bw.Write(face.MinimumElevation);
				PointM.Stream(bw, face.InternalPoints);
			}
		}
	}

	public static Face[] Stream(BinaryReader br)
	{
		int readInt32 = br.ReadInt32();
		checked
		{
			Face[] faces = new Face[readInt32 - 1 + 1];
			if (readInt32 == 0)
			{
				return faces;
			}
			int count = readInt32 - 1;
			for (int i = 0; i <= count; i++)
			{
				Face face = new Face();
				face.cellA = br.ReadInt32();
				face.cellB = br.ReadInt32();
				face.fpA = br.ReadInt32();
				face.fpB = br.ReadInt32();
				face.MinimumElevation = br.ReadSingle();
				face.InternalPoints = new PointMs(PointM.Stream(br));
				faces[i] = face;
			}
			return faces;
		}
	}
}
