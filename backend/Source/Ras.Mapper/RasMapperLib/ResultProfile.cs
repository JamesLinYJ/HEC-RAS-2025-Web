using System.Linq;
using System.Xml;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class ResultProfile
{
	public const int NullProfileIndex = -1;

	public const string NullProfileName = "None";

	public const int MaxProfileIndex = int.MaxValue;

	public const string MaxProfileName = "Max";

	public const int MinProfileIndex = int.MinValue;

	public const string MinProfileName = "Min";

	private int _profileIndex;

	public RASResults Results { get; set; }

	public int ProfileIndex
	{
		get
		{
			return _profileIndex;
		}
		set
		{
			if (Results == null || Results.ProfileNames == null || value < Results.ProfileNames.Count() || value == int.MaxValue)
			{
				_profileIndex = value;
			}
		}
	}

	public string ProfileName
	{
		get
		{
			if (IsMinProfile)
			{
				return "Min";
			}
			if (IsMaxProfile)
			{
				return "Max";
			}
			if (IsNullProfile)
			{
				return "None";
			}
			if (Results == null || Results.ProfileNames == null)
			{
				return "None";
			}
			if (Results.ProfileNames.Count() <= _profileIndex)
			{
				return "None";
			}
			return Results.ProfileNames[_profileIndex];
		}
	}

	public bool IsMaxProfile => _profileIndex == int.MaxValue;

	public bool IsMinProfile => _profileIndex == int.MinValue;

	public bool IsProfile => !(IsMinProfile | IsMaxProfile | IsNullProfile);

	public bool IsNullProfile => _profileIndex == -1;

	public ResultProfile()
	{
	}

	public ResultProfile(RASResults res)
	{
		Results = res;
	}

	public void SetToMaxProfile()
	{
		ProfileIndex = int.MaxValue;
	}

	public void SetToMinProfile()
	{
		ProfileIndex = int.MinValue;
	}

	public void SetToNullProfile()
	{
		ProfileIndex = -1;
	}

	public void TrySetProfile(string profileName)
	{
		int profileIndex = Results.ClosestProfileIndex(profileName);
		if (profileIndex == int.MaxValue || profileIndex == int.MinValue)
		{
			ProfileIndex = profileIndex;
		}
		else if (profileIndex >= 0 && profileIndex < Results.ProfileNames.Count() && Operators.CompareString(Results.ProfileNames[profileIndex].ToLowerInvariant(), profileName.ToLowerInvariant(), TextCompare: false) == 0)
		{
			ProfileIndex = profileIndex;
		}
		else
		{
			ProfileIndex = -1;
		}
	}

	public void XMLSave(XmlElement parametersNode)
	{
		parametersNode.SetAttribute("ProfileIndex", ProfileIndex.ToString());
		parametersNode.SetAttribute("ProfileName", ProfileName);
	}

	public void XMLLoad(XmlElement parametersNode)
	{
		if (XML.BooleanAttribute(parametersNode, "MaxDepth"))
		{
			SetToMaxProfile();
			return;
		}
		if (XML.BooleanAttribute(parametersNode, "MinDepth"))
		{
			SetToMinProfile();
			return;
		}
		int profileIndex = XML.IntegerAttribute(parametersNode, "ProfileIndex", 0);
		string nameFromXML = XML.StringAttribute(parametersNode, "ProfileName");
		if (Results == null)
		{
			ProfileIndex = profileIndex;
		}
		else
		{
			ProfileIndex = Results.SelectBestProfileIndex(profileIndex, nameFromXML);
		}
	}
}
