using System;
using System.Numerics;
using Geospatial;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;

namespace Ras.Engine;

public class WindBC : SpatiotemporalBoundaryCondition
{
	public bool IsLagrangian;

	public bool IsWindStress;

	public Vector2[] FaceWindVelocity;

	public Vector2[] FaceWindStress;

	public float[] FaceWindStressN;

	protected readonly Mesh _mesh;

	public Func<float, float> DragCoeff;

	private readonly Func<int, float> _getAirDensity;

	private double _LToSI;

	private float _LToSIf;

	public WindBC(double[] sourceTimes, Func<int, Vector2[]> getVectorData, Func<int, float> getAirDensity, float scaleFactor, JaggedWeights<float> vectorWeights, Mesh mesh, bool isLagrangian, bool isWindStress)
		: base(sourceTimes, getVectorData, scaleFactor, vectorWeights)
	{
		IsLagrangian = isLagrangian;
		IsWindStress = isWindStress;
		FaceWindStressN = new float[mesh.FaceCount];
		_mesh = mesh;
		_getAirDensity = getAirDensity;
		FaceWindVelocity = null;
		FaceWindStress = null;
		FaceWindStressN = null;
	}

	public void Prepare(double LToSI)
	{
		FaceWindVelocity = new Vector2[_mesh.FaceCount];
		FaceWindStress = new Vector2[_mesh.FaceCount];
		FaceWindStressN = new float[_mesh.FaceCount];
		_LToSI = LToSI;
		_LToSIf = (float)LToSI;
	}

	public void Update(double time, Geospatial.Vectors.Vector[] cellVelocity = null)
	{
		if (IsWindStress)
		{
			UpdateVector(FaceWindStress, time);
			UpdateWindStressNFromStress();
		}
		else
		{
			UpdateVector(FaceWindVelocity, time);
			UpdateWindStressNFromVelocity(cellVelocity);
		}
	}

	private void UpdateWindStressNFromStress()
	{
		HydraulicMeshComputer metadataCache = _mesh.MetadataCache;
		for (int i = 0; i < _mesh.FaceCount; i++)
		{
			FaceWindStressN[i] = Vector2.Dot(FaceWindStress[i], metadataCache.FaceNormalF[i]);
		}
	}

	private void UpdateWindStressNFromVelocity(Geospatial.Vectors.Vector[] cellVelocity)
	{
		HydraulicMeshComputer metadataCache = _mesh.MetadataCache;
		if (IsLagrangian && cellVelocity != null)
		{
			for (int i = 0; i < _mesh.FaceCount; i++)
			{
				float rhoa = _getAirDensity(i);
				Face face = _mesh.Faces[i];
				int cellA = face.cellA;
				int cellB = face.cellB;
				Vector2 vector = (Vector2)cellVelocity[cellA];
				if (cellB >= 0)
				{
					vector = 0.5f * (vector + (Vector2)cellVelocity[cellB]);
				}
				Vector2 w = FaceWindVelocity[i];
				Vector2 windStressLagriangian = WindStressLagriangian(rhoa, DragCoeff, w, vector);
				FaceWindStressN[i] = Vector2.Dot(windStressLagriangian, metadataCache.FaceNormalF[i]);
			}
		}
		else
		{
			for (int j = 0; j < _mesh.FaceCount; j++)
			{
				float rhoa = _getAirDensity(j);
				Vector2 w2 = FaceWindVelocity[j];
				Vector2 windStressEulerian = WindStressEulerian(rhoa, DragCoeff, w2);
				FaceWindStressN[j] = Vector2.Dot(windStressEulerian, metadataCache.FaceNormalF[j]);
			}
		}
	}

	public Vector2 WindStressLagriangian(float rhoa, Func<float, float> dragCoeff, Vector2 W, Vector2 U)
	{
		Vector2 vector = W - U;
		float vectorTerm = vector.Length();
		float dragCoeff2 = dragCoeff(vectorTerm * _LToSIf);
		return rhoa * dragCoeff2 * vectorTerm * vector;
	}

	public Vector2 WindStressEulerian(float rhoa, Func<float, float> dragCoeff, Vector2 W)
	{
		float computed = W.Length();
		float dragCoeff2 = dragCoeff(computed * _LToSIf);
		return rhoa * dragCoeff2 * computed * W;
	}

	public static float WindDragHsu(float W)
	{
		if ((double)W <= 30.0)
		{
			return MathF.Pow(0.4f / (14.56f - 2f * MathF.Log(MathF.Max(W, 0.01f))), 2f);
		}
		return MathF.Max(0.00386f - 4E-05f * W, 0.0015f);
	}

	public static float WindDragAndreas(float W)
	{
		float computed = MathF.Max(W, 0.19f);
		float computed2 = computed - 8.271f;
		computed2 = 0.239f + 0.0433f * (computed2 + MathF.Sqrt(0.12f * MathF.Pow(computed2, 2f) + 0.181f));
		return MathF.Pow(computed2 / computed, 2f);
	}
}
