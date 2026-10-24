using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Utility;

namespace Ras.Engine;

public class TurbulenceState
{
	private SolverControl control;

	public float[] cellStrainRate;

	public GradientUVXY[] cellVelGrad;

	public float[] faceEddyVis;

	public double[] cellK;

	public double[] cellEps;

	public double[] cellLength;

	public double[] cellNut;

	public Mesh mesh;

	public HydraulicMeshComputer hmc;

	public bool IsTransported => control.Turbulence.Model != TurbulenceModel.Smagorinsky;

	private bool IsSWE => control.equationSet == SolverControl.EquationSet.SWE;

	public TurbulenceState(Mesh mesh, SolverControl control)
	{
		this.mesh = mesh;
		hmc = mesh.MetadataCache;
		this.control = control;
		faceEddyVis = new float[mesh.FaceCount];
		if (control.Turbulence.HorizMixCoeff > 1E-20 || IsTransported)
		{
			cellStrainRate = new float[mesh.CellCount];
		}
		if (control.Turbulence.UseCrossDiffusion && IsSWE)
		{
			cellVelGrad = new GradientUVXY[mesh.CellCount];
		}
		if (IsTransported)
		{
			cellK = new double[mesh.CellCount];
			cellEps = new double[mesh.CellCount];
			if (control.Turbulence.Model == TurbulenceModel.PrandtlKL)
			{
				cellLength = new double[mesh.CellCount];
			}
			cellNut = new double[mesh.CellCount];
		}
	}

	internal void PrepareForControl(Mesh currentMesh, SolverControl currentControl)
	{
		if (mesh.CellCount != currentMesh.CellCount || mesh.FaceCount != currentMesh.FaceCount)
		{
			throw new SolverException("Existing turbulence state dimensions do not match the solver mesh.");
		}
		if (control.Turbulence.Model != currentControl.Turbulence.Model)
		{
			throw new SolverException($"Existing turbulence model {control.Turbulence.Model} does not match the solver model {currentControl.Turbulence.Model}.");
		}
		ValidateArray(faceEddyVis, currentMesh.FaceCount, "faceEddyVis");
		if (IsTransported)
		{
			ValidateArray(cellK, currentMesh.CellCount, "cellK");
			ValidateArray(cellEps, currentMesh.CellCount, "cellEps");
			ValidateArray(cellNut, currentMesh.CellCount, "cellNut");
			if (control.Turbulence.Model == TurbulenceModel.PrandtlKL)
			{
				ValidateArray(cellLength, currentMesh.CellCount, "cellLength");
			}
		}
		mesh = currentMesh;
		hmc = currentMesh.MetadataCache;
		control = currentControl;
		bool horizMixCoeffIsPositiveOrIsTransported = currentControl.Turbulence.HorizMixCoeff > 1E-20 || IsTransported;
		ref float[] reference = ref cellStrainRate;
		float[] referenceArray;
		if (horizMixCoeffIsPositiveOrIsTransported)
		{
			referenceArray = ((cellStrainRate == null || cellStrainRate.Length != currentMesh.CellCount) ? new float[currentMesh.CellCount] : cellStrainRate);
		}
		else
		{
			referenceArray = null;
		}
		reference = referenceArray;
		bool isSWE = currentControl.Turbulence.UseCrossDiffusion && IsSWE;
		ref GradientUVXY[] reference2 = ref cellVelGrad;
		GradientUVXY[] reference2Array;
		if (isSWE)
		{
			reference2Array = ((cellVelGrad == null || cellVelGrad.Length != currentMesh.CellCount) ? new GradientUVXY[currentMesh.CellCount] : cellVelGrad);
		}
		else
		{
			reference2Array = null;
		}
		reference2 = reference2Array;
	}

	private static void ValidateArray(Array values, int expectedLength, string fieldName)
	{
		if (values == null || values.Length != expectedLength)
		{
			throw new SolverException("Existing turbulence field " + fieldName + " does not match the solver mesh.");
		}
	}

	public TurbulenceState Clone()
	{
		return new TurbulenceState(mesh, control)
		{
			faceEddyVis = faceEddyVis.ToArray(),
			cellK = cellK?.ToArray(),
			cellEps = cellEps?.ToArray(),
			cellLength = cellLength?.ToArray(),
			cellNut = cellNut?.ToArray(),
			cellVelGrad = cellVelGrad?.ToArray()
		};
	}

	public void ComputeCellVelGradients(MeshWaterState ws, List<int> cellIndexes)
	{
		hmc.CellComputes.StrainRate(cellIndexes, ws.FaceVelocity, ws.FaceVelTangA, ws.FaceVelTangB, ws.CellVolume, cellStrainRate, cellVelGrad);
	}

	public void ComputeCellVelGradients(MeshWaterState ws, List<int> cellIndexes, ThreadFor threadFor)
	{
		threadFor.Run(cellIndexes.Count, (int start, int stop) =>
		{
			Span<int> cellIndexes2 = CollectionsMarshal.AsSpan(cellIndexes).Slice(start, stop - start + 1);
			hmc.CellComputes.StrainRate(cellIndexes2, ws.FaceVelocity, ws.FaceVelTangA, ws.FaceVelTangB, ws.CellVolume, cellStrainRate, cellVelGrad);
		});
	}

