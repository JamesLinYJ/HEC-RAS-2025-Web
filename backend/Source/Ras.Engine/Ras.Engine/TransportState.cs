using System;

namespace Ras.Engine;

public class TransportState
{
	public readonly int ConstituentCount;

	public readonly int CellCount;

	public double[][] CellConcentration;

	public double Time;

	public TransportState(int constituentCount, int cellCount)
	{
		ConstituentCount = constituentCount;
		CellCount = cellCount;
		CellConcentration = new double[constituentCount][];
		for (int i = 0; i < constituentCount; i++)
		{
			CellConcentration[i] = new double[cellCount];
		}
		Time = 0.0;
	}

	public TransportState Clone()
	{
		TransportState transportState = new TransportState(ConstituentCount, CellCount);
		transportState.UpdateFrom(this);
		return transportState;
	}

	public void UpdateFrom(TransportState other)
	{
		if (other.ConstituentCount != ConstituentCount || other.CellCount != CellCount)
		{
			throw new ArgumentException("TransportState dimensions differ.");
		}
		for (int i = 0; i < ConstituentCount; i++)
		{
			Array.Copy(other.CellConcentration[i], CellConcentration[i], CellCount);
		}
		Time = other.Time;
	}
}
