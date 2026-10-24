using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using Utility.Extensions;

namespace RasMapperLib.Mapping;

public class MapTypes
{
	public enum UnitType
	{
		Length,
		Speed,
		LengthTimesSpeed,
		LengthTimesSpeedSquared,
		PoundsPerArea,
		PoundsPerLengthTime,
		VolumetricFlow,
		Time,
		Percent,
		Unitless
	}

	private static readonly double[] DepthDefaultValues = new double[2] { 0.0, 15.0 };

	private static readonly double[] WSELDefaultValues = new double[2] { 0.0, 100.0 };

	private static readonly double[] FlowDefaultValues = new double[2] { 0.0, 10000.0 };

	private static readonly double[] DepthMMCDefaultValues = new double[4] { 0.0, 2.0, 6.0, 15.0 };

	private static readonly double[] VelocityDefaultValues = new double[7] { 0.0, 2.0, 4.0, 6.0, 8.0, 10.0, 15.0 };

	private static readonly double[] DVDefaultValues = new double[2] { 0.0, 10.0 };

	private static readonly double[] DVSquaredDefaultValues = new double[2] { 0.0, 30.0 };

	private static readonly double[] CourantDefaultValues = new double[9] { 0.0, 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0 };

	private static readonly double[] ArrivalTimeDefaultValues = new double[6] { 0.0, 1.0, 2.0, 4.0, 6.0, 8.0 };

	private static readonly double[] DurationDefaultValues = new double[6] { 0.0, 1.0, 3.0, 6.0, 12.0, 24.0 };

	private static readonly double[] ShearDefaultValues = new double[6] { 0.0, 0.02, 0.04, 0.06, 0.08, 0.1 };

	private static readonly double[] StreamPowerDefaultValues = new double[6] { 0.0, 2.0, 5.0, 10.0, 25.0, 50.0 };

	private static readonly double[] IceDefaultValues = new double[2] { 0.0, 10.0 };

	private static readonly double[] DepthForecastDefaultValues = new double[4] { 0.0, 2.0, 6.0, 15.0 };

	private static readonly double[] ManningsDefaultValues = new double[7] { 0.02, 0.03, 0.04, 0.06, 0.08, 0.1, 0.2 };

	public static readonly MapTypes Depth = new MapTypes("depth", "Depth", "Depth", "Flood Inundation Depths", supportsTimeProfile: true, supportsStaticMinMaxProfile: true, supportsDynamicMinMaxProfile: true, needsDepthThreshold: false, validIn1D: true, validInStorage: true, validIn2D: true, UnitType.Length, DepthDefaultValues, defaultUseDatasetMinMax: false);

	public static readonly MapTypes LaplacianU = new MapTypes("laplacianU", "Laplacian U", "Laplacian U", "Laplacian U", supportsTimeProfile: true, supportsStaticMinMaxProfile: false, supportsDynamicMinMaxProfile: false, needsDepthThreshold: false, validIn1D: false, validInStorage: false, validIn2D: true, UnitType.Unitless, new double[2] { 0.0, 0.1 }, defaultUseDatasetMinMax: false);

	public static readonly MapTypes LaplacianV = new MapTypes("laplacianV", "Laplacian V", "Laplacian V", "Laplacian V", supportsTimeProfile: true, supportsStaticMinMaxProfile: false, supportsDynamicMinMaxProfile: false, needsDepthThreshold: false, validIn1D: false, validInStorage: false, validIn2D: true, UnitType.Unitless, new double[2] { 0.0, 0.1 }, defaultUseDatasetMinMax: false);

	public static readonly MapTypes Elevation = new MapTypes("elevation", "Water Surface Elevation", "WSE", "Water Surface Elevations", supportsTimeProfile: true, supportsStaticMinMaxProfile: true, supportsDynamicMinMaxProfile: true, needsDepthThreshold: false, validIn1D: true, validInStorage: true, validIn2D: true, UnitType.Length, WSELDefaultValues, defaultUseDatasetMinMax: true);

