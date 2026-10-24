using Utility.DataStructures;
using Utility.Systems;

namespace Geospatial.Rendering.Editing;

public struct EditModifiers : IEditModifiers
{
	private bool _isCtrlDown;

	private bool _isShiftDown;

	private bool _isAltDown;

	private bool _isRightClick;

	private CompactList<KeyCode> _additionalKeys;

	public EditModifiers(bool isCtrlDown, bool isShiftDown, bool isAltDown, bool isRightClick, CompactList<KeyCode> additionalKeys = default(CompactList<KeyCode>))
	{
		_isCtrlDown = false;
		_isShiftDown = false;
		_isAltDown = false;
		_isRightClick = false;
		_additionalKeys = default;
		_isCtrlDown = isCtrlDown;
		_isShiftDown = isShiftDown;
		_isAltDown = isAltDown;
		_isRightClick = isRightClick;
		_additionalKeys = additionalKeys;
	}

	public bool IsRightClick()
	{
		return _isRightClick;
	}

	public bool IsCtrlDown()
	{
		return _isCtrlDown;
	}

	public bool IsShiftDown()
	{
		return _isShiftDown;
	}

	public bool IsAltDown()
	{
		return _isAltDown;
	}

	public bool IsAdditionalKeyDown(KeyCode key)
	{
		if (_additionalKeys.Count == 0)
		{
			return false;
		}
		return _additionalKeys.Contains(key);
	}
}
