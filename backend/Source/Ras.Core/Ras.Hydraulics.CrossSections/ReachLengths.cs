using System.Diagnostics;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Hydraulics.CrossSections;

[DebuggerDisplay("{Left} | {Channel} | {Right}")]
public class ReachLengths : IDataHash
{
	[HashContributor]
	[Metadata("Len Left")]
	public double Left { get; set; }

	[HashContributor]
	[Metadata("Len Channel")]
	public double Channel { get; set; }

	[HashContributor]
	[Metadata("Len Right")]
	public double Right { get; set; }

	public ReachLengths()
	{
	}

	public ReachLengths(double l, double c, double r)
	{
		Left = l;
		Channel = c;
		Right = r;
	}

	public bool EqualsPiecewise(ReachLengths other)
	{
		bool isNaNAndIsNaN = Left == other.Left || (double.IsNaN(Left) && double.IsNaN(other.Left));
		bool isNaNAndIsNaN2 = Channel == other.Channel || (double.IsNaN(Channel) && double.IsNaN(other.Channel));
		bool isNaNAndIsNaN3 = Right == other.Right || (double.IsNaN(Right) && double.IsNaN(other.Right));
		return isNaNAndIsNaN & isNaNAndIsNaN2 & isNaNAndIsNaN3;
	}

	public bool IsNaN()
	{
		if (double.IsNaN(Left) && double.IsNaN(Channel))
		{
			return double.IsNaN(Right);
		}
		return false;
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