	public static readonly MapTypes Flow = new MapTypes("flow", "Flow (1D Only)", "Flow", "Total Flow", supportsTimeProfile: true, supportsStaticMinMaxProfile: true, supportsDynamicMinMaxProfile: true, needsDepthThreshold: false, validIn1D: true, validInStorage: false, validIn2D: false, UnitType.VolumetricFlow, FlowDefaultValues, defaultUseDatasetMinMax: false);

	public static readonly MapTypes Courant = new MapTypes("courant", "Courant (Velocity/Length)", "Courant (Velocity/Length)", "Courant (Velocity/Length) = cell's Maximum(face abs(velocity) * dt / cell to cell distance)", supportsTimeProfile: true, supportsStaticMinMaxProfile: false, supportsDynamicMinMaxProfile: false, needsDepthThreshold: false, validIn1D: true, validInStorage: false, validIn2D: true, UnitType.Unitless, CourantDefaultValues, defaultUseDatasetMinMax: false);

	public static readonly MapTypes ResidenceTime = new MapTypes("residenceTime", "Courant (Residence Time, 2D Only)", "Courant (Residence Time)", "Courant (Residence Time) = cell outflow * dt / cell volume", supportsTimeProfile: true, supportsStaticMinMaxProfile: false, supportsDynamicMinMaxProfile: false, needsDepthThreshold: false, validIn1D: false, validInStorage: false, validIn2D: true, UnitType.Unitless, CourantDefaultValues, defaultUseDatasetMinMax: false);

	public static readonly MapTypes Froude = new MapTypes("froude", "Froude", "Froude", "Froude number v/sqrt(gd)", supportsTimeProfile: true, supportsStaticMinMaxProfile: true, supportsDynamicMinMaxProfile: false, needsDepthThreshold: false, validIn1D: true, validInStorage: false, validIn2D: true, UnitType.Unitless, CourantDefaultValues, defaultUseDatasetMinMax: false);

	public static readonly MapTypes Velocity = new MapTypes("velocity", "Velocity", "Velocity", "Velocities", supportsTimeProfile: true, supportsStaticMinMaxProfile: true, supportsDynamicMinMaxProfile: true, needsDepthThreshold: false, validIn1D: true, validInStorage: false, validIn2D: true, UnitType.Speed, VelocityDefaultValues, defaultUseDatasetMinMax: false);

	public static readonly MapTypes DepthTimesVelocity = new MapTypes("depth and velocity", "Depth * Velocity", "D * V", "Depth times Velocity", supportsTimeProfile: true, supportsStaticMinMaxProfile: true, supportsDynamicMinMaxProfile: false, needsDepthThreshold: false, validIn1D: true, validInStorage: false, validIn2D: true, UnitType.LengthTimesSpeed, DVDefaultValues, defaultUseDatasetMinMax: false);

	public static readonly MapTypes DepthTimesVelocitySquared = new MapTypes("depth and velocity squared", "Depth * Velocity^2", "D * V^2", "Depth times Velocity squared", supportsTimeProfile: true, supportsStaticMinMaxProfile: true, supportsDynamicMinMaxProfile: false, needsDepthThreshold: false, validIn1D: true, validInStorage: false, validIn2D: true, UnitType.LengthTimesSpeedSquared, DVSquaredDefaultValues, defaultUseDatasetMinMax: false);

	public static readonly MapTypes EnergyDepth = new MapTypes("energy_depth", "Energy (Depth)", "Energy (Depth)", "Kinetic + Potential Energy Map", supportsTimeProfile: true, supportsStaticMinMaxProfile: true, supportsDynamicMinMaxProfile: false, needsDepthThreshold: false, validIn1D: true, validInStorage: true, validIn2D: true, UnitType.Length, DepthDefaultValues, defaultUseDatasetMinMax: false);

