using System;
using System.Drawing;
using Geospatial.Vectors;
using Utility.VisualElements;

namespace Geospatial.Systems.Shapes;

public interface IVectorCanvas : IVisualElement, IDisposable
{
	double StrokeThickness { get; set; }

	Color Fill { get; set; }

	Color Stroke { get; set; }

	Color HaloColor { get; set; }

	bool IsStrokeDotted { get; set; }

	float[] DottedStrokeStyle { get; set; }

	bool IsTranslated { get; set; }

	float[] Translation { get; set; }

	float HaloThickness { get; set; }

	int Count { get; }

	string Font { get; set; }

	int TextEmphasis { get; set; }

	int TextMaxWidth { get; set; }

	float TextKerning { get; set; }

	Memory<Geospatial.Vectors.Point> ClipExtent { get; set; }

	void SetCapacity(int capacity)
	{
	}

	void DisposeInternalResources();

	void Clear();

	void AddShape(IShape child);

	void RemoveShape(IShape child);

	void TrimShapesFromEnd(int count);

	bool ContainsShape(IShape child);

	void CompleteClip();
}
