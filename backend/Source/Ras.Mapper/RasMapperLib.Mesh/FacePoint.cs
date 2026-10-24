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
public struct FacePoint
{
	private static Point2Double NullPoint = new Point2Double(double.MinValue, double.MinValue);

	public static FacePoint NullFacePoint = new FacePoint(NullPoint);

	public Point2Double Point;

	public IList<int> Faces;

	public FacePoint(Point2Double pt)
	{
		this = default;
		Point = pt;
	}

	public FacePoint(PointM pt)
	{
		this = default;
		Point = new Point2Double(pt);
	}

	public PointM PointM()
	{
		PointM result = default;
		result.Assign(Point.X, Point.Y);
		return result;
	}

	public int CCWFaceByLocalIndex(int fi)
	{
		checked
		{
			if (fi == Faces.Count - 1)
			{
				return 0;
			}
			return fi + 1;
		}
	}

	public int CWFaceByLocalIndex(int fi)
	{
		checked
		{
			if (fi > 0)
			{
				return fi - 1;
			}
			return Faces.Count - 1;
		}
	}

	public int CCWFaceByGlobalIndex(int face)
	{
		int indexOf = Faces.IndexOf(face);
		if (indexOf == -1)
		{
			throw new ArgumentException("This face does not belong to this Face-point.");
		}
		int indexOfOffset = checked(indexOf + 1);
		if (indexOfOffset == Faces.Count)
		{
			indexOfOffset = 0;
		}
		return Faces[indexOfOffset];
	}

	public int CWFaceByGlobalIndex(int face)
	{
		int indexOf = Faces.IndexOf(face);
		if (indexOf == -1)
		{
			throw new ArgumentException("This face does not belong to this Face-point.");
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

	public bool IsNull()
	{
		if (Point.X == NullPoint.X && Point.Y == NullPoint.Y)
		{
			return true;
		}
		return false;
	}

	public static void Stream(BinaryWriter bw, IList<FacePoint> fps)
	{
		bw.Write(fps.Count);
		checked
		{
			int count = fps.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				FacePoint facePoint = fps[i];
				bw.Write(facePoint.Point.X);
				bw.Write(facePoint.Point.Y);
				if (facePoint.Faces is int[])
				{
					Pipe.WriteArray(bw, (int[])facePoint.Faces);
				}
				else
				{
					Pipe.WriteArray(bw, facePoint.Faces.ToArray());
				}
			}
		}
	}

	public static FacePoint[] Stream(BinaryReader br)
	{
		int readInt32 = br.ReadInt32();
		checked
		{
			FacePoint[] facePoints = new FacePoint[readInt32 - 1 + 1];
			if (readInt32 == 0)
			{
				return facePoints;
			}
			int count = readInt32 - 1;
			for (int i = 0; i <= count; i++)
			{
				FacePoint facePoint = default;
				double x = br.ReadDouble();
				double y = br.ReadDouble();
				int[] faces = Pipe.ReadArray<int>(br);
				facePoint.Point = new Point2Double(x, y);
				facePoint.Faces = faces;
				facePoints[i] = facePoint;
			}
			return facePoints;
		}
	}
}
