using System;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Utility.Progress;

namespace Ras.Engine;

public class ConveyanceFaceApportionment : IFaceApportionment
{
	private BoundaryCondition _bc;

	private readonly HydraulicMeshComputer _hmesh;

	private Func<MeshWaterState, int, double> _faceConveyanceFunc;

	private readonly double _absTolEta;

	private double TotalFaceLength
	{
		get
		{
			double faceSimpleLengthSum = 0.0;
			for (int i = 0; i < _bc.Faces.Length; i++)
			{
				int faceIndex = _bc.Faces[i];
				faceSimpleLengthSum += (double)_hmesh.FaceSimpleLengths[faceIndex];
			}
			return faceSimpleLengthSum;
		}
	}

	public ConveyanceFaceApportionment(HydraulicMeshComputer hmesh, double absTolEta)
	{
		_hmesh = hmesh;
		_absTolEta = absTolEta;
	}

	public double[] GetFaceFractions(Mesh mesh, MeshWaterState ws, double targetConveyance, double[] result = null)
	{
		double[] faceFractions = result ?? new double[_bc.Faces.Length];
		double minFaceElevation = GetMinFaceElevation(ws);
		minFaceElevation = Math.Max(minFaceElevation, GetMaxWetFaceElevation(ws));
		minFaceElevation = Math.Max(minFaceElevation, GetMaxWetCellElevation(mesh, ws, _bc.Faces));
		ws.UpdateFacesProperties(_bc.Faces, minFaceElevation);
		double faceAreaSum = 0.0;
		double totConveyance = 0.0;
		for (int i = 0; i < _bc.Faces.Length; i++)
		{
			int faceIndex = _bc.Faces[i];
			double faceFractions2 = _faceConveyanceFunc(ws, faceIndex);
			totConveyance += faceFractions2;
			faceFractions[i] = faceFractions2;
			faceAreaSum += ws.FaceArea[faceIndex];
		}
		if (totConveyance < targetConveyance)
		{
			double absTolEtaFaceAreaSumTerm = Math.Max(_absTolEta, faceAreaSum / TotalFaceLength);
			double computed = 1.01;
			double minFaceElevation2 = minFaceElevation + absTolEtaFaceAreaSumTerm;
			int ordinal = 30;
			while (ordinal >= 0)
			{
				minFaceElevation2 += absTolEtaFaceAreaSumTerm;
				totConveyance = ComputeTotConveyance(minFaceElevation2);
				if (totConveyance >= targetConveyance)
				{
					break;
				}
				minFaceElevation = minFaceElevation2;
				ordinal--;
				absTolEtaFaceAreaSumTerm *= 2.0;
			}
			while (minFaceElevation2 > minFaceElevation + _absTolEta)
			{
				double minFaceElevation3 = 0.5 * (minFaceElevation2 + minFaceElevation);
				totConveyance = ComputeTotConveyance(minFaceElevation3);
				if (totConveyance < targetConveyance)
				{
					minFaceElevation = minFaceElevation3;
					continue;
				}
				if (!(totConveyance > targetConveyance * computed))
				{
					break;
				}
				minFaceElevation2 = minFaceElevation3;
			}
		}
		if (!(totConveyance > 0.0))
		{
			double faceFractions3 = ((faceFractions.Length != 0) ? (1.0 / (double)faceFractions.Length) : 0.0);
			for (int j = 0; j < faceFractions.Length; j++)
			{
				faceFractions[j] = faceFractions3;
			}
			return faceFractions;
		}
		for (int k = 0; k < faceFractions.Length; k++)
		{
			faceFractions[k] /= totConveyance;
		}
		return faceFractions;
		double ComputeTotConveyance(double eta)
		{
			double faceConveyanceFuncSum = 0.0;
			ws.UpdateFacesProperties(_bc.Faces, eta);
			for (int l = 0; l < _bc.Faces.Length; l++)
			{
				int faceIndex2 = _bc.Faces[l];
				if (ws.FaceArea[faceIndex2] > 0.0)
				{
					double faceFractions4 = _faceConveyanceFunc(ws, faceIndex2);
					faceConveyanceFuncSum += faceFractions4;
					faceFractions[l] = faceFractions4;
				}
				else
				{
					faceFractions[l] = 0.0;
				}
			}
			return faceConveyanceFuncSum;
		}
	}

	public double[] GetFaceFractionsByStage(Mesh mesh, MeshWaterState ws, double stage, double[] result = null)
	{
		if (_faceConveyanceFunc == null)
		{
			throw new InvalidOperationException("Face conveyance function is not set.");
		}
		double[] faceFractionsByStage = result ?? new double[_bc.Faces.Length];
		ws.UpdateFacesProperties(_bc.Faces, stage);
		double computed = 0.0;
		for (int i = 0; i < _bc.Faces.Length; i++)
		{
			int faceIndex = _bc.Faces[i];
			computed += (faceFractionsByStage[i] = ((ws.FaceArea[faceIndex] > 0.0) ? _faceConveyanceFunc(ws, faceIndex) : 0.0));
		}
		if (computed <= 0.0)
		{
			throw new InvalidOperationException($"Total conveyance is non-positive at stage {stage}.");
		}
		for (int j = 0; j < faceFractionsByStage.Length; j++)
		{
			faceFractionsByStage[j] /= computed;
		}
		return faceFractionsByStage;
	}

	private double GetMaxWetCellElevation(Mesh mesh, MeshWaterState ws, int[] faceIndexes)
	{
		double minValue = double.MinValue;
		foreach (int faceIndex in faceIndexes)
		{
			int cellA = mesh.Faces[faceIndex].cellA;
			if (cellA != -1 && !(ws.CellVolume[cellA] <= 0.0))
			{
				minValue = Math.Max(minValue, ws.CellElevation[cellA]);
			}
		}
		return minValue;
	}

	private double GetMinFaceElevation(MeshWaterState ws)
	{
		double maxValue = double.MaxValue;
		int[] faces = _bc.Faces;
		foreach (int faceIndex in faces)
		{
			maxValue = Math.Min(maxValue, ws.FaceElevation[faceIndex]);
		}
		return maxValue;
	}

	private double GetMaxWetFaceElevation(MeshWaterState ws)
	{
		double minValue = double.MinValue;
		int[] faces = _bc.Faces;
		foreach (int faceIndex in faces)
		{
			if (ws.FaceArea[faceIndex] > 0.0)
			{
				minValue = Math.Max(minValue, ws.FaceElevation[faceIndex]);
			}
		}
		return minValue;
	}

	public void Prepare(ProgressReporter pr, BoundaryCondition bc, Mesh mesh, MeshWaterState ws, Func<MeshWaterState, int, double> faceConveyanceFunc)
	{
		_bc = bc ?? throw new ArgumentNullException("bc");
		_faceConveyanceFunc = faceConveyanceFunc ?? throw new ArgumentNullException("faceConveyanceFunc");
	}
}