	public void ComputeFaceTurbEddyVisALL(MeshWaterState ws, List<int> faceIndexes, List<int> cellIndexes)
	{
		if (!IsTransported && control.Turbulence.HorizMixCoeff > 1E-20 && cellVelGrad == null)
		{
			ComputeCellVelGradients(ws, cellIndexes);
		}
		foreach (int faceIndex in faceIndexes)
		{
			if (mesh.Faces[faceIndex].cellB >= 0 && ws.FaceArea[faceIndex] > 0.0)
			{
				ComputeFaceTurbEddyVisONE(faceIndex, ws);
			}
		}
	}

	public void ComputeFaceTurbEddyVisALL(MeshWaterState ws, List<int> faceIndexes, List<int> cellIndexes, ThreadFor threadFor)
	{
		if (!IsTransported && control.Turbulence.HorizMixCoeff > 1E-20 && cellVelGrad == null)
		{
			ComputeCellVelGradients(ws, cellIndexes, threadFor);
		}
		threadFor.Run(faceIndexes.Count, (int start, int stop) =>
		{
			Face[] faces = mesh.Faces;
			List<int> faceIndexesList = faceIndexes;
			double[] faceArea = ws.FaceArea;
			for (int i = start; i <= stop; i++)
			{
				int faceIndex = faceIndexesList[i];
				if (faces[faceIndex].cellB >= 0 && faceArea[faceIndex] > 0.0)
				{
					ComputeFaceTurbEddyVisONE(faceIndex, ws);
				}
			}
		});
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void ComputeFaceTurbEddyVisONE(int fIdx, MeshWaterState ws)
	{
		if (ws.FaceArea[fIdx] <= 0.0)
		{
			faceEddyVis[fIdx] = 0f;
			return;
		}
		TurbulenceParams turbulence = control.Turbulence;
		double baseEddyViscosity = turbulence.BaseEddyViscosity;
		if (IsTransported)
		{
			baseEddyViscosity += InterpolateCellToFace(fIdx, cellNut);
			faceEddyVis[fIdx] = (float)baseEddyViscosity;
			return;
		}
		if (turbulence.HorizMixCoeff > 1E-20)
		{
			Face face = mesh.Faces[fIdx];
			int cellA = face.cellA;
			int cellB = face.cellB;
			double valueACellStrainRateTerm;
			double valueACellAreasTerm;
			if (cellB >= 0)
			{
				FaceAB faceAB = hmc.FaceAlpha[fIdx];
				double valueA = faceAB.ValueA;
				double valueB = faceAB.ValueB;
				valueACellStrainRateTerm = valueA * (double)cellStrainRate[cellA] + valueB * (double)cellStrainRate[cellB];
				valueACellAreasTerm = valueA * hmc.CellAreas[cellA] + valueB * hmc.CellAreas[cellB];
			}
			else
			{
				valueACellStrainRateTerm = cellStrainRate[cellA];
				valueACellAreasTerm = hmc.CellAreas[cellA];
			}
			baseEddyViscosity += turbulence.HorizMixCoeff * valueACellAreasTerm * valueACellStrainRateTerm;
		}
		if (turbulence.LongMixCoeff > 0.0 || turbulence.TransMixCoeff > 0.0)
		{
			double faceShearFluxCoeffWsTerm = (double)ws.FaceShearFluxCoeff[fIdx] * (double)ws.FaceVelMag[fIdx];
			if (turbulence.LongMixCoeff != turbulence.TransMixCoeff)
			{
				double longMixCoeffTurbulenceTerm = turbulence.LongMixCoeff * faceShearFluxCoeffWsTerm;
				double transMixCoeffTurbulenceTerm = turbulence.TransMixCoeff * faceShearFluxCoeffWsTerm;
				double faceVelMag = Math.Max(ws.FaceVelMag[fIdx], 1E-20);
				double faceVelocityOverFaceVelMagSquared = Math.Pow(ws.FaceVelocity[fIdx] / faceVelMag, 2.0);
				baseEddyViscosity += transMixCoeffTurbulenceTerm + (longMixCoeffTurbulenceTerm - transMixCoeffTurbulenceTerm) * faceVelocityOverFaceVelMagSquared;
			}
			else
			{
				baseEddyViscosity += turbulence.LongMixCoeff * faceShearFluxCoeffWsTerm;
			}
		}
		if (baseEddyViscosity < 0.0)
		{
			throw new Exception("Encountered negative viscosity");
		}
		faceEddyVis[fIdx] = (float)baseEddyViscosity;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private double InterpolateCellToFace(int fIdx, double[] cellValue)
	{
		Face face = mesh.Faces[fIdx];
		int cellA = face.cellA;
		int cellB = face.cellB;
		if (cellB < 0)
		{
			return cellValue[cellA];
		}
		FaceAB faceAB = hmc.FaceAlpha[fIdx];
		return faceAB.ValueA * cellValue[cellA] + faceAB.ValueB * cellValue[cellB];
	}
}
