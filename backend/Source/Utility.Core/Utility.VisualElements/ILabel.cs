namespace Utility.VisualElements;

public interface ILabel : IVisualElement, ICanvasPositionable
{
	string Text { get; set; }
}
