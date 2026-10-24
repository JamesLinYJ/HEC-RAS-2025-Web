using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Utility.Systems;

namespace RasMapperLib.Mesh;

[DebuggerDisplay("({Point.X}, {Point.Y})")]
[ComVisible(false)]
public struct Cell
{
	public Point2Double Point;

	public float MinimumElevation;

	public IList<int> Faces;

	public Cell(Point2Double center)
	{
		this = default;
		Point = center;
	}

	public Cell(double x, double y)
	{
		this = default;
		Point = new Point2Double(x, y);
	}

	public bool IsInitialized()
	{
		return Faces != null;
	}

	public int NextCCWFaceByGlobalIndex(int face)
	{
		int indexOf = Faces.IndexOf(face);
		if (indexOf == -1)
		{
			throw new ArgumentException("This face does not belong to this cell.");
		}
		int indexOfOffset = checked(indexOf + 1);
		if (indexOfOffset == Faces.Count)
		{
			indexOfOffset = 0;
		}
		return Faces[indexOfOffset];
	}

	public int NextCCWFaceByLocalIndex(int face)
	{
		if (face >= Faces.Count || face < 0)
		{
			throw new ArgumentException("Face is OOB");
		}
		int faceOffset = checked(face + 1);
		if (faceOffset == Faces.Count)
		{
			faceOffset = 0;
		}
		return Faces[faceOffset];
	}

	public int NextCWFaceByGlobalIndex(int face)
	{
		int indexOf = Faces.IndexOf(face);
		if (indexOf == -1)
		{
			throw new ArgumentException("This face does not belong to this cell.");
		}
		checked
		{
			int indexOfOffset = indexOf - 1;
			if (indexOfOffset == -1)
			{
				indexOfOffset = Faces.Count - 1;
			}
			return Faces[indexOfOffset];
		}
	}

	public int NextCWFaceByLocalIndex(int face)
	{
		if (face >= Faces.Count || face < 0)
		{
			throw new ArgumentException("Face is OOB");
		}
		checked
		{
			int faceOffset = face - 1;
			if (faceOffset == -1)
			{
				faceOffset = Faces.Count - 1;
			}
			return Faces[faceOffset];
		}
	}

	public static void Stream(BinaryWriter bw, IList<Cell> cells)
	{
		bw.Write(cells.Count);
		checked
		{
			int count = cells.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Cell cell = cells[i];
				bw.Write(cell.Point.X);
				bw.Write(cell.Point.Y);
				bw.Write(cell.MinimumElevation);
				if (cell.Faces is int[])
				{
					Pipe.WriteArray(bw, (int[])cell.Faces);
				}
				else
				{
					Pipe.WriteArray(bw, cell.Faces.ToArray());
				}
			}
		}
	}

	public static Cell[] Stream(BinaryReader br)
	{
		int readInt32 = br.ReadInt32();
		checked
		{
			Cell[] cells = new Cell[readInt32 - 1 + 1];
			if (readInt32 == 0)
			{
				return cells;
			}
			int count = readInt32 - 1;
			for (int i = 0; i <= count; i++)
			{
				Cell cell = default;
				double x = br.ReadDouble();
				double y = br.ReadDouble();
				float minimumElevation = br.ReadSingle();
				int[] faces = Pipe.ReadArray<int>(br);
				cell.Point = new Point2Double(x, y);
				cell.MinimumElevation = minimumElevation;
				cell.Faces = faces;
				cells[i] = cell;
			}
			return cells;
		}
	}
}
