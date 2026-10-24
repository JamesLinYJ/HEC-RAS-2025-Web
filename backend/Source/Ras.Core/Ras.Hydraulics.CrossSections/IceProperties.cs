using Utility.Hashing;
using Utility.ORM;

namespace Ras.Hydraulics.CrossSections;

public class IceProperties : IDataHash
{
	[HashContributor]
	[Metadata("Ice Thick L")]
	public double ThicknessLeftBank { get; set; }

	[HashContributor]
	[Metadata("Ice Thick C")]
	public double ThicknessChannel { get; set; }

	[HashContributor]
	[Metadata("Ice Thick R")]
	public double ThicknessRightBank { get; set; }

	[HashContributor]
	[Metadata("Ice Mann L")]
	public double ManningsLeftBank { get; set; }

	[HashContributor]
	[Metadata("Ice Mann C")]
	public double ManningsChannel { get; set; }

	[HashContributor]
	[Metadata("Ice Mann R")]
	public double ManningsRightBank { get; set; }

	[HashContributor]
	[Metadata("Ice Jam Chan")]
	public bool JamChannel { get; set; }

	[HashContributor]
	[Metadata("Ice Jam OB")]
	public bool JamOverbanks { get; set; }

	[HashContributor]
	[Metadata("Ice Fix Mann")]
	public bool FixMannings { get; set; }

	[HashContributor]
	[Metadata("Ice SG")]
	public double SpecificGravity { get; set; }

	[HashContributor]
	[Metadata("Ice FA")]
	public double FreeAssociation { get; set; }

	[HashContributor]
	[Metadata("Ice Porosity")]
	public double Porosity { get; set; }

	[HashContributor]
	[Metadata("Ice K1")]
	public double K1 { get; set; }

	[HashContributor]
	[Metadata("Ice MaxVel")]
	public double MaxVelocity { get; set; }

	[HashContributor]
	[Metadata("Ice Cohesion")]
	public double Cohesion { get; set; }

	public bool IsDefault()
	{
		if (ThicknessLeftBank == 0.0 && ThicknessChannel == 0.0 && ThicknessRightBank == 0.0 && ManningsLeftBank == 0.0 && ManningsChannel == 0.0 && ManningsRightBank == 0.0 && !JamChannel && !JamOverbanks && !FixMannings && SpecificGravity == 0.0 && FreeAssociation == 0.0 && Porosity == 0.0 && K1 == 0.0 && MaxVelocity == 0.0)
		{
			return Cohesion == 0.0;
		}
		return false;
	}

	public bool EqualsPiecewise(IceProperties other)
	{
		if (ThicknessLeftBank == other.ThicknessLeftBank && ThicknessChannel == other.ThicknessChannel && ThicknessRightBank == other.ThicknessRightBank && ManningsLeftBank == other.ManningsLeftBank && ManningsChannel == other.ManningsChannel && ManningsRightBank == other.ManningsRightBank && JamChannel == other.JamChannel && JamOverbanks == other.JamOverbanks && FixMannings == other.FixMannings && SpecificGravity == other.SpecificGravity && FreeAssociation == other.FreeAssociation && Porosity == other.Porosity && K1 == other.K1 && MaxVelocity == other.MaxVelocity)
		{
			return Cohesion == other.Cohesion;
		}
		return false;
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
