using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Globalization;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Rasters;
using Geospatial.Rendering;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Visual.Mapping.Observables;

public class ObservableRasterSymbology : ObservableObject
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ObservableRasterSymbology");

	private readonly RasterSymbology _symbology;

	private const string DefaultDebugLogName = "Observable Raster Symbology";

	public const RasterSymbologyValueSource DefaultValueSource = RasterSymbologyValueSource.Manual;

	private const string SurfaceFillNode = "SurfaceFill";

	private const string HillshadeNode = "Hillshade";

	private const string ContoursNode = "Contours";

	private const string ColorNode = "Color";

	private const string NameAttr = "Name";

	private const string IsEnabledAttr = "IsEnabled";

	private const string IsSurfaceFillEnabledAttr = "IsSurfaceFillEnabled";

	private const string AlphaAttr = "Alpha";

	private const string InterpolationModeAttr = "InterpolationMode";

	private const string SmoothingRadiusAttr = "SmoothingRadius";

	private const string ResampleCellSizeRatioAttr = "ResampleCellSizeRatio";

	private const string ValueSourceAttr = "ValueSource";

	private const string ZFactorAttr = "ZFactor";

	private const string AzimuthAttr = "Azimuth";

	private const string ZenithAttr = "Zenith";

	private const string IntervalAttr = "Interval";

	private const string AutoIntervalAttr = "Auto";

	private const string IndexEveryAttr = "IndexEvery";

	private const string ContourColorAttr = "Color";

	private const string OpacityAttr = "Opacity";

	private const string ShowLabelsAttr = "ShowLabels";

	private const string DensityAttr = "Density";

	public string DebugLogName { get; set; } = "Observable Raster Symbology";

	public RasterSymbology Symbology => _symbology;

	public InterpolatedSurfaceFill SurfaceFill
	{
		get
		{
			return _symbology.SurfaceFill;
		}
		set
		{
			SetProperty(SurfaceFill, value, _symbology, (RasterSymbology symb, InterpolatedSurfaceFill newVal) =>
			{
				symb.SurfaceFill = newVal;
			}, "SurfaceFill");
			_logger.LogTrace("{ObjectName}.SurfaceFill assigned.", DebugLogName);
		}
	}

	public byte Alpha
	{
		get
		{
			return _symbology.Alpha;
		}
		set
		{
			SetProperty(Alpha, value, _symbology, (RasterSymbology symb, byte newVal) =>
			{
				symb.Alpha = newVal;
			}, "Alpha");
		}
	}

	public bool IsSurfaceFillEnabled
	{
		get
		{
			return _symbology.IsSurfaceFillEnabled;
		}
		set
		{
			SetProperty(IsSurfaceFillEnabled, value, _symbology, (RasterSymbology symb, bool newVal) =>
			{
				symb.IsSurfaceFillEnabled = newVal;
			}, "IsSurfaceFillEnabled");
		}
	}

	public InterpolationMode InterpolationMode
	{
		get
		{
			return _symbology.InterpolationMode;
		}
		set
		{
			SetProperty(InterpolationMode, value, _symbology, (RasterSymbology symb, InterpolationMode newVal) =>
			{
				symb.InterpolationMode = newVal;
			}, "InterpolationMode");
		}
	}

	public float ResampleCellSizeRatio
	{
		get
		{
			return _symbology.ResampleCellSizeRatio;
		}
		set
		{
			SetProperty(ResampleCellSizeRatio, value, _symbology, (RasterSymbology symb, float newVal) =>
			{
				symb.ResampleCellSizeRatio = newVal;
			}, "ResampleCellSizeRatio");
		}
	}

	public HillshadeProperties HillshadeProperties
	{
		get
		{
			return _symbology.HillshadeProperties;
		}
		set
		{
			SetProperty(HillshadeProperties, value, _symbology, (RasterSymbology symb, HillshadeProperties newVal) =>
			{
				symb.HillshadeProperties = newVal;
			}, "HillshadeProperties");
		}
	}

	public ContourProperties ContourProperties
	{
		get
		{
			return _symbology.ContourProperties;
		}
		set
		{
			SetProperty(ContourProperties, value, _symbology, (RasterSymbology symb, ContourProperties newVal) =>
			{
				symb.ContourProperties = newVal;
			}, "ContourProperties");
		}
	}

	public float SmoothingRadius
	{
		get
		{
			return _symbology.SmoothingRadius;
		}
		set
		{
			SetProperty(SmoothingRadius, value, _symbology, (RasterSymbology symb, float newVal) =>
			{
				symb.SmoothingRadius = newVal;
			}, "SmoothingRadius");
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public RasterSymbologyValueSource ValueSource
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<RasterSymbologyValueSource>.Default.Equals(field, value))
			{
				OnPropertyChanging("ValueSource");
				field = value;
				OnPropertyChanged("ValueSource");
			}
		}
	} = RasterSymbologyValueSource.Manual;

	public ObservableRasterSymbology(RasterSymbology symbology)
	{
		_symbology = symbology;
	}

	public ObservableRasterSymbology(RasterSymbology symbology, RasterSymbologyValueSource valSource)
	{
		_symbology = symbology;
		ValueSource = valSource;
	}

	public void Serialize(XElement node, bool inlineColors, string surfaceFillName = null)
	{
		ColorXmlHelper.SetIfNotDefault(node, "IsSurfaceFillEnabled", IsSurfaceFillEnabled, defaultValue: true);
		ColorXmlHelper.SetIfNotDefault(node, "Alpha", Alpha, byte.MaxValue);
		ColorXmlHelper.SetIfNotDefault(node, "InterpolationMode", InterpolationMode, InterpolationMode.Linear);
		ColorXmlHelper.SetIfNotDefault(node, "SmoothingRadius", SmoothingRadius, 15f);
		ColorXmlHelper.SetIfNotDefault(node, "ResampleCellSizeRatio", ResampleCellSizeRatio, 1f);
		ColorXmlHelper.SetIfNotDefault(node, "ValueSource", ValueSource, RasterSymbologyValueSource.Manual);
		SerializeSurfaceFill(node, inlineColors, surfaceFillName ?? SurfaceFill?.Name);
		SerializeHillshade(node);
		SerializeContours(node);
	}

	public void Deserialize(XElement node)
	{
		if (bool.TryParse(node.Attribute("IsSurfaceFillEnabled")?.Value, out var result))
		{
			IsSurfaceFillEnabled = result;
		}
		if (byte.TryParse(node.Attribute("Alpha")?.Value, out var result2))
		{
			Alpha = result2;
		}
		if (Enum.TryParse<InterpolationMode>(node.Attribute("InterpolationMode")?.Value, out var result3))
		{
			InterpolationMode = result3;
		}
		if (float.TryParse(node.Attribute("SmoothingRadius")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result4))
		{
			SmoothingRadius = result4;
		}
		if (float.TryParse(node.Attribute("ResampleCellSizeRatio")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result5))
		{
			ResampleCellSizeRatio = result5;
		}
		if (Enum.TryParse<RasterSymbologyValueSource>(node.Attribute("ValueSource")?.Value, out var result6))
		{
			ValueSource = result6;
		}
		DeserializeSurfaceFill(node);
		DeserializeHillshade(node);
		DeserializeContours(node);
	}

	private void SerializeSurfaceFill(XElement parent, bool inlineColors, string name)
	{
		if (SurfaceFill != null)
		{
			XElement xElement = new XElement("SurfaceFill");
			if (inlineColors)
			{
				ColorXmlHelper.SerializeColorRamp(xElement, SurfaceFill.Colors, SurfaceFill.ColorValues);
			}
			else
			{
				xElement.SetAttributeValue("Name", name);
			}
			parent.Add(xElement);
		}
	}

	private void DeserializeSurfaceFill(XElement parent)
	{
		XElement xElement = parent.Element("SurfaceFill");
		if (xElement != null && ColorXmlHelper.TryParseColorRamp(xElement, out var colors, out var values))
		{
			string name = xElement.Attribute("Name")?.Value ?? SurfaceFill?.Name ?? "";
			SurfaceFill = new InterpolatedSurfaceFill(colors, values, isStretched: true, regenForScreen: false, name);
		}
	}

	private void SerializeHillshade(XElement parent)
	{
		XElement xElement = new XElement("Hillshade");
		ColorXmlHelper.SetIfNotDefault(xElement, "IsEnabled", HillshadeProperties.IsEnabled, defaultValue: false);
		ColorXmlHelper.SetIfNotDefault(xElement, "ZFactor", HillshadeProperties.ZFactor, 3f);
		ColorXmlHelper.SetIfNotDefault(xElement, "Azimuth", HillshadeProperties.Azimuth, 315f);
		ColorXmlHelper.SetIfNotDefault(xElement, "Zenith", HillshadeProperties.Zenith, 45f);
		if (xElement.HasAttributes)
		{
			parent.Add(xElement);
		}
	}

	private void DeserializeHillshade(XElement parent)
	{
		XElement xElement = parent.Element("Hillshade");
		if (xElement != null)
		{
			if (bool.TryParse(xElement.Attribute("IsEnabled")?.Value, out var result))
			{
				HillshadeProperties.IsEnabled = result;
			}
			if (float.TryParse(xElement.Attribute("ZFactor")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result2))
			{
				HillshadeProperties.ZFactor = result2;
			}
			if (float.TryParse(xElement.Attribute("Azimuth")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result3))
			{
				HillshadeProperties.Azimuth = result3;
			}
			if (float.TryParse(xElement.Attribute("Zenith")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result4))
			{
				HillshadeProperties.Zenith = result4;
			}
		}
	}

	private void SerializeContours(XElement parent)
	{
		XElement xElement = new XElement("Contours");
		ColorXmlHelper.SetIfNotDefault(xElement, "IsEnabled", ContourProperties.IsEnabled, defaultValue: false);
		ColorXmlHelper.SetIfNotDefault(xElement, "Auto", ContourProperties.AutoInterval, defaultValue: true);
		ColorXmlHelper.SetIfNotDefault(xElement, "Interval", ContourProperties.ContourInterval, 10f);
		ColorXmlHelper.SetIfNotDefault(xElement, "IndexEvery", ContourProperties.IndexEvery, 5);
		ColorXmlHelper.SetIfNotDefault(xElement, "Color", ContourProperties.Color.ToArgb(), -16777216);
		ColorXmlHelper.SetIfNotDefault(xElement, "Opacity", ContourProperties.Opacity, 70);
		ColorXmlHelper.SetIfNotDefault(xElement, "ShowLabels", ContourProperties.ShowLabels, defaultValue: true);
		ColorXmlHelper.SetIfNotDefault(xElement, "Density", ContourProperties.Density, 1f);
		if (xElement.HasAttributes)
		{
			parent.Add(xElement);
		}
	}

	private void DeserializeContours(XElement parent)
	{
		XElement xElement = parent.Element("Contours");
		if (xElement != null)
		{
			if (bool.TryParse(xElement.Attribute("IsEnabled")?.Value, out var result))
			{
				ContourProperties.IsEnabled = result;
			}
			if (bool.TryParse(xElement.Attribute("Auto")?.Value, out var result2))
			{
				ContourProperties.AutoInterval = result2;
			}
			if (float.TryParse(xElement.Attribute("Interval")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result3))
			{
				ContourProperties.ContourInterval = result3;
			}
			if (int.TryParse(xElement.Attribute("IndexEvery")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result4))
			{
				ContourProperties.IndexEvery = result4;
			}
			if (int.TryParse(xElement.Attribute("Color")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result5))
			{
				ContourProperties.Color = Color.FromArgb(result5);
			}
			if (int.TryParse(xElement.Attribute("Opacity")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result6))
			{
				ContourProperties.Opacity = result6;
			}
			if (bool.TryParse(xElement.Attribute("ShowLabels")?.Value, out var result7))
			{
				ContourProperties.ShowLabels = result7;
			}
			if (float.TryParse(xElement.Attribute("Density")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result8))
			{
				ContourProperties.Density = result8;
			}
		}
	}
}
