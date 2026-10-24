using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using Geospatial.Rendering;
using Geospatial.Rendering.Systems.Vector;
using Visual.Mapping.Observables.Symbology;

namespace Visual.Mapping.Observables;

public static class GlobalVectorSymbologyRegistry
{
	public delegate void OnGlobalSymbologyChangedEvent(VectorSymbologyOption option);

	public const string XMLCollectionTag = "CustomVectorSymbology";

	public const string XMLTag = "Symbology";

	public static List<VectorSymbologyDefinition> DefaultDefinitions { get; } = new List<VectorSymbologyDefinition>();

	public static Dictionary<string, VectorSymbology> UserDefaults { get; } = new Dictionary<string, VectorSymbology>();

	public static event OnGlobalSymbologyChangedEvent OnGlobalSymbologyChanged;

	public static void RaiseSymbologyOptionChanged(VectorSymbologyOption option)
	{
		if (option != null)
		{
			OnGlobalSymbologyChanged?.Invoke(option);
			VectorSymbology symbology = option.Symbology.Symbology;
			if (UserDefaults.TryGetValue(option.Definition.Id, out var localValue))
			{
				VectorSymbologyHelpers.CloneInto(symbology, localValue);
			}
			else
			{
				UserDefaults[option.Definition.Id] = symbology.Clone();
			}
		}
	}

	public static async Task Register()
	{
		await Task.Run((Action)BuildDefaultSymbologyList);
	}

	public static VectorSymbology GetDefaultSymbology(string id)
	{
		if (UserDefaults.TryGetValue(id, out var localValue))
		{
			return localValue;
		}
		return DefaultDefinitions.FirstOrDefault((VectorSymbologyDefinition x) => x.Id == id)?.DefaultSymbology;
	}

	public static XElement Serialize()
	{
		XElement xElement = new XElement("CustomVectorSymbology");
		foreach (KeyValuePair<string, VectorSymbology> userDefault in UserDefaults)
		{
			XElement xElement2 = SymbologyToXML(userDefault.Key, userDefault.Value);
			if (xElement2 != null)
			{
				xElement.Add(xElement2);
			}
		}
		return xElement;
	}

	public static void Deserialize(XElement deserializeFrom)
	{
		foreach (XElement element in deserializeFrom.Elements("Symbology"))
		{
			string attributeElementTerm = element.Attribute("ID")?.Value;
			if (attributeElementTerm != null)
			{
				VectorSymbology vectorSymbology = VectorSymbology.DefaultSymbology();
				VectorSymbologyHelpers.DeserializeVectorSymbology(element, vectorSymbology);
				UserDefaults[attributeElementTerm] = vectorSymbology;
			}
		}
	}

	public static void RegisterDefaultDefinition(VectorSymbologyDefinition def)
	{
		if (DefaultDefinitions.FirstOrDefault((VectorSymbologyDefinition x) => x.Id == def.Id) == null)
		{
			DefaultDefinitions.Add(def);
		}
	}

	private static XElement SymbologyToXML(string id, VectorSymbology symb)
	{
		VectorSymbologyDefinition vectorSymbologyDefinition = DefaultDefinitions.FirstOrDefault((VectorSymbologyDefinition x) => x.Id == id);
		if (vectorSymbologyDefinition == null)
		{
			return null;
		}
		if (VectorSymbologyHelpers.VectorSymbologiesEqual(vectorSymbologyDefinition.Type, symb, vectorSymbologyDefinition.DefaultSymbology))
		{
			return null;
		}
		XElement xElement = new XElement("Symbology");
		xElement.SetAttributeValue("ID", id);
		switch (vectorSymbologyDefinition.Type)
		{
		case VectorType.Line:
			VectorSymbologyHelpers.SerializeLineSymbology(xElement, symb);
			break;
		case VectorType.Polygon:
			VectorSymbologyHelpers.SerializePolygonSymbology(xElement, symb);
			break;
		case VectorType.Point:
			VectorSymbologyHelpers.SerializePointSymbology(xElement, symb);
			break;
		case VectorType.Text:
			VectorSymbologyHelpers.SerializeTextSymbology(xElement, symb);
			break;
		}
		return xElement;
	}

	private static void BuildDefaultSymbologyList()
	{
		Assembly interfaceAsm = typeof(IVectorVisualContext).Assembly;
		foreach (Type selectMany in AppDomain.CurrentDomain.GetAssemblies().Where((Assembly asm) =>
		{
			string fullName = asm.FullName;
			if (fullName.StartsWith("System.") || fullName.StartsWith("Microsoft."))
			{
				return false;
			}
			return asm == interfaceAsm || asm.GetReferencedAssemblies().Any((AssemblyName asmn) => asmn.FullName.Equals(interfaceAsm.FullName));
		}).ToList()
			.SelectMany((Assembly asm) => from t in asm.GetTypes()
				where t.IsClass && MemoryExtensions.Contains(t.GetInterfaces(), typeof(IVectorVisualContext), null)
				select t)
			.ToList())
		{
			PropertyInfo property = selectMany.GetProperty("DefaultSymbologyDefinitions", BindingFlags.Static | BindingFlags.Public);
			if (!(property == null) && property.GetValue(null) is VectorSymbologyDefinition[] vectorSymbologyDefinitionsArray)
			{
				VectorSymbologyDefinition[] vectorSymbologyDefinitions = vectorSymbologyDefinitionsArray;
				for (int intervalIndex = 0; intervalIndex < vectorSymbologyDefinitions.Length; intervalIndex++)
				{
					RegisterDefaultDefinition(vectorSymbologyDefinitions[intervalIndex]);
				}
			}
		}
	}
}
