using System;
using System.Diagnostics;

namespace Geospatial.Vectors;

[DebuggerDisplay("{DebuggerString}")]
public struct FaceValues
{
	public float ValueA;

	public float ValueB;

	public HydraulicConnection HydraulicConnection;

	public bool IsHydraulicallyConnected
	{
		get
		{
			if (HydraulicConnection != HydraulicConnection.None)
			{
				return HydraulicConnection != HydraulicConnection.Levee;
			}
			return false;
		}
	}

	private string DebuggerString
	{
		get
		{
			if (IsHydraulicallyConnected)
			{
				return ValueA.ToString();
			}
			return "Disc: " + ValueA + " / " + ValueB;
		}
	}

	public float WselValueForFlowTransfer()
	{
		if (HydraulicConnection == HydraulicConnection.None)
		{
			return -9999f;
		}
		return Math.Max(ValueA, ValueB);
	}
}
