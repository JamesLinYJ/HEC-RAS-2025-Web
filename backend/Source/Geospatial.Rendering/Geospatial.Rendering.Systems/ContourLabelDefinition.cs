using System;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Systems;

public class ContourLabelDefinition : LabelDefinitionBase, ILabelSymbologySource
{
	public override string Text { get; }

	public VectorSymbology LabelSymbology { get; }

	public ContourLabelDefinition(string stringValue, Memory<Point> polylineLocation, VectorSymbology labelSymbology)
	{
		Text = stringValue;
		PolylineLocation = polylineLocation;
		LabelSymbology = labelSymbology;
	}
}
