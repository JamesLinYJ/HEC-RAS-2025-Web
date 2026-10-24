using System;
using Geospatial.Vectors;
using Ras.Engine;
using Ras.Layers;

namespace Ras.Synthetics;

public class DamBreak1DParams : BasicRectangleParams
{
	public double WaterSurfaceLeft;

	public double WaterSurfaceRight;

	private bool _ready;

	private double _g;

	private double _x0;

	private double _zb;

	private double _hL;

	private double _hR;

	private bool _isDry;

	private double _cm;

	private void EnsureSolution()
	{
		if (!_ready)
		{
			_g = SolverControl.GravSI;
			_x0 = 0.5 * (double)CellsWide * CellSize;
			_zb = TerrainElevation(new Point(_x0, 0.0));
			_hL = Math.Max(WaterSurfaceLeft - _zb, 0.0);
			_hR = Math.Max(WaterSurfaceRight - _zb, 0.0);
			if (_hR > _hL)
			{
				throw new ArgumentException("Stoker/Ritter dam break requires hL >= hR (left side is the reservoir).");
			}
			_isDry = _hR <= 1E-10;
			_cm = (_isDry ? 0.0 : SolveShockConstant(_g, _hL, _hR));
			_ready = true;
		}
	}

	private void Evaluate(double x, double t, out double h, out double u)
	{
		EnsureSolution();
		if (t <= 0.0)
		{
			h = ((x <= _x0) ? _hL : _hR);
			u = 0.0;
			return;
		}
		double hLTerm = Math.Sqrt(_g * _hL);
		double x0Term = (x - _x0) / t;
		if (x0Term <= 0.0 - hLTerm)
		{
			h = _hL;
			u = 0.0;
			return;
		}
		if (_isDry)
		{
			if (x0Term >= 2.0 * hLTerm)
			{
				h = 0.0;
				u = 0.0;
			}
			else
			{
				Rarefaction(hLTerm, x0Term, out h, out u);
			}
			return;
		}
		double cmTerm = 2.0 * hLTerm - 3.0 * _cm;
		double cmHRTerm = 2.0 * _cm * _cm * (hLTerm - _cm) / (_cm * _cm - _g * _hR);
		if (x0Term <= cmTerm)
		{
			Rarefaction(hLTerm, x0Term, out h, out u);
		}
		else if (x0Term < cmHRTerm)
		{
			h = _cm * _cm / _g;
			u = 2.0 * (hLTerm - _cm);
		}
		else
		{
			h = _hR;
			u = 0.0;
		}
	}

	private void Rarefaction(double cL, double xi, out double h, out double u)
	{
		u = 2.0 / 3.0 * (xi + cL);
		double cLXiTerm = (2.0 * cL - xi) / 3.0;
		h = Math.Max(cLXiTerm * cLXiTerm / _g, 0.0);
	}

	private static double SolveShockConstant(double g, double hL, double hR)
	{
		double computed = Math.Sqrt(g * hR);
		double computed2 = Math.Sqrt(g * hL);
		for (int i = 0; i < 200; i++)
		{
			double computed3 = 0.5 * (computed + computed2);
			if (ShockResidual(g, hL, hR, computed3) > 0.0)
			{
				computed2 = computed3;
			}
			else
			{
				computed = computed3;
			}
			if (computed2 - computed <= 1E-14 * computed2)
			{
				break;
			}
		}
		return 0.5 * (computed + computed2);
	}

	private static double ShockResidual(double g, double hL, double hR, double cm)
	{
		double hLTerm = Math.Sqrt(g * hL);
		double cmHRTerm = cm * cm - g * hR;
		return -8.0 * g * hR * cm * cm * (hLTerm - cm) * (hLTerm - cm) + cmHRTerm * cmHRTerm * (cm * cm + g * hR);
	}

	public static DamBreak1DParams WetTest()
	{
		return new DamBreak1DParams
		{
			CellsTall = 2,
			CellsWide = 1000,
			CellSize = 1.0,
			Slope = 0.0,
			NValue = 0f,
			WaterSurfaceLeft = 10.0,
			WaterSurfaceRight = 5.0,
			SolveDt = 0.05,
			SolveDuration = 60.0
		};
	}

	public static DamBreak1DParams DryTest()
	{
		return new DamBreak1DParams
		{
			CellsTall = 2,
			CellsWide = 1000,
			CellSize = 1.0,
			Slope = 0.0,
			NValue = 0f,
			WaterSurfaceLeft = 10.0,
			WaterSurfaceRight = 0.0,
			SolveDt = 0.05,
			SolveDuration = 60.0
		};
	}

	public override Func<Point, float> InitialStageFunction()
	{
		double center = 0.5 * (double)CellsWide * CellSize;
		return (Point pt) => (float)((pt.X < center) ? WaterSurfaceLeft : WaterSurfaceRight);
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double t) =>
		{
			Evaluate(pt.X, t, out var h, out var _);
			return (float)(h + _zb);
		};
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		return (Point pt, double t) =>
		{
			Evaluate(pt.X, t, out var _, out var u);
			return new Vector(u, 0.0);
		};
	}

	public override void InitPlanOptions(Plan pl)
	{
		pl.CheckMaxElevationChange = 5.0;
	}

	public override void InitSolverControl(SolverControl sc)
	{
		sc.checkMaxElevationChange = 5.0;
		sc.stepType = SolverControl.StepType.FWDBACKFWD;
	}
}
