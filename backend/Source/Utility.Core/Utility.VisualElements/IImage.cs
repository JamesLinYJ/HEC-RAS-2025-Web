using System.Numerics;

namespace Utility.VisualElements;

public interface IImage : IVisualElement, ICanvasPositionable
{
	IBitmap Source { get; set; }

	Vector3 Scale { get; set; }

	Vector3 CenterPoint { get; set; }
}
