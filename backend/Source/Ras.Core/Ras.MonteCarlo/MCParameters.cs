using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Ras.MonteCarlo;

public static class MCParameters
{
	public const string CrossSection = "Cross Section";

	public const string ManningsN = "Mannings N Value";

	private const string ManningsOpt1 = "Recompute Bridge/Culvert curves";

	private const string ManningsOpt2 = "Mannings N second option";

	private const string ContractExp = "Contract Expansion";

	private const string ContractExpOpt1 = "Contract Expansion Special option";

	private const string IceThick = "Ice Thickness";

	private const string IceThickOpt1 = "Ice Thickness Special option";

	public const string BridgeCulvert = "Bridge Culvert";

	private const string Weir = "Weir";

	private const string WeirOpt1 = "Weir option 1";

	private const string PressureFlow = "Pressure Flow";

	private const string PierDebris = "Pier Debris";

	public const string InlineStructure = "Inline Structure";

	private const string GateDisch = "Gate Discharge";

	private const string Breach = "Breach";

	public const string LateralStructure = "Lateral Structure";

	public const string StorageAreaConn = "Storage Area Connector";

	public const string BoundaryCond = "Boundary Condition";

	private const string Upper = "Upper";

	private const string Lower = "Lower";

	private const string Velocity = "Velocity";

	public const string StorageArea = "Storage Area";

	private const string Volume = "Volume";

	private const string Flow = "Flow";

	private const string Leakage = "Leakage";

	public const string Pump = "Pump";

	private const string Unknown = "Unknown";

	private const string Pumping = "Pumping";

	private const string Doing = "Doing";

	public static ReadOnlyCollection<string> NodeNames;

	public static ReadOnlyDictionary<string, ReadOnlyCollection<string>> NodeVariableMap;

	public static ReadOnlyDictionary<string, ReadOnlyCollection<string>> VariableOptionsMap;

	static MCParameters()
	{
		NodeNames = new List<string> { "Cross Section", "Bridge Culvert", "Inline Structure", "Lateral Structure", "Storage Area Connector", "Boundary Condition", "Storage Area", "Pump" }.AsReadOnly();
		NodeVariableMap = new ReadOnlyDictionary<string, ReadOnlyCollection<string>>(new Dictionary<string, ReadOnlyCollection<string>>
		{
			{
				"Cross Section",
				new List<string> { "Mannings N Value", "Contract Expansion", "Ice Thickness" }.AsReadOnly()
			},
			{
				"Bridge Culvert",
				new List<string> { "Weir", "Pressure Flow", "Pier Debris" }.AsReadOnly()
			},
			{
				"Inline Structure",
				new List<string> { "Weir", "Gate Discharge", "Breach" }.AsReadOnly()
			},
			{
				"Lateral Structure",
				new List<string> { "Weir", "Gate Discharge", "Breach" }.AsReadOnly()
			},
			{
				"Storage Area Connector",
				new List<string> { "Weir", "Gate Discharge", "Breach" }.AsReadOnly()
			},
			{
				"Boundary Condition",
				new List<string> { "Upper", "Lower", "Velocity" }.AsReadOnly()
			},
			{
				"Storage Area",
				new List<string> { "Volume", "Flow", "Leakage" }.AsReadOnly()
			},
			{
				"Pump",
				new List<string> { "Unknown", "Pumping", "Doing" }.AsReadOnly()
			}
		});
		VariableOptionsMap = new ReadOnlyDictionary<string, ReadOnlyCollection<string>>(new Dictionary<string, ReadOnlyCollection<string>>
		{
			{
				"Mannings N Value",
				new List<string> { "Recompute Bridge/Culvert curves", "Mannings N second option" }.AsReadOnly()
			},
			{
				"Contract Expansion",
				new List<string> { "Contract Expansion Special option" }.AsReadOnly()
			},
			{
				"Ice Thickness",
				new List<string> { "Ice Thickness Special option" }.AsReadOnly()
			},
			{
				"Weir",
				new List<string> { "Weir option 1" }.AsReadOnly()
			},
			{
				"Pressure Flow",
				new List<string>().AsReadOnly()
			},
			{
				"Pier Debris",
				new List<string>().AsReadOnly()
			},
			{
				"Gate Discharge",
				new List<string>().AsReadOnly()
			},
			{
				"Breach",
				new List<string>().AsReadOnly()
			},
			{
				"Upper",
				new List<string>().AsReadOnly()
			},
			{
				"Lower",
				new List<string>().AsReadOnly()
			},
			{
				"Velocity",
				new List<string>().AsReadOnly()
			},
			{
				"Volume",
				new List<string>().AsReadOnly()
			},
			{
				"Flow",
				new List<string>().AsReadOnly()
			},
			{
				"Leakage",
				new List<string>().AsReadOnly()
			},
			{
				"Unknown",
				new List<string>().AsReadOnly()
			},
			{
				"Pumping",
				new List<string>().AsReadOnly()
			},
			{
				"Doing",
				new List<string>().AsReadOnly()
			}
		});
	}

	public static ReadOnlyCollection<string> AvailableVariables(string nodeType)
	{
		NodeVariableMap.TryGetValue(nodeType, out var localValue);
		return localValue;
	}

	public static ReadOnlyCollection<string> AvailableOptions(string variableType)
	{
		VariableOptionsMap.TryGetValue(variableType, out var localValue);
		return localValue;
	}
}
