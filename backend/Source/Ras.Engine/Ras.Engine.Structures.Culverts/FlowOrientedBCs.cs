namespace Ras.Engine.Structures.Culverts;

public class FlowOrientedBCs
{
	private readonly CulvertBoundaryConditions _bcs;

	public bool Reversed { get; }

	public double UpstreamWse
	{
		get
		{
			if (!Reversed)
			{
				return _bcs.UpstreamWse;
			}
			return _bcs.DownstreamWse;
		}
	}

	public double DownstreamWse
	{
		get
		{
			if (!Reversed)
			{
				return _bcs.DownstreamWse;
			}
			return _bcs.UpstreamWse;
		}
	}

	public double UpstreamVelocityHead
	{
		get
		{
			if (!Reversed)
			{
				return _bcs.UpstreamVelocityHead;
			}
			return _bcs.DownstreamVelocityHead;
		}
	}

	public double DownstreamVelocityHead
	{
		get
		{
			if (!Reversed)
			{
				return _bcs.DownstreamVelocityHead;
			}
			return _bcs.UpstreamVelocityHead;
		}
	}

	public double UpstreamEnergyGrade => UpstreamWse + UpstreamVelocityHead;

	public double DownstreamEnergyGrade => DownstreamWse + DownstreamVelocityHead;

	public FlowOrientedBCs(CulvertBoundaryConditions bcs, bool reversed)
	{
		_bcs = bcs;
		Reversed = reversed;
	}
}
