using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Xml.Linq;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Rendering.TiledRenderingSystem;
using Utility.Extensions;

namespace Visual.Mapping.Observables.Symbology;

public static class VectorSymbologyHelpers
{
	public const string SymbologyElementName = "Symbology";

	public const string IDAttributeName = "ID";

	public const string PenWidthAttribute = "PenWidth";

	public const string PenColorAttribute = "PenColor";

	public const string FillColorAttribute = "FillColor";

	public const string PointSizeAttribute = "PointSize";

	public const string HaloColorAttribute = "HaloColor";

	public const string HaloThicknessAttribute = "HaloThickness";

	public const string TextEmphasisAttribute = "TextEmphasis";

	public const string ConditionElement = "Condition";

	public const string ColumnAttribute = "Column";

	public const string OperatorAttribute = "Operator";

	public const string ThresholdAttribute = "Threshold";

	public const string ResultAttribute = "Result";

	public const string PenWidthConditionsElement = "PenWidthConditions";

	public const string PenColorConditionsElement = "PenColorConditions";

	public const string FillColorConditionsElement = "FillColorConditions";

	public const string PointSizeConditionsElement = "PointSizeConditions";

	public static void DeserializeSymbologyOptionsGeneric<T>(this T t, XElement toDeserializeFrom, List<VectorSymbologyOption> symbologyOptions, VectorSymbologyDefinition[] defaultDefs) where T : IRenderLayer
	{
		foreach (VectorSymbologyDefinition vectorSymbologyDefinition in defaultDefs)
		{
			VectorSymbology defaultSymbology = GlobalVectorSymbologyRegistry.GetDefaultSymbology(vectorSymbologyDefinition.Id);
			if (defaultSymbology == null)
			{
				continue;
			}
			VectorSymbology vectorSymbology = ((vectorSymbologyDefinition.ConditionalFactory != null) ? vectorSymbologyDefinition.ConditionalFactory(defaultSymbology) : ConditionalVectorSymbology<ConditionalVectorSymbology.Unit>.FromBase(defaultSymbology));
			VectorSymbologyDefinition updatedDefinition = vectorSymbologyDefinition.GetUpdatedDefinition(vectorSymbology.Clone());
			bool condition = false;
			if (toDeserializeFrom != null)
			{
				foreach (XElement element in toDeserializeFrom.Elements("Symbology"))
				{
					string attributeElementTerm = element.Attribute("ID")?.Value;
					if (attributeElementTerm != null && !(vectorSymbologyDefinition.Id != attributeElementTerm))
					{
						condition = true;
						VectorSymbologyOption deserialize = VectorSymbologyOption.Deserialize(element, updatedDefinition, PyramidedTileBoss.Instance.MapSyncContext, vectorSymbology);
						symbologyOptions.Add(deserialize);
						break;
					}
				}
			}
			if (!condition)
			{
				ObservableVectorSymbology symb = new ObservableVectorSymbology(vectorSymbology, PyramidedTileBoss.Instance.MapSyncContext);
				VectorSymbologyOption vectorSymbologyOption = new VectorSymbologyOption(updatedDefinition, symb);
				symbologyOptions.Add(vectorSymbologyOption);
			}
			(t.GetType().GetProperty(vectorSymbologyDefinition.VariableName) ?? throw new InvalidOperationException($"Symbology definition '{vectorSymbologyDefinition.Id}' targets property '{vectorSymbologyDefinition.VariableName}', which does not exist on render layer type '{t.GetType().Name}'.")).SetValue(t, vectorSymbology);
		}
	}

	public static void SerializeLineSymbology(XElement serializeTo, VectorSymbology symb)
	{
		SerializeColor(serializeTo, "PenColor", symb.PenColor);
		serializeTo.SetAttributeValue("PenWidth", symb.PenWidth);
	}

	public static void SerializePolygonSymbology(XElement serializeTo, VectorSymbology symb)
	{
		SerializeLineSymbology(serializeTo, symb);
		SerializeColor(serializeTo, "FillColor", symb.FillColor);
	}

	public static void SerializePointSymbology(XElement serializeTo, VectorSymbology symb)
	{
		SerializePolygonSymbology(serializeTo, symb);
		serializeTo.SetAttributeValue("PointSize", symb.PointSize);
	}

	public static void SerializeTextSymbology(XElement serializeTo, VectorSymbology symb)
	{
		SerializeColor(serializeTo, "FillColor", symb.FillColor);
		serializeTo.SetAttributeValue("PenWidth", symb.PenWidth);
		SerializeColor(serializeTo, "HaloColor", symb.HaloColor);
		serializeTo.SetAttributeValue("HaloThickness", symb.HaloThickness);
		serializeTo.SetAttributeValue("TextEmphasis", symb.TextEmphasis);
	}

	public static void SerializeConditions(XElement serializeTo, VectorSymbologyCondition[] conditions, string id)
	{
		if (!conditions.IsNullOrEmpty())
		{
			XElement xElement = new XElement(id);
			serializeTo.Add(xElement);
			foreach (VectorSymbologyCondition vectorSymbologyCondition in conditions)
			{
				XElement xElement2 = new XElement("Condition");
				xElement.Add(xElement2);
				xElement2.SetAttributeValue("Column", vectorSymbologyCondition.Column);
				xElement2.SetAttributeValue("Operator", vectorSymbologyCondition.Operator);
				xElement2.SetAttributeValue("Threshold", vectorSymbologyCondition.Threshold);
				xElement2.SetAttributeValue("Result", vectorSymbologyCondition.ResultValue);
			}
		}
	}

