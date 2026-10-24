using System.Xml.Linq;

namespace Ras.MonteCarlo;

public class Station
{
	public string Name { get; set; }

	public int Index { get; }

	public Station(string name, int index)
	{
		Index = index;
		Name = name;
	}

	public static Station FromXElement(XElement el)
	{
		string? name = (string?)el.Element(XMLTags.Name);
		int index = (int)el.Element(XMLTags.Index);
		return new Station(name, index);
	}

	public XElement ToXElement()
	{
		XElement xElement = new XElement(XMLTags.Station);
		xElement.Add(new XElement(XMLTags.Name, Name));
		xElement.Add(new XElement(XMLTags.Index, Index));
		return xElement;
	}

	protected bool Equals(Station other)
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

	public static bool operator ==(Station s1, Station s2)
	{
		if ((object)s1 == s2)
		{
			return true;
		}
		return s1?.Equals(s2) ?? false;
	}

	public static bool operator !=(Station s1, Station s2)
	{
		return !(s1 == s2);
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
		return Equals((Station)obj);
	}

	public override int GetHashCode()
	{
		return ((Name?.GetHashCode() ?? 0) * 397) ^ Index;
	}
}
