using System;
using Geospatial.Rendering;
using Geospatial.Rendering.Systems.Vector;
using Visual.Mapping.Observables.Symbology;

namespace Visual.Mapping.Observables;

public class VectorSymbologyDefinition
{
	public static readonly SymbologyPropertyLabel[] PropertyLabels = new SymbologyPropertyLabel[4]
	{
		new SymbologyPropertyLabel("PenColor", "Pen Color"),
		new SymbologyPropertyLabel("PenWidth", "Line Width"),
		new SymbologyPropertyLabel("FillColor", "Fill Color"),
		new SymbologyPropertyLabel("PointSize", "Point Size")
	};

	public string Id { get; }

	public string Name { get; }

	public VectorType Type { get; }

	public VectorSymbology DefaultSymbology { get; }

	public string VariableName { get; }

	public string LayerName { get; }

	public Func<VectorSymbology, VectorSymbology> ConditionalFactory { get; }

	public bool ShowLineEditor
	{
		get
		{
			VectorType type = Type;
			if (type == VectorType.Point || (uint)(type - 2) <= 1u)
			{
				return true;
			}
			return false;
		}
	}

	public bool ShowPolygonEditor
	{
		get
		{
			VectorType type = Type;
			if (type == VectorType.Point || type == VectorType.Polygon)
			{
				return true;
			}
			return false;
		}
	}

	public bool ShowPointEditor => Type == VectorType.Point;

	public bool ShowTextEditor => Type == VectorType.Text;

	public VectorSymbologyDefinition(string id, string name, VectorType type, VectorSymbology defaultSymb, string variableName, string layerName, Func<VectorSymbology, VectorSymbology> conditionalFactory = null)
	{
		Id = id;
		Name = name;
		Type = type;
		DefaultSymbology = defaultSymb;
		VariableName = variableName;
		LayerName = layerName;
		ConditionalFactory = conditionalFactory;
	}

	public VectorSymbologyDefinition GetUpdatedDefinition(VectorSymbology symb)
	{
		return new VectorSymbologyDefinition(Id, Name, Type, symb, VariableName, LayerName, ConditionalFactory);
	}

	public SymbologyPropertyLabel[] GetPropertyLabels()
	{
		return Type switch
		{
			VectorType.Line => new SymbologyPropertyLabel[2]
			{
				PropertyLabels[0],
				PropertyLabels[1]
			}, 
			VectorType.Polygon => new SymbologyPropertyLabel[3]
			{
				PropertyLabels[0],
				PropertyLabels[1],
				PropertyLabels[2]
			}, 
			VectorType.Point => PropertyLabels, 
			_ => Array.Empty<SymbologyPropertyLabel>(), 
		};
	}

	public void UpdateDefaultSymbology(VectorSymbology newSymb)
	{
		VectorSymbologyHelpers.CloneInto(newSymb, DefaultSymbology);
	}
}
