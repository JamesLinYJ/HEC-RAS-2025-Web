using System;

namespace Ras.Engine.Structures.Culverts;

public class CulvertSolverConvergence
{
	public int Iterations { get; set; }

	public bool IsConverged { get; set; }

	public double KnownEnergyGrade { get; set; }

	public double ComputedEnergyGrade { get; set; }

	public double EnergyGradeError => Math.Abs(ComputedEnergyGrade - KnownEnergyGrade);

	public CulvertSolverConvergence()
	{
	}

	public CulvertSolverConvergence(int iterations, bool isConverged, double knownEnergyGrade, double computedEnergyGrade)
	{
		Iterations = iterations;
		IsConverged = isConverged;
		KnownEnergyGrade = knownEnergyGrade;
		ComputedEnergyGrade = computedEnergyGrade;
	}
}
