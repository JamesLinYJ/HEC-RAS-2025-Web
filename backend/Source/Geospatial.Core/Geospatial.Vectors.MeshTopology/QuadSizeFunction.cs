using System;
using System.Collections.Generic;
using Geospatial.Transformers;

namespace Geospatial.Vectors.MeshTopology;

public class QuadSizeFunction
{
	public static void InferSizeFunctions(ConceptualMesh cmesh, int cIdx, ISpatialTransformer transform, out ISizeFunction sizeNS, out ISizeFunction sizeEW, bool fromNodes = false)
	{
		InferSizeFunctions(cmesh, cIdx, transform, out sizeNS, out sizeEW, out var _, fromNodes);
	}

	public static void InferSizeFunctions(ConceptualMesh cmesh, int cIdx, ISpatialTransformer transform, out ISizeFunction sizeNS, out ISizeFunction sizeEW, out SizeMesh sizeMesh, bool fromNodes = false)
	{
		PatchCommon patchCommon;
		if (transform is QuadTransformBase quadTransformBase)
		{
			patchCommon = quadTransformBase;
		}
		else
		{
			patchCommon = new PatchCommon(cmesh, cIdx);
			patchCommon.SetSides();
		}
		sizeMesh = new SizeMesh();
		List<float> nodeScale = new List<float>();
		List<float> nodeScaleList = new List<float>();
		HashSet<int> intSet = new HashSet<int>();
		HashSet<int> intSetSet = new HashSet<int>();
		foreach (HalfEdge originalPiece in patchCommon.Sides[2].OriginalPieces)
		{
			intSetSet.Add(originalPiece.fIdx);
		}
		foreach (HalfEdge originalPiece2 in patchCommon.Sides[0].OriginalPieces)
		{
			intSetSet.Add(originalPiece2.fIdx);
		}
		foreach (HalfEdge originalPiece3 in patchCommon.Sides[1].OriginalPieces)
		{
			intSet.Add(originalPiece3.fIdx);
		}
		foreach (HalfEdge originalPiece4 in patchCommon.Sides[3].OriginalPieces)
		{
			intSet.Add(originalPiece4.fIdx);
		}
		int ordinal = -1;
		int halfEdgeNodeBack = cmesh.HalfEdgeNodeBack(cmesh.CellFaces[cIdx][0]);
		int ordinal2 = -1;
		foreach (HalfEdge cellFace in cmesh.CellFaces[cIdx])
		{
			Polyline polyline = cmesh.OrientedArcWithScale(cellFace, fromNodes, fromNodes);
			for (int i = 0; i < polyline.Count; i++)
			{
				int unity = -1;
				if (i == polyline.Count - 1 && cmesh.HalfEdgeNodeForward(cellFace) == halfEdgeNodeBack)
				{
					unity = ordinal2;
				}
				else if (i > 0 || ordinal < 0)
				{
					unity = sizeMesh.AddNode(polyline[i]);
					nodeScaleList.Add(float.NaN);
					nodeScale.Add(float.NaN);
					if (ordinal < 0)
					{
						ordinal2 = unity;
					}
				}
				else
				{
					unity = ordinal;
				}
				float polylineTerm = (float)polyline.Z[i];
				if (float.IsFinite(polylineTerm))
				{
					if (intSet.Contains(cellFace.fIdx))
					{
						nodeScale[unity] = NanMin(nodeScale[unity], polylineTerm);
					}
					else if (intSetSet.Contains(cellFace.fIdx))
					{
						nodeScaleList[unity] = NanMin(nodeScaleList[unity], polylineTerm);
					}
				}
				if (i > 0)
				{
					sizeMesh.AddFace(ordinal, unity);
				}
				ordinal = unity;
			}
		}
		sizeMesh.NodeScale = nodeScale;
		sizeNS = new TinSizeFunction(sizeMesh, 0.7);
		sizeMesh.NodeScale = nodeScaleList;
		sizeEW = new TinSizeFunction(sizeMesh, 0.7);
		static float NanMin(float A, float B)
		{
			if (float.IsNaN(A))
			{
				return B;
			}
			if (float.IsNaN(B))
			{
				return A;
			}
			return Math.Min(A, B);
		}
	}

	public static double InferNominalCellSize(ConceptualMesh cmesh, int cIdx)
	{
		double faceRawLineLengthSum = 0.0;
		double polylineLengthSum = 0.0;
		int countOffsetSum = 0;
		foreach (HalfEdge cellFace in cmesh.CellFaces[cIdx])
		{
			Polyline polyline = cmesh.FaceNodeString[cellFace.fIdx];
			if (polyline == null)
			{
				faceRawLineLengthSum += cmesh.FaceRawLine[cellFace.fIdx].Length;
				continue;
			}
			polylineLengthSum += polyline.Length;
			countOffsetSum += polyline.Count - 1;
		}
		if (countOffsetSum > 0)
		{
			return polylineLengthSum / (double)countOffsetSum;
		}
		return faceRawLineLengthSum / 4.0;
	}
}
