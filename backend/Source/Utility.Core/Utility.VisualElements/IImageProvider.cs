namespace Utility.VisualElements;

public interface IImageProvider
{
	IImage Acquire(double width, double height);

	void Return(IImage label);
}
