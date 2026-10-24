using System;
using System.IO;
using System.Xml.Linq;
using Geospatial.GDALAssist;
using Utility.Extensions;
using Utility.Logging;
using Utility.Units;

namespace Ras.Layers;

public class ProjectionDefinition
{
	public class ErrorCodes
	{
		public static readonly MessageClass UnknownProjectionType = new MessageClass("ProjectionDefinition_UnknownProjectionType", MessageSeverity.Error, "Unknown projection type.");

		public static readonly MessageClass CouldNotParseUnitSystem = new MessageClass("ProjectionDefinition_CouldNotParseUnitSystem", MessageSeverity.Error, "Could not parse known unit system from '{TextValue}'.");

		public static Message GetUnknownProjectionType()
		{
			return UnknownProjectionType.Create();
		}

		public static Message GetCouldNotParseUnitSystem(string textValue)
		{
			return CouldNotParseUnitSystem.Create(textValue);
		}
	}

	public const string USCName = "USC";

	public const string SIName = "SI";

	public static readonly ProjectionDefinition LocalUSC = new ProjectionDefinition(ProjectionType.Local, "USC");

	public static readonly ProjectionDefinition LocalSI = new ProjectionDefinition(ProjectionType.Local, "SI");

	public ProjectionType Type { get; }

	public string Value { get; }

	public static ProjectionDefinition Local(UnitSystem units)
	{
		if (units != UnitSystem.USCustomary)
		{
			return LocalSI;
		}
		return LocalUSC;
	}

	public static ProjectionDefinition FromEPSG(int epsgCode)
	{
		return new ProjectionDefinition(ProjectionType.EPSG, epsgCode.ToString());
	}

	public ProjectionDefinition(ProjectionType type, string stringValue)
	{
		Type = type;
		Value = stringValue;
	}

	public static ProjectionDefinition FromExtractedWKT(Projection prj)
	{
		return new ProjectionDefinition(ProjectionType.WKT, prj.GetWKT());
	}

	public static OperationResult CheckProjection(Projection proj, out UnitSystem units)
	{
		if (proj == null)
		{
			units = UnitSystem.USCustomary;
			return OperationResult.Fail("No Projection specified.");
		}
		if (!proj.IsProjected())
		{
			units = UnitSystem.USCustomary;
			return OperationResult.Fail("Unprojected coordinate systems not supported (e.g. Lat/Long).");
		}
		if (!proj.TryParseUnitSystem(out units))
		{
			units = UnitSystem.USCustomary;
			return OperationResult.Fail("Could not parse linear units: " + proj.GetLinearUnitsName());
		}
		return true;
	}

	public OperationResult Evaluate(out Projection proj, out UnitSystem units)
	{
		if (Type == ProjectionType.Unknown)
		{
			proj = null;
			units = UnitSystem.USCustomary;
			return OperationResult.Fail(ErrorCodes.GetUnknownProjectionType());
		}
		if (Type == ProjectionType.Local)
		{
			proj = null;
			if (Value.RoughlyEquals("USC"))
			{
				units = UnitSystem.USCustomary;
				return true;
			}
			if (Value.RoughlyEquals("SI"))
			{
				units = UnitSystem.SI;
				return true;
			}
			units = UnitSystem.USCustomary;
			return OperationResult.Fail("Could not parse units for local projection: " + Value.InDoubleQuotes());
		}
		try
		{
			if (Type == ProjectionType.EPSG)
			{
				if (!int.TryParse(Value, out var result))
				{
					proj = null;
					units = UnitSystem.USCustomary;
					return OperationResult.Fail("Could not parse " + Value.InDoubleQuotes() + " as integer EPSG code.");
				}
				proj = new EPSGProjection(result);
			}
			else if (Type == ProjectionType.Proj)
			{
				if (string.IsNullOrEmpty(Value))
				{
					proj = null;
					units = UnitSystem.USCustomary;
					return OperationResult.Fail("No proj string provided.");
				}
				proj = new Proj4Projection(Value);
			}
			else if (Type == ProjectionType.ESRI)
			{
				if (!File.Exists(Value))
				{
					proj = null;
					units = UnitSystem.USCustomary;
					return OperationResult.Fail("File not found: " + Value.InDoubleQuotes());
				}
				proj = new ESRIProjection(Value);
			}
			else
			{
				if (Type != ProjectionType.WKT)
				{
					throw new NotImplementedException();
				}
				if (string.IsNullOrEmpty(Value))
				{
					proj = null;
					units = UnitSystem.USCustomary;
					return OperationResult.Fail("No WKT string provided.");
				}
				proj = new WKTProjection(Value);
			}
		}
		catch (Exception ex)
		{
			proj = null;
			units = UnitSystem.USCustomary;
			return OperationResult.Fail(GeneralMessageCodes.GetExceptionOccurred("Evaluating projection", ex));
		}
		return CheckProjection(proj, out units);
	}

	public void Serialize(XElement projectionNode)
	{
		projectionNode.SetAttributeValue("Type", SerializeProjType(Type));
		projectionNode.SetValue(Value);
	}

	public static ProjectionDefinition Deserialize(XElement projEl)
	{
		string projType = projEl.Attribute("Type")?.Value ?? "";
		return new ProjectionDefinition(stringValue: projEl.Value, type: TryParseProjectionType(projType));
	}

	public static ProjectionType TryParseProjectionType(string projType)
	{
		if (projType.RoughlyEquals("Local") || projType.RoughlyEquals("None"))
		{
			return ProjectionType.Local;
		}
		if (projType.RoughlyEquals("EPSG"))
		{
			return ProjectionType.EPSG;
		}
		if (projType.RoughlyEquals("Proj"))
		{
			return ProjectionType.Proj;
		}
		if (projType.RoughlyEquals("ESRI"))
		{
			return ProjectionType.ESRI;
		}
		if (projType.RoughlyEquals("WKT"))
		{
			return ProjectionType.WKT;
		}
		return ProjectionType.Unknown;
	}

	public static string SerializeProjType(ProjectionType projType)
	{
		return projType switch
		{
			ProjectionType.Local => "Local", 
			ProjectionType.EPSG => "EPSG", 
			ProjectionType.Proj => "Proj", 
			ProjectionType.ESRI => "ESRI", 
			ProjectionType.WKT => "WKT", 
			_ => "Unknown", 
		};
	}

	public static string SerializeUnitSystemShort(UnitSystem unitSystem)
	{
		return unitSystem switch
		{
			UnitSystem.USCustomary => "USC", 
			UnitSystem.SI => "SI", 
			_ => "", 
		};
	}

	public static bool TryParseUnitSystem(string stringValue, out UnitSystem unitSystem)
	{
		if (stringValue.RoughlyEquals("USC"))
		{
			unitSystem = UnitSystem.USCustomary;
			return true;
		}
		if (stringValue.RoughlyEquals("SI"))
		{
			unitSystem = UnitSystem.SI;
			return true;
		}
		unitSystem = UnitSystem.NullUnitSystem;
		return false;
	}
}
