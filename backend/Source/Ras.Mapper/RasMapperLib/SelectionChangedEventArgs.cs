using System;

namespace RasMapperLib;

public class SelectionChangedEventArgs : EventArgs
{
	public SelectionType SelectionType;

	public SelectionChangedEventArgs(SelectionType selType)
	{
		SelectionType = selType;
	}
}
