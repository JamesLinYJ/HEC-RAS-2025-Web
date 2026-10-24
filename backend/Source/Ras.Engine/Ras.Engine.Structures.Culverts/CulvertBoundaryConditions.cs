namespace Ras.Engine.Structures.Culverts;

public class CulvertBoundaryConditions
{
	public double UpstreamWse { get; set; }

	public double DownstreamWse { get; set; }

	public double UpstreamVelocityHead { get; set; }

	public double DownstreamVelocityHead { get; set; }

	public double UpstreamEnergyGrade => UpstreamWse + UpstreamVelocityHead;

	public double DownstreamEnergyGrade => DownstreamWse + DownstreamVelocityHead;

	public CulvertBoundaryConditions()
	{
	}

	public CulvertBoundaryConditions(double upstreamWse, double downstreamWse, double upstreamVelocityHead, double downstreamVelocityHead)
	{
		UpstreamWse = upstreamWse;
		DownstreamWse = downstreamWse;
		UpstreamVelocityHead = upstreamVelocityHead;
		DownstreamVelocityHead = downstreamVelocityHead;
	}

	public (double usVal, double dsVal, bool isReverse) GetEGorWSELForRatingTable()
	{
		bool upstreamEnergyGradeDownstreamEnergyGradeTerm = UpstreamEnergyGrade >= DownstreamEnergyGrade;
		double upstreamEnergyGrade = (upstreamEnergyGradeDownstreamEnergyGradeTerm ? UpstreamEnergyGrade : UpstreamWse);
		double downstreamWse = (upstreamEnergyGradeDownstreamEnergyGradeTerm ? DownstreamWse : DownstreamEnergyGrade);
		return (usVal: upstreamEnergyGrade, dsVal: downstreamWse, isReverse: !upstreamEnergyGradeDownstreamEnergyGradeTerm);
	}

	public FlowOrientedBCs AsFlowOriented(bool reversed)
	{
		return new FlowOrientedBCs(this, reversed);
	}
}
