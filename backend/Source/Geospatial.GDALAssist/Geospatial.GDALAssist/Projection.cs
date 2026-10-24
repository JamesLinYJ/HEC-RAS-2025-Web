using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using OSGeo.OSR;
using Utility.Extensions;
using Utility.Logging;
using Utility.Units;

namespace Geospatial.GDALAssist;

public class Projection
{
	public static bool UseGlobalStaticSRSList = false;

	protected string _cmdLineProjection = "";

	private string _wkt;

	private static readonly ILogger _logger = LogHelper.GetLogger("Projection");

	protected static List<SpatialReference> StoredProjections = new List<SpatialReference>();

	protected SpatialReference _srs = new SpatialReference(null);

	private readonly Lock _srsLock = new Lock();

	private static int _nextInstanceId;

	private readonly int _instanceId = Interlocked.Increment(ref _nextInstanceId);

	protected Projection()
	{
		_srs.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
	}

	public static Projection FromFile(string fn)
	{
		string extension = Path.GetExtension(fn);
		if (extension.RoughlyEquals(".prj"))
		{
			return new ESRIProjection(fn);
		}
		if (extension.RoughlyEquals(".wkt"))
		{
			return new WKTProjection(File.ReadAllText(fn));
		}
		if (extension.RoughlyEquals(".proj4"))
		{
			return new Proj4Projection(File.ReadAllText(fn));
		}
		if (extension.RoughlyEquals(".epsg"))
		{
			if (int.TryParse(File.ReadAllText(fn), out var result))
			{
				return new EPSGProjection(result);
			}
			return null;
		}
		return null;
	}

	protected static void GlobalSRSLookup(ref SpatialReference srs)
	{
		bool condition = false;
		foreach (SpatialReference storedProjection in StoredProjections)
		{
			if (IsSame(srs, storedProjection))
			{
				srs.Dispose();
				srs = storedProjection;
				break;
			}
		}
		if (!condition && srs.Validate() == 0)
		{
			StoredProjections.Add(srs);
		}
	}

	protected static bool IsSame(SpatialReference lhs, SpatialReference rhs)
	{
		return IsSame_Strict(lhs, rhs);
	}

	protected static bool IsSame_Strict(SpatialReference lhs, SpatialReference rhs)
	{
		if (lhs.IsSame(rhs, null) != 0)
		{
			return lhs.Validate() == rhs.Validate();
		}
		return false;
	}

	internal Projection(SpatialReference SRS)
	{
		_srs = SRS.Clone();
		_srs.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
		if (UseGlobalStaticSRSList)
		{
			GlobalSRSLookup(ref _srs);
		}
		string argout;
		try
		{
			_srs.ExportToProj4(out argout);
		}
		catch
		{
			try
			{
				_srs.ExportToWkt(out argout, null);
			}
			catch
			{
				argout = "";
			}
		}
		_cmdLineProjection = "\"" + argout + "\"";
	}

	internal static void DisposeAll()
	{
		foreach (SpatialReference storedProjection in StoredProjections)
		{
			storedProjection?.Dispose();
		}
	}

	public static void ClearStoredSRS()
	{
		StoredProjections.Clear();
	}

	public string GetCommandLineString()
	{
		return _cmdLineProjection;
	}

	public string GetWKT()
	{
		if (_wkt != null)
		{
			return _wkt;
		}
		using (_srsLock.EnterScope())
		{
			if (_wkt == null)
			{
				_srs.ExportToWkt(out _wkt, null);
			}
			return _wkt;
		}
	}

	public string GetPrettyWKT()
	{
		using (_srsLock.EnterScope())
		{
			_srs.ExportToWkt(out var argout, new string[1] { "MULTILINE=YES" });
			return argout;
		}
	}

	public string GetProj4()
	{
		using (_srsLock.EnterScope())
		{
			_srs.ExportToProj4(out var argout);
			return argout;
		}
	}

	public bool IsEqual(Projection prj)
	{
		if (prj == null)
		{
			return false;
		}
		OrderLocks(this, prj, out var first, out var second);
		using (first.EnterScope())
		{
			using (second.EnterScope())
			{
				return IsSame(_srs, prj._srs);
			}
		}
	}

	public string GetName()
	{
		using (_srsLock.EnterScope())
		{
			return _srs.GetName();
		}
	}

