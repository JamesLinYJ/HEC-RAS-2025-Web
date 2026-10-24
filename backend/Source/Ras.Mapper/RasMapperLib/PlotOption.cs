using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class PlotOption
{
	public string Name { get; }

	public string Description { get; }

	public bool IsDebugOption { get; }

	public PlotOption(string name, string desc = "", bool debugOption = false)
	{
		Name = name;
		if (string.IsNullOrEmpty(desc))
		{
			Description = name;
		}
		else
		{
			Description = desc;
		}
		IsDebugOption = debugOption;
	}

	public static implicit operator string(PlotOption po)
	{
		return po.Name;
	}

	public static bool operator ==(PlotOption po1, PlotOption po2)
	{
		if ((object)po1 == null && (object)po2 == null)
		{
			return true;
		}
		if ((object)po1 == null)
		{
			return false;
		}
		if ((object)po2 == null)
		{
			return false;
		}
		if (FuzzyMatches(po1.Name, po2.Name))
		{
			return true;
		}
		return false;
	}

	public static bool operator !=(PlotOption po1, PlotOption po2)
	{
		return !(po1 == po2);
	}

	public override bool Equals(object obj)
	{
		if (obj == null)
		{
			return false;
		}
		if (!(obj is PlotOption plotOption))
		{
			return false;
		}
		return this == plotOption;
	}

	public override int GetHashCode()
	{
		return Name.Trim().ToLowerInvariant().GetHashCode();
	}

	private static bool FuzzyMatches(string name1, string name2)
	{
		if (Operators.CompareString(name1, name2, TextCompare: false) == 0)
		{
			return true;
		}
		if (Operators.CompareString(name1.Trim().ToLowerInvariant(), name2.Trim().ToLowerInvariant(), TextCompare: false) == 0)
		{
			return true;
		}
		return false;
	}

	public override string ToString()
	{
		return Description.ToString();
	}
}
