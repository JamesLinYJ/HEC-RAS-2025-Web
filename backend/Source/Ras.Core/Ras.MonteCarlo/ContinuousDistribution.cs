using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Xml.Linq;

namespace Ras.MonteCarlo;

public abstract class ContinuousDistribution
{
	public static readonly ReadOnlyCollection<string> DistributionNames = new ReadOnlyCollection<string>(new List<string>
	{
		TriangularDistribution.Name,
		NormalDistribution.Name,
		LogNormalDistribution.Name,
		UniformDistribution.Name
	});

	public float TruncateMin { get; set; }

	public float TruncateMax { get; set; }

	protected ContinuousDistribution()
	{
		TruncateMax = float.NaN;
		TruncateMin = float.NaN;
	}

	public abstract double Sample(double rand);

	public abstract XElement ToXElement();

	public abstract void LoadData(XElement el);

	public abstract void LoadDefault();
}
