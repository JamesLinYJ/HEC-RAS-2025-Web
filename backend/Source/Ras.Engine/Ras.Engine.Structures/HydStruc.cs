using System;
using System.Linq;

namespace Ras.Engine.Structures;

public class HydStruc
{
	public class HsWeir
	{
		public double[] Sta { get; set; }

		public double[] Elev { get; set; }

		public double[] StaA { get; set; }

		public double[] StaC { get; set; }

		public double[] SegSta { get; set; }

		public double[] QSeg { get; set; }

		public double QTotal { get; set; }

		public double CoefQ { get; set; }

		public void InitStaElev()
		{
			Sta = new double[7];
			Sta[0] = 0.0;
			Sta[1] = 157.1;
			Sta[2] = 300.0;
			Sta[3] = 316.0;
			Sta[4] = 320.0;
			Sta[5] = 474.9;
			Sta[6] = 636.1;
			Elev = new double[7];
			Elev[0] = 600.0;
			Elev[1] = 589.5;
			Elev[2] = 580.0;
			Elev[3] = 580.0;
			Elev[4] = 580.0;
			Elev[5] = 589.9;
			Elev[6] = 600.0;
			StaA = new double[6];
			StaA[0] = -0.066666797;
			StaA[1] = -0.066666536;
			StaA[2] = 0.0;
			StaA[3] = 0.0;
			StaA[4] = 0.06373705;
			StaA[5] = 0.06373705;
			StaC = new double[7];
			StaC[0] = 600.0;
			StaC[1] = 599.9999;
			StaC[2] = 580.0;
			StaC[3] = 580.0;
			StaC[4] = 559.6041;
			StaC[5] = 559.6043;
			SegSta = new double[5];
			SegSta[0] = 0.0;
			SegSta[1] = 157.1;
			SegSta[2] = 316.0;
			SegSta[3] = 474.9;
			SegSta[4] = 636.1;
			QSeg = new double[4];
			CoefQ = 2.0;
		}
	}

	private class HydWeirVar
	{
		private bool _isPosFlow;

		private int _nseStart;

		private double _x1Seg;

		private double _aUS;

		private double _cUS;

		private double _aDS;

		private double _cDS;

		private double _x2Seg;

		private int _nSeg;

		private double _x1Sta;

		private double _x2Sta;

		private double _wsel1US;

		private double _wsel2US;

		private double _wsel1DS;

		private double _wsel2DS;

		private double _weir1;

		private double _weir2;

		private double _coefQ;

		private double _coefSub;

		private double _coefHead;

		private bool _isVert;

		private double _qSta;

		private double _wettedPerimter;

		public HydWeirVar HydWeirVarCheckReverseFlow(HsWeirProp weirHW, HsWeirProp weirTW, int i)
		{
			_x1Seg = weirHW.SegSta[i];
			_x2Seg = weirHW.SegSta[i + 1];
			_nSeg = i;
			double segA = weirHW.SegA[i];
			double segC = weirHW.SegC[i];
			double segAX1SegTerm = segA * _x1Seg + segC;
			double segAX2SegTerm = segA * _x2Seg + segC;
			double computed = 0.5 * (segAX1SegTerm + segAX2SegTerm);
			double segA2 = weirTW.SegA[i];
			double segC2 = weirTW.SegC[i];
			double segA2X1SegTerm = segA2 * _x1Seg + segC2;
			double segA2X2SegTerm = segA2 * _x2Seg + segC2;
			double computed2 = 0.5 * (segA2X1SegTerm + segA2X2SegTerm);
			if (computed >= computed2)
			{
				_isPosFlow = true;
				_aUS = segA;
				_cUS = segC;
				_aDS = segA2;
				_cDS = segC2;
			}
			else
			{
				_isPosFlow = false;
				_aUS = segA2;
				_cUS = segC2;
				_aDS = segA;
				_cDS = segC;
			}
			return this;
		}

