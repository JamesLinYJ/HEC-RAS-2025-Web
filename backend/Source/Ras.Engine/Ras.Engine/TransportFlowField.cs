using System;

namespace Ras.Engine;

public class TransportFlowField
{
	public readonly int CellCount;

	public readonly int FaceCount;

	public double[] FaceFlowOld;

	public double[] FaceFlowNew;

	public double[] FaceAreaOld;

	public double[] FaceAreaNew;

	public double[] CellVolumeOld;

	public double[] CellVolumeNew;

	public CellWaterSourceSnapshot CellWaterSources;

	public bool[] CellActive;

	public bool[] FaceActive;

	public bool IsAlias { get; private set; }

	public TransportFlowField(int cellCount, int faceCount)
		: this(cellCount, faceCount, allocate: true)
	{
	}

	private TransportFlowField(int cellCount, int faceCount, bool allocate)
	{
		if (cellCount < 0)
		{
			throw new ArgumentOutOfRangeException("cellCount");
		}
		if (faceCount < 0)
		{
			throw new ArgumentOutOfRangeException("faceCount");
		}
		CellCount = cellCount;
		FaceCount = faceCount;
		CellWaterSources = new CellWaterSourceSnapshot(cellCount);
		if (allocate)
		{
			FaceFlowOld = new double[faceCount];
			FaceFlowNew = new double[faceCount];
			FaceAreaOld = new double[faceCount];
			FaceAreaNew = new double[faceCount];
			CellVolumeOld = new double[cellCount];
			CellVolumeNew = new double[cellCount];
		}
	}

	public bool IsCellActive(int cIdx)
	{
		if (CellActive != null)
		{
			return CellActive[cIdx];
		}
		return true;
	}

	public bool IsFaceActive(int fIdx)
	{
		if (FaceActive != null)
		{
			return FaceActive[fIdx];
		}
		return true;
	}

	public static TransportFlowField Steady(double[] faceFlow, double[] faceArea, double[] cellVolume)
	{
		ArgumentNullException.ThrowIfNull(faceFlow, "faceFlow");
		ArgumentNullException.ThrowIfNull(faceArea, "faceArea");
		ArgumentNullException.ThrowIfNull(cellVolume, "cellVolume");
		if (faceArea.Length != faceFlow.Length)
		{
			throw new ArgumentException("Face flow and area arrays must have matching lengths.");
		}
		return new TransportFlowField(cellVolume.Length, faceFlow.Length, allocate: false)
		{
			FaceFlowOld = faceFlow,
			FaceFlowNew = faceFlow,
			FaceAreaOld = faceArea,
			FaceAreaNew = faceArea,
			CellVolumeOld = cellVolume,
			CellVolumeNew = cellVolume
		};
	}

	public static TransportFlowField Alias(MeshWaterState oldState, MeshWaterState newState, bool[] cellActive = null, bool[] faceActive = null, CellWaterSourceSnapshot cellWaterSources = null)
	{
		if (oldState == null)
		{
			throw new ArgumentNullException("oldState");
		}
		if (newState == null)
		{
			throw new ArgumentNullException("newState");
		}
		if (oldState == newState)
		{
			throw new ArgumentException("Alias requires distinct old and new states for the theta blend.");
		}
		if (oldState.FaceFlow == null || newState.FaceFlow == null)
		{
			throw new ArgumentException("Aliased states must carry FaceFlow (IWaterStateConfig.StateHasFaceFlow).");
		}
		if (oldState.CellCount != newState.CellCount || oldState.FaceCount != newState.FaceCount)
		{
			throw new ArgumentException("Aliased states must have matching dimensions.");
		}
		if (cellActive != null && cellActive.Length != newState.CellCount)
		{
			throw new ArgumentException("Cell-active mask length must match the aliased states.");
		}
		if (faceActive != null && faceActive.Length != newState.FaceCount)
		{
			throw new ArgumentException("Face-active mask length must match the aliased states.");
		}
		if (cellWaterSources != null && cellWaterSources.CellCount != newState.CellCount)
		{
			throw new ArgumentException("Cell-water source dimensions must match the aliased states.");
		}
		TransportFlowField transportFlowField = new TransportFlowField(newState.CellCount, newState.FaceCount, allocate: false);
		transportFlowField.CellActive = cellActive;
		transportFlowField.FaceActive = faceActive;
		transportFlowField.CellWaterSources = cellWaterSources ?? new CellWaterSourceSnapshot(newState.CellCount);
		transportFlowField.Rebind(oldState, newState);
		return transportFlowField;
	}

	public void Rebind(MeshWaterState oldState, MeshWaterState newState)
	{
		ArgumentNullException.ThrowIfNull(oldState, "oldState");
		ArgumentNullException.ThrowIfNull(newState, "newState");
		if (oldState == newState)
		{
			throw new ArgumentException("Rebind requires distinct old and new states.");
		}
		if (oldState.CellCount != CellCount || newState.CellCount != CellCount || oldState.FaceCount != FaceCount || newState.FaceCount != FaceCount)
		{
			throw new ArgumentException("Rebound state dimensions must match the flow field.");
		}
		if (oldState.FaceFlow == null || newState.FaceFlow == null)
		{
			throw new ArgumentException("Rebound states must carry FaceFlow (IWaterStateConfig.StateHasFaceFlow).");
		}
		FaceFlowOld = oldState.FaceFlow;
		FaceFlowNew = newState.FaceFlow;
		FaceAreaOld = oldState.FaceArea;
		FaceAreaNew = newState.FaceArea;
		CellVolumeOld = oldState.CellVolume;
		CellVolumeNew = newState.CellVolume;
		IsAlias = true;
	}

	public void RollOver()
	{
		if (IsAlias)
		{
			throw new InvalidOperationException("RollOver is invalid in alias mode; the flow solver owns the old/new time levels.");
		}
		double[] faceFlowNew = FaceFlowNew;
		double[] faceFlowOld = FaceFlowOld;
		FaceFlowOld = faceFlowNew;
		FaceFlowNew = faceFlowOld;
		faceFlowOld = FaceAreaNew;
		faceFlowNew = FaceAreaOld;
		FaceAreaOld = faceFlowOld;
		FaceAreaNew = faceFlowNew;
		faceFlowNew = CellVolumeNew;
		faceFlowOld = CellVolumeOld;
		CellVolumeOld = faceFlowNew;
		CellVolumeNew = faceFlowOld;
	}
}
