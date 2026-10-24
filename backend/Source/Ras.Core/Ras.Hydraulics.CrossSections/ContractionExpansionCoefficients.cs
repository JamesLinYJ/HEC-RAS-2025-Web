using System.Diagnostics;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Hydraulics.CrossSections;

[DebuggerDisplay("{ToString()}")]
public class ContractionExpansionCoefficients : IDataHash
{
	[HashContributor]
	[Metadata("Contr")]
	public double ContractionCoefficient { get; set; }

	[HashContributor]
	[Metadata("Expan")]
	public double ExpansionCoefficient { get; set; }

	[HashContributor]
	[Metadata("Contr (USF)", Optional = true)]
	public double USFContractionCoefficient { get; set; }

	[HashContributor]
	[Metadata("Expan (USF)", Optional = true)]
	public double USFExpansionCoefficient { get; set; }

	public bool EqualsPiecewise(ContractionExpansionCoefficients other)
	{
		if (ContractionCoefficient == other.ContractionCoefficient && ExpansionCoefficient == other.ExpansionCoefficient && USFContractionCoefficient == other.USFContractionCoefficient)
		{
			return USFExpansionCoefficient == other.USFExpansionCoefficient;
		}
		return false;
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}

	public override string ToString()
	{
		return "SF: " + ContractionCoefficient + "/" + ExpansionCoefficient + ", USF: " + USFContractionCoefficient + "/" + USFExpansionCoefficient;
	}
}
