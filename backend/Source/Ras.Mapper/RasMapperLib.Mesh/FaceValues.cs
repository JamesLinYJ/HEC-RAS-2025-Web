using System.Diagnostics;

namespace RasMapperLib.Mesh;

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

	public FaceValues(float valA, float valB, HydraulicConnection hc)
	{
		this = default;
		ValueA = valA;
		ValueB = valB;
		HydraulicConnection = hc;
	}
}