	public bool TryGetAreaOfUse(out Extent latLon, out Extent projected)
	{
		try
		{
			using (_srsLock.EnterScope())
			{
				AreaOfUse areaOfUse = _srs.GetAreaOfUse();
				if (areaOfUse == null)
				{
					latLon = Extent.NullExtent;
					projected = Extent.NullExtent;
					return false;
				}
				latLon = new Extent(areaOfUse.west_lon_degree, areaOfUse.east_lon_degree, areaOfUse.south_lat_degree, areaOfUse.north_lat_degree);
			}
			projected = Projector.GetBoundingBox(latLon, WellKnownProjections.WGS84, this);
			return true;
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Error executing TryGetAreaOfUse");
			latLon = Extent.NullExtent;
			projected = Extent.NullExtent;
			return false;
		}
	}

	public bool TryGetMathematicalCenter(out Point center)
	{
		if (TryGetAreaOfUse(out var _, out var projected))
		{
			center = projected.Center;
			return true;
		}
		center = Point.NullPoint;
		return false;
	}

	public bool TryGetUTMZone(out int zone)
	{
		using (_srsLock.EnterScope())
		{
			zone = _srs.GetUTMZone();
		}
		return zone > 0;
	}

	public bool TryGetEPSGCode(out int epsg)
	{
		epsg = 0;
		using (_srsLock.EnterScope())
		{
			if (_srs.AutoIdentifyEPSG() != 0)
			{
				return false;
			}
			if (!int.TryParse(_srs.GetAuthorityCode(null), out var result))
			{
				return false;
			}
			if (result <= 0)
			{
				return false;
			}
			epsg = result;
		}
		return true;
	}

	private static void OrderLocks(Projection a, Projection b, out Lock first, out Lock second)
	{
		if (a._instanceId < b._instanceId)
		{
			first = a._srsLock;
			second = b._srsLock;
		}
		else
		{
			first = b._srsLock;
			second = a._srsLock;
		}
	}

	internal static CoordinateTransformation CreateTransformation(Projection src, Projection dest)
	{
		OrderLocks(src, dest, out var first, out var second);
		using (first.EnterScope())
		{
			using (second.EnterScope())
			{
				return new CoordinateTransformation(src._srs, dest._srs);
			}
		}
	}

	public double GetProjParm(string name, double defaultValue = 0.0)
	{
		using (_srsLock.EnterScope())
		{
			return _srs.GetProjParm(name, defaultValue);
		}
	}

	public double GetSemiMajor()
	{
		using (_srsLock.EnterScope())
		{
			return _srs.GetSemiMajor();
		}
	}

	public string GetLinearUnitsName()
	{
		using (_srsLock.EnterScope())
		{
			return _srs.GetLinearUnitsName();
		}
	}

	public bool TryParseUnitSystem(out UnitSystem units)
	{
		string sanitizedLinearUnitName = GetSanitizedLinearUnitName();
		if (sanitizedLinearUnitName == "Feet")
		{
			units = UnitSystem.USCustomary;
			return true;
		}
		if (sanitizedLinearUnitName == "Meters")
		{
			units = UnitSystem.SI;
			return true;
		}
		units = UnitSystem.USCustomary;
		return false;
	}

	public string GetSanitizedLinearUnitName()
	{
		try
		{
			if (IsValid() != SRSValidation.Corrupt)
			{
				string classifyLinearUnitName = ClassifyLinearUnitName(GetAttrValue("UNIT"));
				if (classifyLinearUnitName != "")
				{
					return classifyLinearUnitName;
				}
			}
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Could not read the WKT UNIT node from a projection.");
		}
		try
		{
			if (IsProjected())
			{
				return ClassifyLinearUnitName(GetLinearUnitsName());
			}
		}
		catch (Exception exception2)
		{
			_logger.LogDebug(exception2, "Could not read the linear unit name from a projection.");
		}
		return "";
	}

	private static string ClassifyLinearUnitName(string unitName)
	{
		if (string.IsNullOrEmpty(unitName))
		{
			return "";
		}
		switch (unitName.ToLower())
		{
		case "foot_us":
		case "us foot":
		case "us-foot":
		case "us-ft":
		case "feet":
		case "foot":
		case "us survey foot":
		case "u.s. survey foot":
		case "foot (international)":
		case "ft":
			return "Feet";
		case "meter":
		case "metre":
		case "meters":
		case "metres":
		case "m":
			return "Meters";
		case "degrees":
		case "degree":
		case "decimal degrees":
			return "Degrees";
		default:
		{
			string toLower = unitName.ToLower();
			if (toLower.Contains("foot"))
			{
				return "Feet";
			}
			if (toLower.Contains("ft"))
			{
				return "Feet";
			}
			if (toLower.Contains("meter"))
			{
				return "Meters";
			}
			if (toLower.Contains("metre"))
			{
				return "Meters";
			}
			if (toLower.Contains("degree"))
			{
				return "Degrees";
			}
			return "";
		}
		}
	}

