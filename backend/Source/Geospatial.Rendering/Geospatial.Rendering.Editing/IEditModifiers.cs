using Utility.Systems;

namespace Geospatial.Rendering.Editing;

public interface IEditModifiers
{
	bool IsRightClick();

	bool IsCtrlDown();

	bool IsShiftDown();

	bool IsAltDown();

	bool IsAdditionalKeyDown(KeyCode key);
}