		public HydWeirVar ComputeWeirFlowSeg(HsWeir hsWeir)
		{
			for (int i = _nseStart; i < hsWeir.Sta.Length - 1; i++)
			{
				_x1Sta = hsWeir.Sta[i];
				_x2Sta = hsWeir.Sta[i + 1];
				_weir1 = hsWeir.Elev[i];
				_weir2 = hsWeir.Elev[i + 1];
				_wsel1US = _aUS * _x1Sta + _cUS;
				_wsel2US = _aUS * _x2Sta + _cUS;
				_wsel1DS = _aDS * _x1Sta + _cDS;
				_wsel2DS = _aDS * _x2Sta + _cDS;
				if (_wsel1US > _weir1 || _wsel2US > _weir2)
				{
					ComputeWeirFlowStaCheckWeir();
					if (_isVert)
					{
						break;
					}
					_coefQ = hsWeir.CoefQ;
					_coefSub = 1.0;
					_coefHead = 1.0;
					ComputeWeirFlowStaQ(hsWeir, i);
					ComputeWeirFlowSegSum(hsWeir);
				}
				if (i >= hsWeir.Sta.Length - 2)
				{
					break;
				}
				if (hsWeir.Sta[i + 2] > _x2Seg)
				{
					_nseStart = i + 1;
					break;
				}
			}
			return this;
		}

		public HydWeirVar ComputeWeirFlowStaCheckWeir()
		{
			if (_x2Sta > _x1Sta)
			{
				_isVert = false;
				if (_wsel1US < _weir1 || _wsel2US < _weir2)
				{
					var (localValue, localValue2) = LineIntersectFromPoints(_x1Sta, _wsel1US, _x2Sta, _wsel2US, _x1Sta, _weir1, _x2Sta, _weir2);
					if (_wsel1US < _weir1)
					{
						_x1Sta = localValue;
						_wsel1US = localValue2;
					}
					else
					{
						_x2Sta = localValue;
						_wsel2US = localValue2;
					}
				}
			}
			else
			{
				_isVert = true;
				double wsel1USWsel2USTerm = Math.Max(_wsel1US, _wsel2US);
				double computed = Math.Max(_weir1, _weir2);
				computed = Math.Min(computed, wsel1USWsel2USTerm);
				double weir1Weir2Term = Math.Min(_weir1, _weir2);
				_wettedPerimter = computed - weir1Weir2Term;
			}
			return this;
		}

		public HydWeirVar ComputeWeirFlowStaQ(HsWeir hsWeir, int i)
		{
			_qSta = 0.0;
			if (IsNear(_aUS, hsWeir.StaA[i], 1E-05))
			{
				double wsel1USAboveWeir1 = _wsel1US - _weir1;
				if (wsel1USAboveWeir1 > 0.0)
				{
					_qSta = _coefQ * (_x2Sta - _x1Sta) * Math.Pow(wsel1USAboveWeir1, 1.5);
				}
			}
			else
			{
				double aUSStaATerm = _aUS - hsWeir.StaA[i];
				double cUSStaCTerm = _cUS - hsWeir.StaC[i];
				double x = 0.0;
				if (!IsNear(_wsel1US, _weir1, 1E-05))
				{
					x = aUSStaATerm * _x1Sta + cUSStaCTerm;
					x = Math.Max(x, 0.0);
				}
				double x2 = 0.0;
				if (!IsNear(_wsel2US, _weir2, 1E-05))
				{
					x2 = aUSStaATerm * _x2Sta + cUSStaCTerm;
					x2 = Math.Max(x2, 0.0);
				}
				double x2Term = Math.Pow(x2, 2.5) - Math.Pow(x, 2.5);
				if (aUSStaATerm != 0.0)
				{
					_qSta = 0.4 * _coefQ * x2Term / aUSStaATerm;
				}
				else
				{
					_qSta = 0.0;
				}
			}
			return this;
		}

		public HydWeirVar ComputeWeirFlowSegSum(HsWeir hsWeir)
		{
			_qSta = _qSta * _coefSub * _coefHead;
			if (_isPosFlow)
			{
				hsWeir.QSeg[_nSeg] += _qSta;
			}
			else
			{
				hsWeir.QSeg[_nSeg] += 0.0 - _qSta;
			}
			return this;
		}
	}

	public class HsWeirProp
	{
		public double[] WselSegSta { get; set; }

		public double[] SegSta { get; set; }

		public double[] QSeg { get; set; }

		public double[] SegA { get; set; }

		public double[] SegC { get; set; }

