using System.Diagnostics;
using System.Xml;
using H5Assist.VB;
using RasMapperLib.Utilities;

namespace RasMapperLib.Mapping;

[DebuggerDisplay("({HDF5Dataset})")]
public class Generic2DMapType : MapTypes
{
	public const string sXMLName = "Generic2D";

	public string HDF5Dataset;

	public string OutputBlock;

	private string _units;

	public Generic2DMapType(string outputBlock, string h5Dataset, string overrideName, bool useDatasetMinMax, string units)
		: base("Generic2D", overrideName, overrideName, "A general map-type for 2D-only datasets", supportsTimeProfile: true, supportsStaticMinMaxProfile: false, supportsDynamicMinMaxProfile: false, needsDepthThreshold: false, validIn1D: false, validInStorage: false, validIn2D: true, UnitType.Unitless, new double[2] { 0.0, 1.0 }, useDatasetMinMax)
	{
		OutputBlock = outputBlock;
		HDF5Dataset = h5Dataset;
		_units = units;
	}

	public Generic2DMapType(string outputBlock, string h5Dataset, bool useDatasetMinMax)
		: base("Generic2D", "Generic 2D Map", h5Dataset, "A general map-type for 2D-only datasets", supportsTimeProfile: true, supportsStaticMinMaxProfile: false, supportsDynamicMinMaxProfile: false, needsDepthThreshold: false, validIn1D: false, validInStorage: false, validIn2D: true, UnitType.Unitless, new double[2] { 0.0, 1.0 }, useDatasetMinMax)
	{
		OutputBlock = outputBlock;
		HDF5Dataset = h5Dataset;
	}

	public string GetUnits(string outputBlock, string d2AreaName)
	{
		if (_units == null)
		{
			using H5Reader hr = new H5Reader(HDF5Dataset);
			Generic2DMapParameters generic2DMapParameters = Generic2DMapParameters.Generic2DParametersFromDataSet(hr, outputBlock, d2AreaName, HDF5Dataset);
			_units = generic2DMapParameters.Units;
		}
		return _units;
	}

	public override void XMLSave(XmlElement node)
	{
		base.XMLSave(node);
		node.SetAttribute("OutputBlock", OutputBlock);
		node.SetAttribute("Dataset", HDF5Dataset);
		node.SetAttribute("UseDatasetMinMax", DefaultUseDatasetMinMax.ToString());
	}

	public static Generic2DMapType XMLLoad(XmlNode node)
	{
		string outputBlock = XML.StringAttribute(node, "OutputBlock");
		string h5Dataset = XML.StringAttribute(node, "Dataset");
		bool useDatasetMinMax = XML.BooleanAttribute(node, "UseDatasetMinMax");
		return new Generic2DMapType(outputBlock, h5Dataset, useDatasetMinMax);
	}
}
