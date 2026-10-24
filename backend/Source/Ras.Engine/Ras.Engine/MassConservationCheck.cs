using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Ras.Engine;

public class MassConservationCheck
{
	public List<int> _cells;

	private Solver _solver;

	private object _lock;

	private double lastVolume;

	private double netVolumeChange;

	public double error;

	public double cumulativeError;

	private List<Tuple<int, int>> _faces;

	public double Time { get; private set; }

	public double cumulativeRelError => cumulativeError / Math.Max(lastVolume, 1.0);

	public double relError => error / Math.Max(lastVolume, 1.0);

	public MassConservationCheck(Solver solver, List<int> cells)
	{
		_cells = new List<int>(cells);
		_solver = solver;
		_lock = new object();
	}

	public void Initialize(MeshWaterState ws)
	{
		_faces = new List<Tuple<int, int>>();
		HashSet<int> cells = new HashSet<int>(_cells);
		foreach (int cell2 in _cells)
		{
			Cell cell = _solver.Mesh.Cells[cell2];
			Span<int> faces = cell.Faces.Span;
			for (int i = 0; i < faces.Length; i++)
			{
				int faceIndex = faces[i];
				if ((_solver.FaceFlags[faceIndex] & Solver.FaceFlag.HAS_FLOW) != 0)
				{
					Face face = _solver.Mesh.Faces[faceIndex];
					int sign;
					int neighborCell;
					if (face.cellA == cell2)
					{
						sign = 1;
						neighborCell = face.cellB;
					}
					else
					{
						sign = -1;
						neighborCell = face.cellA;
					}
					if (!cells.Contains(neighborCell))
					{
						_faces.Add(new Tuple<int, int>(faceIndex, sign));
					}
				}
			}
		}
		lastVolume = 0.0;
		netVolumeChange = 0.0;
		Commit(ws);
		error = 0.0;
		cumulativeError = 0.0;
	}

	public void Increment(MeshWaterState ws, double dt)
	{
		double volumeIncrement = 0.0;
		foreach (Tuple<int, int> face in _faces)
		{
			volumeIncrement -= dt * (double)face.Item2 * ws.FaceFlow[face.Item1];
		}
		if (_solver.CellWaterSources.HasCellSources)
		{
			ReadOnlySpan<double> currentNetCellFlow = _solver.CellWaterSources.CurrentNetCellFlow;
			foreach (int cell in _cells)
			{
				volumeIncrement += dt * currentNetCellFlow[cell];
			}
		}
		netVolumeChange += volumeIncrement;
	}

	public void Commit(MeshWaterState ws)
	{
		double newVolume = 0.0;
		_solver.threadFor.Run(_cells.Count, (int start, int end) =>
		{
			double cellVolumeSum = 0.0;
			for (int i = start; i <= end; i++)
			{
				cellVolumeSum += ws.CellVolume[_cells[i]];
			}
			lock (_lock)
			{
				newVolume += cellVolumeSum;
			}
		});
		error = lastVolume + netVolumeChange - newVolume;
		cumulativeError += error;
		lastVolume = newVolume;
		Time = ws.Time;
		netVolumeChange = 0.0;
	}

	public void CommitSerial(MeshWaterState ws)
	{
		double cellVolumeSum = 0.0;
		for (int i = 0; i < _cells.Count; i++)
		{
			cellVolumeSum += ws.CellVolume[_cells[i]];
		}
		error = lastVolume + netVolumeChange - cellVolumeSum;
		cumulativeError += error;
		lastVolume = cellVolumeSum;
		Time = ws.Time;
		netVolumeChange = 0.0;
	}

	public void Rollback()
	{
		netVolumeChange = 0.0;
	}
}
