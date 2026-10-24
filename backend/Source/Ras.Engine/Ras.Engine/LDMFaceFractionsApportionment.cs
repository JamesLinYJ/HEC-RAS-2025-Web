using System;
using Geospatial.Hydraulics;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Progress;
using Utility.Units;

namespace Ras.Engine;

internal sealed class LDMFaceFractionsApportionment : IFaceApportionment
{
	internal const int MaximumBoundaryCellCount = 16384;

	internal const double AbsoluteFractionTolerance = 0.005;

	private static readonly ILogger _logger = LogHelper.GetLogger("LDMFaceFractionsApportionment");

	private readonly MeshPropertyTables _propertyTables;

	private readonly UnitSystem _units;

	private readonly double _absTolEta;

	private readonly double _relKTol;

	private readonly float _longitudinalMixingCoefficient;

	private readonly float _lateralMixingCoefficient;

	private IFaceApportionment _active;

	private BoundaryCondition _boundaryCondition;

	internal bool UsedFallback { get; private set; }

	internal float LongitudinalMixingCoefficient => _longitudinalMixingCoefficient;

	internal float LateralMixingCoefficient => _lateralMixingCoefficient;

	private IFaceApportionment Active => _active ?? throw new InvalidOperationException("LDM face apportionment is not prepared.");

	internal LDMFaceFractionsApportionment(MeshPropertyTables propertyTables, UnitSystem units, double absTolEta, double relKTol)
		: this(propertyTables, units, absTolEta, relKTol, float.NaN, float.NaN)
	{
	}

	internal LDMFaceFractionsApportionment(MeshPropertyTables propertyTables, UnitSystem units, double absTolEta, double relKTol, TurbulenceParams turbulence)
		: this(propertyTables, units, absTolEta, relKTol, (float)turbulence.LongMixCoeff, (float)turbulence.TransMixCoeff)
	{
	}

	private LDMFaceFractionsApportionment(MeshPropertyTables propertyTables, UnitSystem units, double absTolEta, double relKTol, float longitudinalMixingCoefficient, float lateralMixingCoefficient)
	{
		_propertyTables = propertyTables ?? throw new ArgumentNullException("propertyTables");
		_units = units;
		_absTolEta = absTolEta;
		_relKTol = relKTol;
		_longitudinalMixingCoefficient = longitudinalMixingCoefficient;
		_lateralMixingCoefficient = lateralMixingCoefficient;
	}

	public double[] GetFaceFractions(Mesh mesh, MeshWaterState ws, double targetConveyance, double[] result = null)
	{
		return Active.GetFaceFractions(mesh, ws, targetConveyance, result);
	}

	public double[] GetFaceFractionsByStage(Mesh mesh, MeshWaterState ws, double stage, double[] result = null)
	{
		return Active.GetFaceFractionsByStage(mesh, ws, stage, result);
	}

