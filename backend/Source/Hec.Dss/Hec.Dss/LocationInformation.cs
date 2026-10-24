using System.Collections.Generic;
using System.Text;

namespace Hec.Dss;

public class LocationInformation
{
	public const string VERTICAL_DATUM_INFO_KEY = "verticalDatumInfo";

	public double XOrdinate;

	public double YOrdinate;

	public double ZOrdiante;

	public CoordinateSystem CoordinateSystem;

	public int CoordinateID;

	public int HorizontalUnits;

	public int HorizontalDatum;

	public int VerticalUnits;

	public int VerticalDatum;

	public string TimeZoneName;

	public string Supplemental;

	public VerticalDatumInfo VerticalDatumInfo
	{
		get
		{
			string customValue = GetCustomValue("verticalDatumInfo");
			if (customValue != "")
			{
				return new VerticalDatumInfo(customValue);
			}
			return new VerticalDatumInfo("");
		}
		set
		{
			SetCustomValue("verticalDatumInfo", value.ToXml());
		}
	}

	public LocationInformation()
	{
		CoordinateSystem = CoordinateSystem.None;
		TimeZoneName = "";
		Supplemental = "";
	}

	private Dictionary<string, string> SupplementalAsDictionary()
	{
		Dictionary<string, string> stringByString = new Dictionary<string, string>();
		if (Supplemental == null || Supplemental == "")
		{
			return stringByString;
		}
		string[] parts = Supplemental.Split(';');
		for (int i = 0; i < parts.Length; i++)
		{
			string[] partsArray = parts[i].Split(':');
			if (partsArray.Length == 2)
			{
				stringByString.Add(partsArray[0], partsArray[1]);
			}
		}
		return stringByString;
	}

	private void DictionaryToSupplemental(Dictionary<string, string> stringByString)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (KeyValuePair<string, string> localValue in stringByString)
		{
			stringBuilder.Append(localValue.Key + ":" + localValue.Value + ";");
		}
		Supplemental = stringBuilder.ToString();
	}

	private string GetCustomValue(string name)
	{
		Dictionary<string, string> supplementalAsDictionary = SupplementalAsDictionary();
		if (!supplementalAsDictionary.ContainsKey(name))
		{
			return "";
		}
		if (name == "verticalDatumInfo")
		{
			return Compression.UnCompress(supplementalAsDictionary[name]);
		}
		return supplementalAsDictionary[name];
	}

	private void SetCustomValue(string name, string supplementalAsDictionary2)
	{
		if (!(name.Trim() == ""))
		{
			Dictionary<string, string> supplementalAsDictionary = SupplementalAsDictionary();
			if (supplementalAsDictionary2 == "")
			{
				supplementalAsDictionary.Remove(name);
			}
			else if (name == "verticalDatumInfo")
			{
				supplementalAsDictionary[name] = Compression.Compress(supplementalAsDictionary2);
			}
			else
			{
				supplementalAsDictionary[name] = supplementalAsDictionary2;
			}
			DictionaryToSupplemental(supplementalAsDictionary);
		}
	}
}
