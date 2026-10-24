using System;
using System.Linq;
using System.Xml;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class ArrivalTimeParameters
{
	private const float MinDepthThreshold = 0.001f;

	public ArrivalStartModes StartMode { get; set; }

	public float ArrivalSimDays { get; set; }

	public float ArrivalSimHours { get; set; }

	public float ArrivalSimMinutes { get; set; }

	public string ArrivalAbsDateTime { get; set; }

	public bool OutputUseHours { get; set; }

	public float DepthThreshold { get; set; }

	public ArrivalTimeParameters()
	{
		ArrivalSimDays = float.NaN;
		ArrivalSimHours = float.NaN;
		ArrivalSimMinutes = float.NaN;
		ArrivalAbsDateTime = "";
		OutputUseHours = true;
		DepthThreshold = float.NaN;
	}

	public double GetArrivalStartingSimulationTime(RASResults res)
	{
		if (StartMode == ArrivalStartModes.AtBeginning)
		{
			return 0.0;
		}
		if (StartMode == ArrivalStartModes.UseSimTime)
		{
			double computed = 0.0;
			if (!float.IsNaN(ArrivalSimDays))
			{
				computed += (double)ArrivalSimDays;
			}
			if (!float.IsNaN(ArrivalSimHours))
			{
				computed += (double)ArrivalSimHours / 24.0;
			}
			if (!float.IsNaN(ArrivalSimMinutes))
			{
				computed += (double)ArrivalSimMinutes / 24.0 / 60.0;
			}
			return computed;
		}
		if (StartMode == ArrivalStartModes.UseAbsDateTime)
		{
			DateTime dateTime = DSS.ConvertFromDSSDateTime(ArrivalAbsDateTime);
			if ((DateTime.Compare(dateTime, DateTime.MinValue) != 0) & (res.ProfileDateTimes.Count() > 0))
			{
				return (dateTime - res.ProfileDateTimes[0]).TotalDays;
			}
			return 0.0;
		}
		return 0.0;
	}

	public string ArrivalStartDescription()
	{
		if (StartMode == ArrivalStartModes.AtBeginning)
		{
			return "";
		}
		if (StartMode == ArrivalStartModes.UseSimTime)
		{
			string textValue = "";
			if (!float.IsNaN(ArrivalSimDays))
			{
				textValue = textValue + Strings.Format(ArrivalSimDays) + "d";
			}
			if (!float.IsNaN(ArrivalSimHours))
			{
				textValue = textValue + Strings.Format(ArrivalSimHours) + "h";
			}
			if (!float.IsNaN(ArrivalSimMinutes))
			{
				textValue = textValue + Strings.Format(ArrivalSimMinutes) + "m";
			}
			if (textValue.Length > 0)
			{
				return textValue;
			}
		}
		else if (((StartMode == ArrivalStartModes.UseAbsDateTime) & !string.IsNullOrWhiteSpace(ArrivalAbsDateTime)) && DateTime.Compare(DSS.ConvertFromDSSDateTime(ArrivalAbsDateTime), DateTime.MinValue) != 0)
		{
			return ArrivalAbsDateTime;
		}
		return "";
	}

	public void XMLSave(XmlElement mapParametersNode)
	{
		if (!float.IsNaN(DepthThreshold))
		{
			mapParametersNode.SetAttribute("ArrivalDepth", DepthThreshold.ToString());
		}
		if (StartMode != ArrivalStartModes.AtBeginning)
		{
			mapParametersNode.SetAttribute("ArrivalStartMode", StartMode.ToString());
		}
		if (!float.IsNaN(ArrivalSimDays))
		{
			mapParametersNode.SetAttribute("ArrivalSimDays", ArrivalSimDays.ToString());
		}
		if (!float.IsNaN(ArrivalSimHours))
		{
			mapParametersNode.SetAttribute("ArrivalSimHours", ArrivalSimHours.ToString());
		}
		if (!float.IsNaN(ArrivalSimMinutes))
		{
			mapParametersNode.SetAttribute("ArrivalSimMinutes", ArrivalSimMinutes.ToString());
		}
		if (Operators.CompareString(ArrivalAbsDateTime, "", TextCompare: false) != 0)
		{
			mapParametersNode.SetAttribute("ArrivalAbsDateTime", ArrivalAbsDateTime);
		}
		if (!OutputUseHours)
		{
			mapParametersNode.SetAttribute("Hours", OutputUseHours.ToString());
		}
	}

	public void XMLLoad(XmlElement mapParametersNode)
	{
		DepthThreshold = XML.SingleAttribute(mapParametersNode, "ArrivalDepth");
		StartMode = (ArrivalStartModes)XML.IntegerAttribute(mapParametersNode, "ArrivalStartMode", 0);
		ArrivalSimDays = XML.SingleAttribute(mapParametersNode, "ArrivalSimDays");
		ArrivalSimHours = XML.SingleAttribute(mapParametersNode, "ArrivalSimHours");
		ArrivalSimMinutes = XML.SingleAttribute(mapParametersNode, "ArrivalSimMinutes");
		ArrivalAbsDateTime = XML.StringAttribute(mapParametersNode, "ArrivalAbsDateTime");
		OutputUseHours = XML.BooleanAttribute(mapParametersNode, "Hours", defaultValue: true);
	}
}
