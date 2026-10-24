using System;
using System.Threading.Tasks;
using Utility;

namespace Geospatial.Vectors.Meshes;

public class VectorMeshInputs
{
	public Mesh Mesh { get; set; }

	public HydraulicMeshComputer HydraulicMesh => Mesh.MetadataCache;

	public WaterSurfaceMeshInputs WaterSurfaceInputs { get; set; }

	public IDisposable Cleanup { get; set; }

	public Memory<float> FacePerpendicularValues { get; set; }

	public Memory<(Vector, Vector)> FaceTrueVectors { get; set; }

	public Memory<FacePointVels> FacepointVectors { get; set; }

	public VectorMeshInputs(Mesh mesh)
	{
		Mesh = mesh;
		FacePerpendicularValues = new float[mesh.FaceCount];
		FaceTrueVectors = new (Vector, Vector)[mesh.FaceCount];
		FacepointVectors = new FacePointVels[mesh.FacePointCount];
		int count3 = 0;
		for (int i = 0; i < mesh.FacePointCount; i++)
		{
			int count = mesh.FacePoints[i].Faces.Count;
			count3 += count;
		}
		Memory<Vector> vectors = new Vector[count3];
		Span<FacePointVels> facepointVectors = FacepointVectors.Span;
		count3 = 0;
		for (int j = 0; j < mesh.FacePointCount; j++)
		{
			int count2 = mesh.FacePoints[j].Faces.Count;
			facepointVectors[j] = new FacePointVels(vectors.Slice(count3, count2));
			count3 += count2;
		}
	}

	public void Build()
	{
		Task task = Task.Run(() =>
		{
			Utility.Parallel.SmartFor(Mesh.FaceCount, (int start, int finish) =>
			{
				Span<FaceValues> faceWaterSurfaces = WaterSurfaceInputs.FaceWaterSurfaces.Span;
				Span<float> facePerpendicularValues = FacePerpendicularValues.Span;
				Span<(Vector, Vector)> faceTrueVectors = FaceTrueVectors.Span;
				for (int i = start; i <= finish; i++)
				{
					faceTrueVectors[i] = HydraulicMesh.FaceComputes.Vector_CAveragedStencil(i, facePerpendicularValues, faceWaterSurfaces);
				}
			}, 256);
		});
		Task task2 = Task.Run(() =>
		{
			Utility.Parallel.SmartFor(Mesh.FacePointCount, (int start, int finish) =>
			{
				Span<FaceValues> faceWaterSurfacesSpan = WaterSurfaceInputs.FaceWaterSurfaces.Span;
				Span<float> facePerpendicularValuesSpan = FacePerpendicularValues.Span;
				Span<FacePointVels> facepointVectors = FacepointVectors.Span;
				for (int i = start; i <= finish; i++)
				{
					HydraulicMesh.NodeComputes.Velocity_FacePerpLeastSquares_Weighted(i, facePerpendicularValuesSpan, faceWaterSurfacesSpan, facepointVectors[i].Velocity.Span);
				}
			}, 256);
		});
		task.Wait();
		task2.Wait();
	}

	public void GetLocalFacepointValues(int c, Span<Vector> fpLocalValues)
	{
		Cell cell = Mesh.Cells[c];
		Span<int> faces = cell.Faces.Span;
		int length = faces.Length;
		Span<FacePointVels> facepointVectors = FacepointVectors.Span;
		for (int i = 0; i < length; i++)
		{
			int index = i;
			int faceIndex = faces[i];
			int fPPrev = Mesh.Faces[faceIndex].GetFPPrev(c);
			fpLocalValues[index] = facepointVectors[fPPrev].Velocity.Span[Mesh.FacePoints[fPPrev].Faces.IndexOf(faceIndex)];
		}
	}

	public void GetLocalFaceValues(int c, Span<Vector> faceValues)
	{
		Cell cell = Mesh.Cells[c];
		Span<int> faces = cell.Faces.Span;
		int length = faces.Length;
		Span<(Vector, Vector)> faceTrueVectors = FaceTrueVectors.Span;
		for (int i = 0; i < length; i++)
		{
			int faceIndex = faces[i];
			if (c == Mesh.Faces[faceIndex].cellA)
			{
				faceValues[i] = faceTrueVectors[faceIndex].Item1;
			}
			else
			{
				faceValues[i] = faceTrueVectors[faceIndex].Item2;
			}
		}
	}

	public Vector ComputeCellValue_FacePerpLeastSquares(int cIdx)
	{
		FaceVelocityCoef faceVelocityCoef = HydraulicMesh.CellComputes.VelocityCoef(cIdx);
		double computed = 0.0;
		double computed2 = 0.0;
		Span<float> facePerpendicularValues = FacePerpendicularValues.Span;
		Cell cell = Mesh.Cells[cIdx];
		Span<int> faces = cell.Faces.Span;
		for (int i = 0; i < faces.Length; i++)
		{
			int faceIndex = faces[i];
			Vector vector = HydraulicMesh.FaceNormal[faceIndex];
			float facePerpendicularValue = facePerpendicularValues[faceIndex];
			computed += vector.X * (double)facePerpendicularValue;
			computed2 += vector.Y * (double)facePerpendicularValue;
		}
		double dx = faceVelocityCoef.A22 * computed - faceVelocityCoef.A12 * computed2;
		double dy = (0.0 - faceVelocityCoef.A12) * computed + faceVelocityCoef.A11 * computed2;
		return new Vector(dx, dy);
	}
}