	public static void DeserializeVectorSymbology(XElement element, VectorSymbology defaultSymb)
	{
		string attributeElementTerm = element.Attribute("PenColor")?.Value;
		if (attributeElementTerm != null)
		{
			defaultSymb.PenColor = DeserializeColor(attributeElementTerm);
		}
		double? attribute = (double?)element.Attribute("PenWidth");
		if (attribute.HasValue)
		{
			double valueOrDefault = attribute.GetValueOrDefault();
			defaultSymb.PenWidth = valueOrDefault;
		}
		string attributeElementTerm2 = element.Attribute("FillColor")?.Value;
		if (attributeElementTerm2 != null)
		{
			defaultSymb.FillColor = DeserializeColor(attributeElementTerm2);
		}
		int? attribute2 = (int?)element.Attribute("PointSize");
		if (attribute2.HasValue)
		{
			int valueOrDefault2 = attribute2.GetValueOrDefault();
			defaultSymb.PointSize = valueOrDefault2;
		}
		string attributeElementTerm3 = element.Attribute("HaloColor")?.Value;
		if (attributeElementTerm3 != null)
		{
			defaultSymb.HaloColor = DeserializeColor(attributeElementTerm3);
		}
		float? attribute3 = (float?)element.Attribute("HaloThickness");
		if (attribute3.HasValue)
		{
			float valueOrDefault3 = attribute3.GetValueOrDefault();
			defaultSymb.HaloThickness = valueOrDefault3;
		}
		attribute2 = (int?)element.Attribute("TextEmphasis");
		if (attribute2.HasValue)
		{
			int valueOrDefault4 = attribute2.GetValueOrDefault();
			defaultSymb.TextEmphasis = valueOrDefault4;
		}
	}

	public static VectorSymbologyCondition[] DeserializeConditions(XElement symbologyElem, string id)
	{
		XElement xElement = symbologyElem.Element(id);
		if (xElement == null)
		{
			return Array.Empty<VectorSymbologyCondition>();
		}
		return xElement.Elements("Condition").Select(ParseCondition).ToArray();
	}

	private static VectorSymbologyCondition ParseCondition(XElement el)
	{
		VectorSymbologyCondition vectorSymbologyCondition = new VectorSymbologyCondition
		{
			Column = (el.Attribute("Column")?.Value ?? ""),
			Threshold = (el.Attribute("Threshold")?.Value ?? ""),
			ResultValue = (el.Attribute("Result")?.Value ?? "")
		};
		if (Enum.TryParse<ConditionOperator>(el.Attribute("Operator")?.Value, out var result))
		{
			vectorSymbologyCondition.Operator = result;
		}
		return vectorSymbologyCondition;
	}

	public static bool VectorSymbologiesEqual(VectorType type, VectorSymbology symb1, VectorSymbology symb2)
	{
		return type switch
		{
			VectorType.Line => LineSymbologyEqual(symb1, symb2), 
			VectorType.Polygon => PolygonSymbologyEqual(symb1, symb2), 
			VectorType.Point => PointSymbologyEqual(symb1, symb2), 
			VectorType.Text => TextSymbologyEqual(symb1, symb2), 
			_ => false, 
		};
	}

	public static void CloneInto(VectorSymbology source, VectorSymbology dest)
	{
		dest.PenWidth = source.PenWidth;
		dest.PenColor = source.PenColor;
		dest.FillColor = source.FillColor;
		dest.PointSize = source.PointSize;
		dest.HaloThickness = source.HaloThickness;
		dest.HaloColor = source.HaloColor;
		dest.TextEmphasis = source.TextEmphasis;
	}

	public static void CloneInto(VectorSymbology source, ObservableVectorSymbology dest)
	{
		dest.PenWidth = source.PenWidth;
		dest.PenColor = source.PenColor;
		dest.FillColor = source.FillColor;
		dest.PointSize = source.PointSize;
		dest.HaloThickness = source.HaloThickness;
		dest.HaloColor = source.HaloColor;
		dest.TextEmphasis = source.TextEmphasis;
	}

	private static bool LineSymbologyEqual(VectorSymbology symb1, VectorSymbology symb2)
	{
		if (symb1.PenColor.ToArgb() == symb2.PenColor.ToArgb())
		{
			return symb1.PenWidth == symb2.PenWidth;
		}
		return false;
	}

	private static bool PolygonSymbologyEqual(VectorSymbology symb1, VectorSymbology symb2)
	{
		if (LineSymbologyEqual(symb1, symb2))
		{
			return symb1.FillColor.ToArgb() == symb2.FillColor.ToArgb();
		}
		return false;
	}

	private static bool PointSymbologyEqual(VectorSymbology symb1, VectorSymbology symb2)
	{
		if (PolygonSymbologyEqual(symb1, symb2))
		{
			return symb1.PointSize == symb2.PointSize;
		}
		return false;
	}

	private static bool TextSymbologyEqual(VectorSymbology symb1, VectorSymbology symb2)
	{
		if (symb1.PenColor.ToArgb() == symb2.PenColor.ToArgb() && symb1.FillColor.ToArgb() == symb2.FillColor.ToArgb() && symb1.HaloColor.ToArgb() == symb2.HaloColor.ToArgb())
		{
			return symb1.HaloThickness == symb2.HaloThickness;
		}
		return false;
	}

	private static void SerializeColor(XElement serializeTo, string name, Color color)
	{
		string message = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
		serializeTo.SetAttributeValue(name, message);
	}

	private static Color DeserializeColor(string hex)
	{
		return Color.FromArgb(Convert.ToInt32(hex.TrimStart('#'), 16));
	}
}