	public static readonly MapTypes EnergyElevation = new MapTypes("energy_elevation", "Energy (Elevation)", "Energy (Elevation)", "Kinetic + Potential Energy Map", supportsTimeProfile: true, supportsStaticMinMaxProfile: true, supportsDynamicMinMaxProfile: false, needsDepthThreshold: false, validIn1D: true, validInStorage: true, validIn2D: true, UnitType.Length, WSELDefaultValues, defaultUseDatasetMinMax: false);

	public static readonly MapTypes ArrivalTime = new MapTypes("arrival time", "Arrival Time", "Arrival Time", "The time (from a specified Start Time) for water to reach a specified flood depth", supportsTimeProfile: false, supportsStaticMinMaxProfile: false, supportsDynamicMinMaxProfile: false, needsDepthThreshold: true, validIn1D: true, validInStorage: true, validIn2D: true, UnitType.Time, ArrivalTimeDefaultValues, defaultUseDatasetMinMax: false);

	public static readonly MapTypes ArrivalTimeMax = new MapTypes("arrival time max", "Arrival Time (Max)", "Arrival Time (Max)", "The time (from a specified Start Time) for water to reach its maximum flood depth", supportsTimeProfile: false, supportsStaticMinMaxProfile: false, supportsDynamicMinMaxProfile: false, needsDepthThreshold: false, validIn1D: true, validInStorage: true, validIn2D: true, UnitType.Time, ArrivalTimeDefaultValues, defaultUseDatasetMinMax: false);

	public static readonly MapTypes Duration = new MapTypes("duration", "Duration", "Duration", "Length of Time a location is inundated to a specified flood depth", supportsTimeProfile: false, supportsStaticMinMaxProfile: false, supportsDynamicMinMaxProfile: false, needsDepthThreshold: true, validIn1D: true, validInStorage: true, validIn2D: true, UnitType.Time, DurationDefaultValues, defaultUseDatasetMinMax: false);

	public static readonly MapTypes Recession = new MapTypes("recession", "Recession", "Recession", "Length of Time until a location ceases to be inundated to a specified flood depth", supportsTimeProfile: false, supportsStaticMinMaxProfile: false, supportsDynamicMinMaxProfile: false, needsDepthThreshold: true, validIn1D: true, validInStorage: true, validIn2D: true, UnitType.Time, DurationDefaultValues, defaultUseDatasetMinMax: false);

	public static readonly MapTypes FractionInundated = new MapTypes("fraction inundated", "Percent Time Inundated", "Percent Time Inundated", "Percent of Time a location is inundated during the simulation", supportsTimeProfile: false, supportsStaticMinMaxProfile: false, supportsDynamicMinMaxProfile: false, needsDepthThreshold: true, validIn1D: true, validInStorage: true, validIn2D: true, UnitType.Time, new double[2] { 0.0, 100.0 }, defaultUseDatasetMinMax: false);

	public static readonly MapTypes ShearStress = new MapTypes("Shear", "Shear Stress", "Shear Stress", "Shear Stress", supportsTimeProfile: true, supportsStaticMinMaxProfile: true, supportsDynamicMinMaxProfile: true, needsDepthThreshold: false, validIn1D: true, validInStorage: false, validIn2D: true, UnitType.PoundsPerArea, ShearDefaultValues, defaultUseDatasetMinMax: false);

	public static readonly MapTypes StreamPower = new MapTypes("stream power", "Stream Power", "Stream Power", "Stream Power", supportsTimeProfile: true, supportsStaticMinMaxProfile: true, supportsDynamicMinMaxProfile: true, needsDepthThreshold: false, validIn1D: true, validInStorage: false, validIn2D: true, UnitType.PoundsPerLengthTime, StreamPowerDefaultValues, defaultUseDatasetMinMax: false);

	public static readonly MapTypes CellWet = new MapTypes("wet cells", "Wet Cells", "Wet Cells", "Fill cell with wet/dry value", supportsTimeProfile: true, supportsStaticMinMaxProfile: true, supportsDynamicMinMaxProfile: true, needsDepthThreshold: true, validIn1D: true, validInStorage: true, validIn2D: true, UnitType.Length, DepthDefaultValues, defaultUseDatasetMinMax: false);

