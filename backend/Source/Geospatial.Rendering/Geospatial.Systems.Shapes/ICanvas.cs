using System;
using System.Drawing;
using Geospatial.Rasters.Convolution;
using Utility.VisualElements;

namespace Geospatial.Systems.Shapes;

public interface ICanvas : IVisualElement, IDisposable
{
	Color BackgroundColor { get; set; }

	Matrix3x3D Transformation { get; set; }

	Matrix3x3D BaseTransform { get; set; }

	Matrix3x3D TransformationFromBase { get; set; }

	void AddChild(IVisualElement child);

	void RemoveChild(IVisualElement child);

	bool HasChild(IVisualElement child);

	void SetZIndex(IVisualElement element, int zIndex);

	void Invalidate();

	void Invalidate(double upperLeftX, double upperLeftY, double lowerRightX, double lowerRightY);

	void Dispose(bool disposing);
}
