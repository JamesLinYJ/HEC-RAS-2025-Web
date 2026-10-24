using System.Xml.Linq;

namespace Ras.MonteCarlo;

public class Reach
{
	private const string EmptyName = "N/A";

	public static readonly Reach NullReach = new Reach("N/A", -1);

	public string Name { get; private set; }

	public int Index { get; }

	public Reach(string name, int index)
	{
		Name = name;
		Index = index;
	}

	public static Reach FromXElement(XElement el)
	{
		string? name = (string?)el.Element(XMLTags.Name);
		int index = (int)el.Element(XMLTags.Index);
		return new Reach(name, index);
	}

	public XElement ToXElement()
	{
		XElement xElement = new XElement(XMLTags.Reach);
		xElement.Add(new XElement(XMLTags.Name, Name));
		xElement.Add(new XElement(XMLTags.Index, Index));
		return xElement;
	}

	protected bool Equals(Reach other)
	{
		if ((object)other == null)
		{
			return false;
		}
		if (string.Equals(Name.Trim(), other.Name.Trim()))
		{
			return Index == other.Index;
		}
		return false;
	}

	public static bool operator ==(Reach r1, Reach r2)
	{
		if ((object)r1 == r2)
		{
			return true;
		}
		return r1?.Equals(r2) ?? false;
	}

	public static bool operator !=(Reach r1, Reach r2)
	{
		return !(r1 == r2);
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
		return Equals((Reach)obj);
	}

	public override int GetHashCode()
	{
		return ((Name?.GetHashCode() ?? 0) * 397) ^ Index;
	}
}
