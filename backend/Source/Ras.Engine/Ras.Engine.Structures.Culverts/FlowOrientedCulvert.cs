namespace Ras.Engine.Structures.Culverts;

public class FlowOrientedCulvert : ICulvertGeometry
{
	private readonly CulvertPhysProps _culvert;

	public bool Reversed { get; set; }

	public double InvertUS
	{
		get
		{
			if (!Reversed)
			{
				return _culvert.InvertUS;
			}
			return _culvert.InvertDS;
		}
	}

	public double InvertDS
	{
		get
		{
			if (!Reversed)
			{
				return _culvert.InvertDS;
			}
			return _culvert.InvertUS;
		}
	}

	public CulvertInletCoeff InletCoefUS
	{
		get
		{
			if (!Reversed)
			{
				return _culvert.InletCoefUS;
			}
			return _culvert.InletCoefDS;
		}
	}

	public CulvertInletCoeff InletCoefDS
	{
		get
		{
			if (!Reversed)
			{
				return _culvert.InletCoefDS;
			}
			return _culvert.InletCoefUS;
		}
	}

	public double CoefEntranceLoss
	{
		get
		{
			if (!Reversed)
			{
				return _culvert.CoefEntranceLoss;
			}
			return _culvert.CoefEntranceLossReverse;
		}
	}

	public double CoefExitLoss
	{
		get
		{
			if (!Reversed)
			{
				return _culvert.CoefExitLoss;
			}
			return _culvert.CoefExitLossReverse;
		}
	}

	public double Slope
	{
		get
		{
			if (!Reversed)
			{
				return _culvert.Slope;
			}
			return 0.0 - _culvert.Slope;
		}
	}

	public CulvertShape Shape => _culvert.Shape;

	public double FullRise => _culvert.FullRise;

	public double Span => _culvert.Span;

	public double Length => _culvert.Length;

	public double Mann => _culvert.Mann;

	public double MannBottom => _culvert.MannBottom;

	public bool IsMannBottom => _culvert.IsMannBottom;

	public double DepthMannBottom => _culvert.DepthMannBottom;

	public double DepthBlocked => _culvert.DepthBlocked;

	public bool IsPartiallyBlocked => _culvert.IsPartiallyBlocked;

	public double EffectiveRise => _culvert.EffectiveRise;

	public FlowOrientedCulvert(CulvertPhysProps culvert, bool reversed)
	{
		_culvert = culvert;
		Reversed = reversed;
	}
}