	public void Prepare(ProgressReporter pr, BoundaryCondition bc, Mesh mesh, MeshWaterState ws, Func<MeshWaterState, int, double> faceConveyanceFunc)
	{
		UsedFallback = false;
		_active = null;
		ArgumentNullException.ThrowIfNull(bc, "bc");
		ArgumentNullException.ThrowIfNull(mesh, "mesh");
		ArgumentNullException.ThrowIfNull(ws, "ws");
		if (bc.Faces == null || bc.Faces.Length == 0)
		{
			throw new ArgumentException("LDM face apportionment requires at least one boundary face.", "bc");
		}
		if (bc.Faces.Length > 16384)
		{
			PrepareFallback(pr, bc, mesh, ws, faceConveyanceFunc, $"face count {bc.Faces.Length} exceeds the {16384} cell limit");
			return;
		}
		if (!TryValidateBoundaryNodeBedElevations(bc, out var error))
		{
			PrepareFallback(pr, bc, mesh, ws, faceConveyanceFunc, error);
			return;
		}
		_boundaryCondition = bc;
		FaceFractionsApportionment faceFractionsApportionment = new FaceFractionsApportionment(_propertyTables, _absTolEta, _relKTol, 0.005);
		LDMSolver solver = new LDMSolver(bc.Faces.Length);
		LDMControl control = CreateControl(saveVelocityDistribution: false);
		float[] widths = new float[bc.Faces.Length];
		float[] areas = new float[bc.Faces.Length];
		float[] wettedPerimeters = new float[bc.Faces.Length];
		float[] mannings = new float[bc.Faces.Length];
		float[] conveyances = new float[bc.Faces.Length];
		faceFractionsApportionment.SetFacesConveyanceFunc((MeshWaterState state, double eta) => ComputeFaceConveyances(bc, mesh, state, eta, solver, control, widths, areas, wettedPerimeters, mannings, conveyances));
		try
		{
			faceFractionsApportionment.Prepare(pr, bc, mesh, ws, null);
			_active = faceFractionsApportionment;
		}
		catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is ArithmeticException)
		{
			PrepareFallback(pr, bc, mesh, ws, faceConveyanceFunc, ex.Message);
		}
	}

	internal LDMResult SolveDiagnostic(Mesh mesh, double eta, string name)
	{
		if (_active == null)
		{
			throw new InvalidOperationException("LDM face apportionment is not prepared.");
		}
		if (UsedFallback)
		{
			throw new InvalidOperationException("A diagnostic LDM solution is unavailable after fallback.");
		}
		BoundaryCondition boundaryCondition = _boundaryCondition;
		float eta2 = (float)eta;
		if (!float.IsFinite(eta2))
		{
			throw new ArgumentOutOfRangeException("eta", "Stage must be finite.");
		}
		int faceLength = boundaryCondition.Faces.Length;
		float[] floatValues = new float[faceLength];
		float[] floatValuesArray = new float[faceLength];
		float[] floatValues2 = new float[faceLength];
		float[] floatValues3 = new float[faceLength];
		PopulateHydraulicCells(boundaryCondition, mesh, eta, floatValues, floatValuesArray, floatValues2, floatValues3);
		return new LDMSolver(faceLength).SolveHydraulicCells(control: CreateControl(saveVelocityDistribution: true), cellWidths: floatValues, cellAreas: floatValuesArray, cellWettedPerimeters: floatValues2, cellMannings: floatValues3, faceBedElevations: boundaryCondition.BoundaryNodeBedElevations, waterSurfaceElevation: eta2, name: name);
	}

	private double[] ComputeFaceConveyances(BoundaryCondition bc, Mesh mesh, MeshWaterState ws, double eta, LDMSolver solver, LDMControl control, float[] widths, float[] areas, float[] wettedPerimeters, float[] mannings, float[] conveyances)
	{
		ws.UpdateFacesProperties(bc.Faces, eta);
		PopulateHydraulicCells(bc, mesh, eta, widths, areas, wettedPerimeters, mannings);
		float eta2 = (float)eta;
		if (!float.IsFinite(eta2))
		{
			throw new InvalidOperationException($"Stage {eta:G17} cannot be represented as a float.");
		}
		if (!solver.TryComputeCellConveyances(widths, areas, wettedPerimeters, mannings, bc.BoundaryNodeBedElevations, eta2, control, conveyances, out var diagnostics))
		{
			throw new InvalidOperationException($"LDM did not converge at stage {eta:G17} after {diagnostics.Iterations} iterations (velocity error {diagnostics.FinalVelocityError:G9}).");
		}
		double[] faceConveyances = new double[conveyances.Length];
		for (int i = 0; i < conveyances.Length; i++)
		{
			faceConveyances[i] = conveyances[i];
		}
		return faceConveyances;
	}

	private void PopulateHydraulicCells(BoundaryCondition bc, Mesh mesh, double eta, float[] widths, float[] areas, float[] wettedPerimeters, float[] mannings)
	{
		for (int i = 0; i < bc.Faces.Length; i++)
		{
			int faceIndex = bc.Faces[i];
			if (!_propertyTables.TryGetFaceSectionPropertiesAtWaterSurface(faceIndex, eta, out var area, out var wettedPerimeter, out var manning))
			{
				throw new InvalidOperationException($"Face {faceIndex} has invalid hydraulic properties at stage {eta:G17}.");
			}
			double faceSimpleLength = mesh.MetadataCache.FaceSimpleLengths[faceIndex];
			widths[i] = ConvertFiniteFloat(faceSimpleLength, faceIndex, "width");
			areas[i] = ConvertFiniteFloat(area, faceIndex, "area");
			wettedPerimeters[i] = ConvertFiniteFloat(wettedPerimeter, faceIndex, "wettedPerimeter");
			mannings[i] = ConvertFiniteFloat(manning, faceIndex, "manning");
		}
	}

	private LDMControl CreateControl(bool saveVelocityDistribution)
	{
		return new LDMControl(_units)
		{
			ComputeAlpha = false,
			ComputeBeta = false,
			ComputeDispersion = false,
			SaveVelocityDistribution = saveVelocityDistribution,
			LongitudinalMixingCoefficient = _longitudinalMixingCoefficient,
			LateralMixingCoefficient = _lateralMixingCoefficient
		};
	}

	private static bool TryValidateBoundaryNodeBedElevations(BoundaryCondition bc, out string error)
	{
		float[] boundaryNodeBedElevations = bc.BoundaryNodeBedElevations;
		if (boundaryNodeBedElevations == null)
		{
			error = "boundary-node bed elevations are unavailable";
			return false;
		}
		if (boundaryNodeBedElevations.Length != bc.Faces.Length + 1)
		{
			error = $"boundary-node elevation count {boundaryNodeBedElevations.Length} does not match the required count {bc.Faces.Length + 1}";
			return false;
		}
		for (int i = 0; i < boundaryNodeBedElevations.Length; i++)
		{
			float boundaryNodeBedElevation = boundaryNodeBedElevations[i];
			if (!float.IsFinite(boundaryNodeBedElevation) || boundaryNodeBedElevation == -9999f)
			{
				error = $"boundary node {i} has an invalid terrain elevation";
				return false;
			}
		}
		error = "";
		return true;
	}

	private static float ConvertFiniteFloat(double computed, int faceIndex, string propertyName)
	{
		float computed2 = (float)computed;
		if (!float.IsFinite(computed2))
		{
			throw new InvalidOperationException($"Face {faceIndex} has nonfinite {propertyName} hydraulic data.");
		}
		return computed2;
	}

	private void PrepareFallback(ProgressReporter pr, BoundaryCondition bc, Mesh mesh, MeshWaterState ws, Func<MeshWaterState, int, double> faceConveyanceFunc, string reason)
	{
		UsedFallback = true;
		string isNullOrWhiteSpaceBcTerm = (string.IsNullOrWhiteSpace(bc.Name) ? "unnamed boundary" : bc.Name);
		string message = $"LDM flow distribution for '{isNullOrWhiteSpaceBcTerm}' fell back to the divided channel method: {reason}.";
		if (pr != null && pr.IsMutable)
		{
			pr.ReportWarning(message);
		}
		else
		{
			_logger.LogWarning("{Warning}", message);
		}
		FaceFractionsApportionment faceFractionsApportionment = new FaceFractionsApportionment(_propertyTables, _absTolEta, _relKTol);
		faceFractionsApportionment.Prepare(pr, bc, mesh, ws, faceConveyanceFunc);
		_active = faceFractionsApportionment;
	}
}
