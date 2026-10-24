using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Mapping;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class RasterMapDefinition
{
	public const string sDynamicBehavior = "Dynamic";

	public const string sStaticBehavior = "Fixed Profile";

	public const string sDynamicLagBehavior = "Dynamic with Offset";

	public string VariableName;

	public MapTypes MapType;

	public int ProfileIndex;

	public RasterMapBehavior MapBehavior;

	private string _resultShortID;

	private RASResults _result;

	public RASResults Result
	{
		get
		{
			if (_result == null)
			{
				RASMapper rasMapper = SharedData.RasMapper;
				if (rasMapper == null)
				{
					return null;
				}
				List<RASResults> source = rasMapper.ResultsGroup.FindAllResults();
				_result = source.FirstOrDefault([SpecialName] (RASResults res) => Operators.CompareString(res.PlanAttributes.PlanShortID, _resultShortID, TextCompare: false) == 0);
			}
			return _result;
		}
	}

	public string ResultShortID => _resultShortID;

	public RasterMapDefinition()
	{
		VariableName = "";
	}

	public RasterMapDefinition(string varName, RASResults result, MapTypes mt, int pfIdx, RasterMapBehavior b)
	{
		VariableName = "";
		VariableName = varName;
		_result = result;
		_resultShortID = result.PlanAttributes.PlanShortID;
		MapType = mt;
		ProfileIndex = pfIdx;
		MapBehavior = b;
	}

	public string GetRasterInfoForUser()
	{
		return string.Concat(string.Concat(string.Concat(string.Concat(VariableName + " = ", Result.PlanAttributes.PlanShortID), " | ", MapType.DefaultLayerName), " | ", Result.ProfileName(ProfileIndex)), " | ", BehaviorString());
	}

	public string Serialize()
	{
		return string.Concat(string.Concat(string.Concat(string.Concat(VariableName + " = ", Result.PlanAttributes.PlanShortID), " | ", MapType.XMLName), " | ", ProfileIndex.ToString()), " | ", BehaviorString());
	}

	public void Deserialize(string rmdInfo)
	{
		string[] parts = rmdInfo.Split('=');
		VariableName = Strings.Trim(parts[0]);
		string[] partsArray = parts[1].Split('|');
		_resultShortID = Strings.Trim(partsArray[0]);
		MapType = MapTypes.LoadFromXMLName(Strings.Trim(partsArray[1]));
		int.TryParse(partsArray[2], out ProfileIndex);
		MapBehavior = BehaviorType(Strings.Trim(partsArray[3]));
	}

	public string TryGetPlan(string plan)
	{
		return Result.PlanAttributes.PlanShortID;
	}

	public string BehaviorString()
	{
		return BehaviorString(MapBehavior);
	}

	public static string BehaviorString(RasterMapBehavior rmb)
	{
		return rmb switch
		{
			RasterMapBehavior.Dynamic => "Dynamic", 
			RasterMapBehavior.Fixed => "Fixed Profile", 
			RasterMapBehavior.DynamicLag => "Dynamic with Offset", 
			_ => "", 
		};
	}

	public static RasterMapBehavior BehaviorType(string behaviorStr)
	{
		if (string.IsNullOrEmpty(behaviorStr))
		{
			return RasterMapBehavior.Other;
		}
		return behaviorStr switch
		{
			"Dynamic" => RasterMapBehavior.Dynamic, 
			"Fixed Profile" => RasterMapBehavior.Fixed, 
			"Dynamic with Offset" => RasterMapBehavior.DynamicLag, 
			_ => RasterMapBehavior.Other, 
		};
	}

	public void XMLSave(XmlDocument doc, XmlElement xmlLayer)
	{
		XmlElement xmlElement = doc.CreateElement("RasterMap");
		xmlLayer.AppendChild(xmlElement);
		xmlElement.SetAttribute("Result", _resultShortID);
		xmlElement.SetAttribute("MapType", MapType.XMLName);
		xmlElement.SetAttribute("AnimationBehavior", BehaviorString(MapBehavior));
		xmlElement.SetAttribute("ProfileIndex", ProfileIndex.ToString());
	}

	public void XMLLoad(XmlElement xmlLayer)
	{
		if (xmlLayer != null)
		{
			_resultShortID = XML.StringAttribute(xmlLayer, "Result");
			MapType = MapTypes.LoadFromXMLName(XML.StringAttribute(xmlLayer, "MapType"));
			MapBehavior = BehaviorType(XML.StringAttribute(xmlLayer, "AnimationBehavior"));
			ProfileIndex = XML.IntegerAttribute(xmlLayer, "ProfileIndex");
		}
	}

	public static void XMLSaveAll(List<RasterMapDefinition> rmdList, XmlDocument doc, XmlElement xmlLayer)
	{
		foreach (RasterMapDefinition rmd in rmdList)
		{
			rmd.XMLSave(doc, xmlLayer);
		}
	}

	public static List<RasterMapDefinition> XMLLoadAll(XmlElement xmlLayer)
	{
		XmlNodeList xmlNodeList = xmlLayer.SelectNodes("RasterMap");
		List<RasterMapDefinition> rasterMapDefinitions = new List<RasterMapDefinition>();
		foreach (object xmlNodeList2 in xmlNodeList)
		{
			object objectValue = RuntimeHelpers.GetObjectValue(xmlNodeList2);
			RasterMapDefinition rasterMapDefinition = new RasterMapDefinition();
			rasterMapDefinition.XMLLoad((XmlElement)objectValue);
			rasterMapDefinitions.Add(rasterMapDefinition);
		}
		return rasterMapDefinitions;
	}
}