	public static readonly MapTypes Arbitrary2D = new MapTypes("Sediment", "Sediment", "Sediment", "Sediment", supportsTimeProfile: true, supportsStaticMinMaxProfile: false, supportsDynamicMinMaxProfile: false, needsDepthThreshold: false, validIn1D: false, validInStorage: false, validIn2D: true, UnitType.Unitless, new double[2] { 0.0, 1.0 }, defaultUseDatasetMinMax: false);

	public static readonly MapTypes DepthRange = new MapTypes("depth range", "Depth Range", "Depth Range", "Depth Range", supportsTimeProfile: true, supportsStaticMinMaxProfile: true, supportsDynamicMinMaxProfile: true, needsDepthThreshold: false, validIn1D: true, validInStorage: true, validIn2D: true, UnitType.Speed, new double[2] { 0.0, 1.0 }, defaultUseDatasetMinMax: false);

	public static readonly MapTypes VelocityRange = new MapTypes("velocity range", "Velocity Range", "Velocity Range", "Velocity Range", supportsTimeProfile: true, supportsStaticMinMaxProfile: true, supportsDynamicMinMaxProfile: true, needsDepthThreshold: false, validIn1D: true, validInStorage: true, validIn2D: true, UnitType.Speed, new double[2] { 0.0, 1.0 }, defaultUseDatasetMinMax: false);

	public static readonly MapTypes ArrivalRange = new MapTypes("arrival time range", "Arrival Time Range", "Arrival Time Range", "Arrival Time Range", supportsTimeProfile: false, supportsStaticMinMaxProfile: false, supportsDynamicMinMaxProfile: false, needsDepthThreshold: true, validIn1D: true, validInStorage: true, validIn2D: true, UnitType.Time, new double[2] { 0.0, 1.0 }, defaultUseDatasetMinMax: false);

	public static readonly MapTypes Unknown = new MapTypes("Unknown", "Unknown", "Unknown", "Unknown", supportsTimeProfile: false, supportsStaticMinMaxProfile: false, supportsDynamicMinMaxProfile: false, needsDepthThreshold: false, validIn1D: false, validInStorage: false, validIn2D: false, UnitType.Unitless, new double[2] { 0.0, 1.0 }, defaultUseDatasetMinMax: false);

	public static readonly List<MapTypes> AvailableTypes = new List<MapTypes>(new MapTypes[20]
	{
		Elevation, Velocity, Flow, Depth, Courant, ResidenceTime, Froude, ShearStress, DepthTimesVelocity, DepthTimesVelocitySquared,
		EnergyDepth, EnergyElevation, ArrivalTime, ArrivalTimeMax, Duration, Recession, FractionInundated, StreamPower, Arbitrary2D, CellWet
	});

	public static readonly List<MapTypes> UnsteadyTypes = new List<MapTypes>(new MapTypes[19]
	{
		Elevation, Velocity, Flow, Depth, Courant, ResidenceTime, Froude, ShearStress, DepthTimesVelocity, DepthTimesVelocitySquared,
		EnergyDepth, EnergyElevation, ArrivalTime, ArrivalTimeMax, Recession, Duration, FractionInundated, StreamPower, CellWet
	});

	public static readonly List<MapTypes> TimeSeriesPlotTypes = new List<MapTypes>(new MapTypes[4] { Elevation, Velocity, Depth, Courant });

	public static readonly List<MapTypes> SteadyTypes = new List<MapTypes>(new MapTypes[10] { Elevation, Velocity, Flow, Depth, ShearStress, DepthTimesVelocity, DepthTimesVelocitySquared, EnergyDepth, EnergyElevation, StreamPower });

	public static readonly List<MapTypes> UncertaintyAnalysisTypes = new List<MapTypes>(new MapTypes[1] { FractionInundated });

