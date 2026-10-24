using System.Xml.Linq;

namespace Ras.MonteCarlo;

public class RRSTriple
{
	public Station Station { get; }

	public Reach Reach { get; }

	public River River { get; }

	public string Name => ToString();

	public RRSTriple(River riv, Reach rch, Station sta)
	{
		River = riv;
		Reach = rch;
		Station = sta;
	}

	public override string ToString()
	{
		return River.Name.Trim() + " - " + Reach.Name.Trim() + " - " + Station.Name.Trim();
	}

	public static RRSTriple FromXElement(XElement el)
	{
		River riv = River.FromXElement(el.Element(XMLTags.River));
		Reach rch = Reach.FromXElement(el.Element(XMLTags.Reach));
		Station sta = Station.FromXElement(el.Element(XMLTags.Station));
		return new RRSTriple(riv, rch, sta);
	}

	public XElement ToXElement(string name)
	{
		XElement xElement = new XElement(name);
		xElement.Add(River.ToXElement());
		xElement.Add(Reach.ToXElement());
		xElement.Add(Station.ToXElement());
		return xElement;
	}

	public static bool operator ==(RRSTriple r1, RRSTriple r2)
	{
		if ((object)r1 == r2)
		{
			return true;
		}
		if ((object)r1 == null || (object)r2 == null)
		{
			return false;
		}
		return r1.Equals(r2);
	}

	public static bool operator !=(RRSTriple r1, RRSTriple r2)
	{
		return !(r1 == r2);
	}

	protected bool Equals(RRSTriple other)
	{
		if ((object)other == null)
		{
			return false;
		}
		if (object.Equals(River, other.River) && object.Equals(Reach, other.Reach))
		{
			return object.Equals(Station, other.Station);
		}
		return false;
	}

	public override bool Equals(object obj)
	{
		if (obj == null)
		{
			return false;
		}
		if (ReferenceEquals(this, obj))
		{
			return true;
		}
		if (obj.GetType() != GetType())
		{
			return false;
		}
		return Equals((RRSTriple)obj);
	}

	public override int GetHashCode()
	{
		return (((((River != null) ? River.GetHashCode() : 0) * 397) ^ ((Reach != null) ? Reach.GetHashCode() : 0)) * 397) ^ ((Station != null) ? Station.GetHashCode() : 0);
	}
}
