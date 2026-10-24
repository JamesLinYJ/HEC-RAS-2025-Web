using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Utility.Hashing;
using Utility.Systems;

namespace Geospatial.Vectors;

[DebuggerDisplay("({Point.X}, {Point.Y})")]
public struct FacePoint : IDataHash
{
	public static FacePoint NullFacePoint = new FacePoint(Point.NullPoint);

	public Point Point;

	public IList<int> Faces;

	public FacePoint(Point pt)
	{
		Point = pt;
		Faces = null;
	}

	public FacePoint(double x, double y)
	{
		Point = new Point(x, y);
		Faces = null;
	}

	public FacePoint Clone()
	{
		FacePoint result = new FacePoint(Point);
		result.Faces = Faces.ToArray();
		return result;
	}

	public int CCWFaceByLocalIndex(int fi)
	{
		if (fi == Faces.Count - 1)
		{
			return 0;
		}
		return fi + 1;
	}

	public int CWFaceByLocalIndex(int fi)
	{
		if (fi > 0)
		{
			return fi - 1;
		}
		return Faces.Count - 1;
	}

	public int CCWFaceByGlobalIndex(int face)
	{
		int indexOf = Faces.IndexOf(face);
		if (indexOf == -1)
		{
			throw new ArgumentException("This face does not belong to this Face-point.");
		}
		int indexOfOffset = indexOf + 1;
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
		int indexOfOffset = indexOf - 1;
		if (indexOfOffset == -1)
		{
			indexOfOffset = Faces.Count - 1;
		}
		return Faces[indexOfOffset];
	}

	public bool IsNull()
	{
		return Point == Point.NullPoint;
	}

	public static void Stream(BinaryWriter bw, IList<FacePoint> fps)
	{
		bw.Write(fps.Count);
		for (int i = 0; i <= fps.Count - 1; i++)
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

	public static FacePoint[] Stream(BinaryReader br)
	{
		int readInt32 = br.ReadInt32();
		FacePoint[] facePoints = new FacePoint[readInt32 - 1 + 1];
		if (readInt32 == 0)
		{
			return facePoints;
		}
		for (int i = 0; i <= readInt32 - 1; i++)
		{
			FacePoint facePoint = default;
			double x = br.ReadDouble();
			double y = br.ReadDouble();
			int[] faces = Pipe.ReadArray<int>(br);
			facePoint.Point = new Point(x, y);
			facePoint.Faces = faces;
			facePoints[i] = facePoint;
		}
		return facePoints;
	}

	public int GetDataHash()
	{
		int dataHash = Point.GetHashCode();
		if (Faces != null)
		{
			foreach (int face in Faces)
			{
				dataHash = StableHash.Combine(dataHash, face);
			}
		}
		return dataHash;
	}
}