		public void InitSta()
		{
			WselSegSta = new double[5];
			SegSta = new double[5];
			SegSta[0] = 0.0;
			SegSta[1] = 157.1;
			SegSta[2] = 316.0;
			SegSta[3] = 474.9;
			SegSta[4] = 636.1;
			WselSegSta = new double[5];
			SegA = new double[4];
			SegC = new double[4];
			QSeg = new double[4];
		}
	}

	public HsWeir hsWeir { get; set; } = new HsWeir();

	public HsWeirProp weirHW { get; set; } = new HsWeirProp();

	public HsWeirProp weirTW { get; set; } = new HsWeirProp();

	public string Name { get; set; }

	public HydStruc(string name)
	{
		Name = name;
	}

	public void ComputeWeirFlow(HydStruc hydStruc)
	{
		Array.Fill(hydStruc.hsWeir.QSeg, 0.0);
		Array.Fill(hydStruc.weirHW.QSeg, 0.0);
		Array.Fill(hydStruc.weirTW.QSeg, 0.0);
		HydWeirVar hydWeirVar = new HydWeirVar();
		Array.Fill(hydStruc.weirHW.SegA, 0.0);
		Array.Fill(hydStruc.weirHW.SegC, 582.0);
		Array.Fill(hydStruc.weirTW.SegA, 0.0);
		Array.Fill(hydStruc.weirTW.SegC, 0.0);
		for (int i = 0; i < hydStruc.hsWeir.SegSta.Length - 1; i++)
		{
			hydWeirVar.HydWeirVarCheckReverseFlow(hydStruc.weirHW, hydStruc.weirTW, i);
			hydWeirVar.ComputeWeirFlowSeg(hydStruc.hsWeir);
		}
		hsWeir.QTotal = hsWeir.QSeg.Sum();
	}

	public static (double x, double y) LineIntersectFromPoints(double ax1Org, double ay1Org, double ax2Org, double ay2Org, double bx1Org, double by1Org, double bx2Org, double by2Org)
	{
		double computed = 0.0;
		double computed2 = 0.0;
		double ax2OrgAboveAx1Org = ax2Org - ax1Org;
		double ay2OrgAboveAy1Org = ay2Org - ay1Org;
		double bx1OrgAboveAx1Org = bx1Org - ax1Org;
		double by1OrgAboveAy1Org = by1Org - ay1Org;
		double bx2OrgAboveAx1Org = bx2Org - ax1Org;
		double by2OrgAboveAy1Org = by2Org - ay1Org;
		double computed3 = computed2 - ay2OrgAboveAy1Org;
		double computed4 = ax2OrgAboveAx1Org - computed;
		double computed5 = computed4 * computed2 + computed3 * computed;
		double by1OrgAboveAy1OrgAboveBy2OrgAboveAy1Org = by1OrgAboveAy1Org - by2OrgAboveAy1Org;
		double bx2OrgAboveAx1OrgAboveBx1OrgAboveAx1Org = bx2OrgAboveAx1Org - bx1OrgAboveAx1Org;
		double computed6 = bx2OrgAboveAx1OrgAboveBx1OrgAboveAx1Org * by1OrgAboveAy1Org + by1OrgAboveAy1OrgAboveBy2OrgAboveAy1Org * bx1OrgAboveAx1Org;
		double computed7 = computed3 * bx2OrgAboveAx1OrgAboveBx1OrgAboveAx1Org - computed4 * by1OrgAboveAy1OrgAboveBy2OrgAboveAy1Org;
		double computed8 = 0.0;
		double computed9 = 0.0;
		if (computed7 != 0.0)
		{
			computed8 = (computed5 * bx2OrgAboveAx1OrgAboveBx1OrgAboveAx1Org - computed4 * computed6) / computed7;
			computed9 = (computed3 * computed6 - computed5 * by1OrgAboveAy1OrgAboveBy2OrgAboveAy1Org) / computed7;
		}
		computed8 += ax1Org;
		computed9 += ay1Org;
		if (ax1Org == ax2Org)
		{
			computed8 = ax1Org;
		}
		if (bx1Org == bx2Org)
		{
			computed8 = bx1Org;
		}
		return (x: computed8, y: computed9);
	}

	public static bool IsNear(double doubleValue, double doubleValue2, double acceptableDifference)
	{
		return Math.Abs(doubleValue - doubleValue2) <= acceptableDifference;
	}
}
