using System.Xml.Linq;

namespace Ras.MonteCarlo;

public class River
{
	private const string EmptyName = "N/A";

	public static readonly River NullRiver = new River("N/A", -1);

	public string Name { get; private set; }

	public int Index { get; }

	public River(string name, int index)
	{
		Index = index;
		Name = name;
	}

	public static River FromXElement(XElement el)
	{
		string? name = (string?)el.Element(XMLTags.Name);
		int index = (int)el.Element(XMLTags.Index);
		return new River(name, index);
	}

	public XElement ToXElement()
	{
		XElement xElement = new XElement(XMLTags.River);
		xElement.Add(new XElement(XMLTags.Name, Name));
		xElement.Add(new XElement(XMLTags.Index, Index));
		return xElement;
	}

	public static bool IsNull(River riv)
	{
		if (riv.Name == "N/A")
		{
			return true;
		}
		return false;
	}

	protected bool Equals(River other)
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

	public static bool operator ==(River r1, River r2)
	{
		if ((object)r1 == r2)
		{
			return true;
		}
		return r1?.Equals(r2) ?? false;
	}

	public static bool operator !=(River r1, River r2)
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
		return Equals((River)obj);
	}

	public override int GetHashCode()
	{
		return ((Name?.GetHashCode() ?? 0) * 397) ^ Index;
	}
}
