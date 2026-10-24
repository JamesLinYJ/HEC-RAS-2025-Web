using System;
using Geospatial;
using Geospatial.Vectors;

namespace Ras.Engine;

public class AirPressureBC : SpatiotemporalBoundaryCondition
{
	public double[] CellAirPressure;

	public double[] FaceGradAirPressure;

	private readonly Mesh _mesh;

	public AirPressureBC(double[] sourceTimes, Func<int, float[]> getSourceData, float scaleFactor, JaggedWeights<float> cellWeights, Mesh mesh)
		: base(sourceTimes, getSourceData, scaleFactor, cellWeights, TimeConvention.Instantaneous)
	{
		_mesh = mesh;
		CellAirPressure = null;
		FaceGradAirPressure = null;
	}

	public void Prepare()
	{
		CellAirPressure = new double[_mesh.CellCount];
		FaceGradAirPressure = new double[_mesh.FaceCount];
	}

	public void Update(double time, Memory<double> faceInvDist)
	{
		UpdateScalar(CellAirPressure, time);
		UpdateAirPressureGradient(faceInvDist);
	}

	private void UpdateAirPressureGradient(Memory<double> faceInvDist)
	{
		Span<double> faceInvDistSpan = faceInvDist.Span;
		for (int i = 0; i < _mesh.FaceCount; i++)
		{
			Face face = _mesh.Faces[i];
			int cellB = face.cellB;
			if (cellB >= 0)
			{
				int cellA = face.cellA;
				FaceGradAirPressure[i] = (CellAirPressure[cellB] - CellAirPressure[cellA]) * faceInvDistSpan[i];
			}
			else
			{
				FaceGradAirPressure[i] = 0.0;
			}
		}
	}
}