	public bool ValidIn1DAreas { get; }

	public bool ValidInStorageAreas { get; }

	public bool ValidIn2DAreas { get; }

	public UnitType MapUnitType { get; }

	public string XMLName { get; }

	public string Description { get; }

	public string DefaultLayerName { get; }

	public string LongDescription { get; }

	public bool CanAnimate { get; }

	public bool SupportsStaticMinMaxProfile { get; }

	public bool SupportsDynamicMinMaxProfile { get; }

	public bool NeedsDepthThreshold { get; }

	public double[] DefaultValues { get; }

	public bool DefaultUseDatasetMinMax { get; }

	public bool NeedsProfile
	{
		get
		{
			if (!CanAnimate && !SupportsStaticMinMaxProfile)
			{
				return SupportsDynamicMinMaxProfile;
			}
			return true;
		}
	}

	public bool TimeUnits => MapUnitType == UnitType.Time;

	public bool LengthUnits => MapUnitType == UnitType.Length;

	public bool SpeedUnits => MapUnitType == UnitType.Speed;

	public bool PercentUnits => MapUnitType == UnitType.Percent;

	public static MapTypes LoadFromXMLName(string name)
	{
		MapTypes mapTypes = AvailableTypes.FirstOrDefault([SpecialName] (MapTypes mt) => Operators.CompareString(mt.XMLName, name, TextCompare: false) == 0);
		if ((object)mapTypes == null)
		{
			mapTypes = Unknown;
		}
		return mapTypes;
	}

	public static MapTypes LoadFromXML(XmlNode node)
	{
		string stringAttribute = XML.StringAttribute(node, "MapType");
		if (stringAttribute.RoughlyEquals("Generic2D"))
		{
			return Generic2DMapType.XMLLoad(node);
		}
		return LoadFromXMLName(stringAttribute);
	}

	public virtual void XMLSave(XmlElement node)
	{
		node.SetAttribute("MapType", XMLName);
	}

	public static MapTypes LoadFromDescription(string desc)
	{
		return AvailableTypes.FirstOrDefault([SpecialName] (MapTypes mt) => Operators.CompareString(mt.Description, desc, TextCompare: false) == 0);
	}

	public static MapTypes LoadFromLayerName(string layerName)
	{
		return AvailableTypes.FirstOrDefault([SpecialName] (MapTypes mt) => Operators.CompareString(mt.DefaultLayerName, layerName, TextCompare: false) == 0);
	}

	protected MapTypes(string xmlName, string longName, string layerName, string longDesc, bool supportsTimeProfile, bool supportsStaticMinMaxProfile, bool supportsDynamicMinMaxProfile, bool needsDepthThreshold, bool validIn1D, bool validInStorage, bool validIn2D, UnitType unitType, double[] defaultValues, bool defaultUseDatasetMinMax)
	{
		XMLName = xmlName;
		Description = longName;
		DefaultLayerName = layerName;
		LongDescription = longDesc;
		CanAnimate = supportsTimeProfile;
		SupportsStaticMinMaxProfile = supportsStaticMinMaxProfile;
		SupportsDynamicMinMaxProfile = supportsDynamicMinMaxProfile;
		NeedsDepthThreshold = needsDepthThreshold;
		ValidIn1DAreas = validIn1D;
		ValidInStorageAreas = validInStorage;
		ValidIn2DAreas = validIn2D;
		MapUnitType = unitType;
		DefaultValues = defaultValues;
		DefaultUseDatasetMinMax = defaultUseDatasetMinMax;
	}

	public override bool Equals(object obj) => ReferenceEquals(this, obj);

	public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

	public static bool operator ==(MapTypes a, MapTypes b)
	{
		if ((object)a == null && (object)b == null)
		{
			return true;
		}
		if ((object)a == null || (object)b == null)
		{
			return false;
		}
		return a.Equals(b);
	}

	public static bool operator !=(MapTypes a, MapTypes b)
	{
		return !(a == b);
	}
}
