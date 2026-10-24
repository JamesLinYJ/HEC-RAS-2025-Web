namespace Utility.VisualElements;

public interface IBitmapProvider
{
	IBitmap Acquire(int width, int height);

	void Return(IBitmap bmp);
}