	public bool IsProjected()
	{
		using (_srsLock.EnterScope())
		{
			return _srs.IsProjected() != 0;
		}
	}

	public bool IsGeographic()
	{
		using (_srsLock.EnterScope())
		{
			return _srs.IsGeographic() != 0;
		}
	}

	public string GetAttrValue(string stringValue)
	{
		using (_srsLock.EnterScope())
		{
			return _srs.GetAttrValue(stringValue, 0);
		}
	}

	public bool IsVertical()
	{
		using (_srsLock.EnterScope())
		{
			return _srs.IsVertical() != 0;
		}
	}

	public SRSValidation IsValid()
	{
		int validate;
		using (_srsLock.EnterScope())
		{
			validate = _srs.Validate();
		}
		if (validate == 0)
		{
			return SRSValidation.Valid;
		}
		if (validate == Ogr.OGRERR_UNSUPPORTED_SRS)
		{
			return SRSValidation.Unsupported;
		}
		if (validate == Ogr.OGRERR_CORRUPT_DATA)
		{
			return SRSValidation.Corrupt;
		}
		throw new Exception("SpatialReference.Validate() produced an unexpected result.");
	}

	public bool TryGetRelevantUnitLength(out double baseIncrement)
	{
		if (IsProjected())
		{
			if (!TryParseUnitSystem(out var units))
			{
				baseIncrement = double.NaN;
				return false;
			}
			if (units == UnitSystem.SI)
			{
				baseIncrement = 10.0;
			}
			else
			{
				baseIncrement = 30.0;
			}
		}
		else
		{
			baseIncrement = 0.0001;
		}
		return true;
	}

	public static double GetLengthScaleFactor(Projection sourcePrj, Projection destPrj, UnitSystem? sourceFallback = null, UnitSystem? destFallback = null)
	{
		UnitSystem unitSystem = ParseWithFallback(sourcePrj, sourceFallback);
		UnitSystem unitSystem2 = ParseWithFallback(destPrj, destFallback);
		if (unitSystem == UnitSystem.SI && unitSystem2 == UnitSystem.USCustomary)
		{
			return 3.28084;
		}
		if (unitSystem == UnitSystem.USCustomary && unitSystem2 == UnitSystem.SI)
		{
			return 0.3048;
		}
		return 1.0;
	}

	public static double GetLengthScaleFactor(Projection sourcePrj, Projection destPrj, double fallbackValue)
	{
		if (!sourcePrj.TryParseUnitSystem(out var units))
		{
			_logger.LogWarning("Could not determine the units of source projection \"{Unit}\" - using a scale factor of {Fallback}.", sourcePrj.GetLinearUnitsName(), fallbackValue);
			return fallbackValue;
		}
		if (!destPrj.TryParseUnitSystem(out var units2))
		{
			_logger.LogWarning("Could not determine the units of destination projection \"{Unit}\" - using a scale factor of {Fallback}.", destPrj.GetLinearUnitsName(), fallbackValue);
			return fallbackValue;
		}
		if (units == UnitSystem.SI && units2 == UnitSystem.USCustomary)
		{
			return 3.28084;
		}
		if (units == UnitSystem.USCustomary && units2 == UnitSystem.SI)
		{
			return 0.3048;
		}
		return 1.0;
	}

	private static UnitSystem ParseWithFallback(Projection prj, UnitSystem? fallback)
	{
		if (prj.TryParseUnitSystem(out var units))
		{
			return units;
		}
		if (!fallback.HasValue)
		{
			throw new Exception("Could not parse unit system from projection.");
		}
		return fallback.Value;
	}

	public bool TryExtractCenter(out Point center)
	{
		try
		{
			double projParm = GetProjParm("longitude_of_center", GetProjParm("central_meridian"));
			double projParm2 = GetProjParm("latitude_of_origin", GetProjParm("latitude_of_center"));
			double projParm3 = GetProjParm("false_easting");
			double projParm4 = GetProjParm("false_northing");
			center = new Point(projParm3, projParm4);
			if (projParm3 == 0.0 && projParm4 == 0.0)
			{
				center = Projector.TransformPoint(WellKnownProjections.WGS84, this, new Point(projParm, projParm2));
			}
			return true;
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Error computing center coordinate of projection.");
			center = Point.NullPoint;
			return false;
		}
	}

	public void ExportEsri(string esrifile)
	{
		SpatialReference spatialReference;
		using (_srsLock.EnterScope())
		{
			spatialReference = _srs.Clone();
		}
		spatialReference.MorphToESRI();
		spatialReference.ExportToWkt(out var argout, null);
		File.WriteAllText(esrifile, argout);
	}
}
