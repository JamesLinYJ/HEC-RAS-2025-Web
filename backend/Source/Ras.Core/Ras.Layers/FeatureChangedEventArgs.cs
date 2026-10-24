using System;

namespace Ras.Layers;

public class FeatureChangedEventArgs : EventArgs
{
	public FeatureChangeType ChangeType { get; }

	public int Index { get; }

	public FeatureChangedEventArgs(int idx, FeatureChangeType changeType)
	{
		Index = idx;
		ChangeType = changeType;
	}
}
