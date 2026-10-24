using System;
using System.Collections.Generic;
using Geospatial.Vectors.Meshes;
using Utility.Matrices;

namespace Geospatial.Vectors;

public class FaceTangential
{
	public SparseMatrix<double> A;

	public SparseMatrix<double> B;

	public SparseMatrix<double> H;

	private Mesh _mesh;

	public FaceTangential(Mesh mesh, double[] fWeights = null)
	{
		Initialize(mesh, fWeights);
	}

	private void Initialize(Mesh mesh, double[] fWeights)
	{
		_mesh = mesh;
		A = new SparseMatrix<double>();
		B = new SparseMatrix<double>();
		H = new SparseMatrix<double>();
		if (fWeights == null)
		{
			fWeights = new double[_mesh.FaceCount];
			Array.Fill(fWeights, 1.0);
		}
		ComputeFaceTangentialSide(A, fWeights, isSideB: false);
		ComputeFaceTangentialSide(B, fWeights, isSideB: true);
		SparseMatrix<double>.AddMatrices(A, B, H);
		SparseMatrix<double>.MultiplyByScalar(H, 0.5);
	}

	private void ComputeFaceTangentialSide(SparseMatrix<double> C, double[] fWeights, bool isSideB)
	{
		int faceCount = _mesh.FaceCount;
		int maxNonZero = faceCount * 5;
		C.Initialize(faceCount, maxNonZero);
		HydraulicMeshComputer metadataCache = _mesh.MetadataCache;
		Vector[] fNorms = metadataCache.FaceNormal;
		bool[] rotationDirection = new bool[2] { true, false };
		int[] rowFaces = new int[5];
		double[] rowValues = new double[5];
		for (int i = 0; i < faceCount; i++)
		{
			ComputeTangentialSideFace(i);
		}
		C.Sort(skipColumnPointers: true);
		C.Pack();
		void ComputeTangentialSideFace(int fIdx)
		{
			Face face = _mesh.Faces[fIdx];
			int cellA = ((!isSideB || face.cellB == -1) ? face.cellA : face.cellB);
			Cell cell = _mesh.Cells[cellA];
			Span<int> faces = cell.Faces.Span;
			List<int> intList = new List<int> { fIdx };
			double faceSimpleLength = _mesh.FaceSimpleLength(fIdx);
			int count = Math.Min(faces.Length - 2, 2);
			bool[] rotationDirectionArray = rotationDirection;
			foreach (bool rotationDirection2 in rotationDirectionArray)
			{
				int fIdx2 = fIdx;
				double faceSimpleLengthSum = 0.0;
				for (int k = 0; k < count; k++)
				{
					fIdx2 = ((!rotationDirection2) ? cell.NextCWFaceByGlobalIndex(fIdx2) : cell.NextCCWFaceByGlobalIndex(fIdx2));
					intList.Add(fIdx2);
					double faceSimpleLength2 = _mesh.FaceSimpleLength(fIdx2);
					faceSimpleLengthSum += faceSimpleLength2;
					if (faceSimpleLengthSum > 0.5 * faceSimpleLength)
					{
						break;
					}
				}
			}
			double computed = 0.0;
			double computed2 = 0.0;
			double computed3 = 0.0;
			for (int l = 0; l < intList.Count; l++)
			{
				int ordinal = intList[l];
				double fWeight = fWeights[ordinal];
				double deltaX = fNorms[ordinal].DeltaX;
				double deltaY = fNorms[ordinal].DeltaY;
				computed += fWeight * deltaX * deltaX;
				computed2 += fWeight * deltaX * deltaY;
				computed3 += fWeight * deltaY * deltaY;
			}
			double computed4 = computed * computed3 - computed2 * computed2;
			if (Math.Abs(computed4) >= 1E-10)
			{
				double deltaX = fNorms[fIdx].DeltaX;
				double deltaY = fNorms[fIdx].DeltaY;
				double deltaXDeltaYTerm = ((0.0 - computed2) * deltaX - computed3 * deltaY) / computed4;
				double deltaXDeltaYTerm2 = (computed * deltaX + computed2 * deltaY) / computed4;
				for (int m = 0; m < intList.Count; m++)
				{
					int rowFaces2 = intList[m];
					double fWeight2 = fWeights[rowFaces2];
					deltaX = fNorms[rowFaces2].DeltaX;
					deltaY = fNorms[rowFaces2].DeltaY;
					rowValues[m] = fWeight2 * (deltaXDeltaYTerm * deltaX + deltaXDeltaYTerm2 * deltaY);
					rowFaces[m] = rowFaces2;
				}
			}
			else
			{
				for (int n = 0; n < intList.Count; n++)
				{
					rowValues[n] = 0.0;
					rowFaces[n] = -1;
				}
			}
			SparseMatrix<double>.BuildRow(C, fIdx, rowFaces, rowValues, 1E-15, intList.Count);
		}
	}
}
