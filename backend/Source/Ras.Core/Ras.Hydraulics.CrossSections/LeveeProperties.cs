using System.Diagnostics;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Hydraulics.CrossSections;

[DebuggerDisplay("{ToString()}")]
public class LeveeProperties : IDataHash
{
	[HashContributor]
	[Metadata("Left Levee Sta", Optional = true)]
	public double LeftStation { get; set; }

	[HashContributor]
	[Metadata("Right Levee Sta", Optional = true)]
	public double RightStation { get; set; }

	[HashContributor]
	[Metadata("Left Levee Elev", Optional = true)]
	public double LeftElevation { get; set; }

	[HashContributor]
	[Metadata("Right Levee Elev", Optional = true)]
	public double RightElevation { get; set; }

	public bool HasLeftLevee => !double.IsNaN(LeftStation);

	public bool HasRightLevee => !double.IsNaN(RightStation);

	public bool EqualsPiecewise(LeveeProperties other)
	{
		if (LeftStation != other.LeftStation && (!double.IsNaN(LeftStation) || !double.IsNaN(other.LeftStation)))
		{
			return false;
		}
		if (RightStation != other.RightStation && (!double.IsNaN(RightStation) || !double.IsNaN(other.RightStation)))
		{
			return false;
		}
		if (LeftElevation != other.LeftElevation && (!double.IsNaN(LeftElevation) || !double.IsNaN(other.LeftElevation)))
		{
			return false;
		}
		if (RightElevation != other.RightElevation && (!double.IsNaN(RightElevation) || !double.IsNaN(other.RightElevation)))
		{
			return false;
		}
		return true;
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}

	public int GetVersionHash2()
	{
		return Hash.Combine(LeftElevation, LeftStation, RightElevation, RightStation);
	}

	public override string ToString()
	{
		return "(" + LeftStation + ", " + LeftElevation + ") to (" + RightStation + ", " + RightElevation + ")";
	}
}
