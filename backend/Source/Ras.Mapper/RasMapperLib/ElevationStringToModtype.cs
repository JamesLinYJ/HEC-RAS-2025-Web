using System.Collections.Generic;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

[StandardModule]
public sealed class ElevationStringToModtype
{
	private static Dictionary<string, ElevationModificationType> modType;

	static ElevationStringToModtype()
	{
		modType = new Dictionary<string, ElevationModificationType>();
		modType.Add("Replace Terrain Value", ElevationModificationType.SetValue);
		modType.Add("Higher (Terrain/User) Value", ElevationModificationType.TakeHigher);
		modType.Add("Lower (Terrain/User) Value", ElevationModificationType.TakeLower);
		modType.Add("Add Value to Terrain", ElevationModificationType.AddValue);
	}

	public static ElevationModificationType GetModEnum(string description)
	{
		return modType[description];
	}

	public static List<string> GetDescriptions()
	{
		return new List<string>(modType.Keys);
	}

	public static string GetDescription(ElevationModificationType mt)
	{
		foreach (KeyValuePair<string, ElevationModificationType> modType2 in modType)
		{
			if (modType2.Value == mt)
			{
				return modType2.Key;
			}
		}
		return string.Empty;
	}
}
