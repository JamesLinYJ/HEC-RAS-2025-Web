namespace Utility.VisualElements;

public interface ILabelProvider
{
	ILabel Acquire();

	void Return(ILabel label);
}
