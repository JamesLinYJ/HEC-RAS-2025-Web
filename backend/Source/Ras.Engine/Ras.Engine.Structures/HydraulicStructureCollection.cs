using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine.Structures.Culverts;
using Ras.Engine.Structures.Gates;
using Ras.Engine.Structures.Pumps;
using Ras.Engine.Structures.Weirs;
using Utility.Progress;

namespace Ras.Engine.Structures;

public class HydraulicStructureCollection
{
	public readonly List<HydraulicStructure> AllHydraulicStructures = new List<HydraulicStructure>();

	public readonly List<Weir> Weirs = new List<Weir>();

	public readonly List<Gate> Gates = new List<Gate>();

	public readonly List<Culvert> Culverts = new List<Culvert>();

	public readonly List<Pump> Pumps = new List<Pump>();

	public OrderedDictionary<int, (double flow, double grad, Vector momentum)> SourceSinks = new OrderedDictionary<int, (double, double, Vector)>();

	public bool HasStructures => AllHydraulicStructures.Count > 0;

	public bool HasWeirs => Weirs.Count > 0;

	public bool HasGates => Gates.Count > 0;

	public bool HasCulverts => Culverts.Count > 0;

	public bool HasPumps => Pumps.Count > 0;

	public bool HasSourceSinks => SourceSinks.Count > 0;

	public void AddHydraulicStructure(HydraulicStructure structure)
	{
		AllHydraulicStructures.Add(structure);
		if (structure is Weir)
		{
			Weirs.Add((Weir)structure);
		}
		else if (structure is Gate)
		{
			Gates.Add((Gate)structure);
		}
		else if (structure is Culvert)
		{
			Culverts.Add((Culvert)structure);
		}
		else if (structure is Pump)
		{
			Pumps.Add((Pump)structure);
		}
	}

	public bool Equals(HydraulicStructureCollection other)
	{
		if (AllHydraulicStructures != other.AllHydraulicStructures)
		{
			return false;
		}
		if (Weirs != other.Weirs)
		{
			return false;
		}
		if (Gates != other.Gates)
		{
			return false;
		}
		if (Culverts != other.Culverts)
		{
			return false;
		}
		if (Pumps != other.Pumps)
		{
			return false;
		}
		return true;
	}

	public void IdentifyStructureCellsAndFaces(Mesh globalMesh)
	{
		foreach (HydraulicStructure allHydraulicStructure in AllHydraulicStructures)
		{
			allHydraulicStructure.IdentifyStructureCellsAndFaces(globalMesh);
		}
	}

	public void Prepare(ComputeControl computeControl, SolverControl solverControl, Mesh mesh, MeshPropertyTables meshPropertyTables, ProgressReporter pr = null)
	{
		if (AllHydraulicStructures.Count == 0)
		{
			return;
		}
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		pr.ReportMessage("Preparing Structures...");
		int count = AllHydraulicStructures.Count;
		float fracPer = 0.95f / (float)count;
		int nCompleted = 0;
		Parallel.For(0, AllHydraulicStructures.Count, (int i) =>
		{
			HydraulicStructure hydraulicStructure = AllHydraulicStructures[i];
			hydraulicStructure.UnitSys = computeControl.Units;
			hydraulicStructure.Prepare(solverControl, mesh, meshPropertyTables);
			int increment = Interlocked.Increment(ref nCompleted);
			pr.ReportProgressFraction((float)increment * fracPer);
			_ = pr.CancelRequested;
		});
		foreach (HydraulicStructure allHydraulicStructure in AllHydraulicStructures)
		{
			allHydraulicStructure.AddSourceSinks(SourceSinks);
		}
		pr.ReportProgress(100);
	}

	public void Apply(double absTime, double dt, MeshWaterState waterState, Mesh mesh, MeshPropertyTables propTables, HydraulicMeshComputer hmesh)
	{
		double computed = 0.0;
		double computed2 = 0.0;
		Vector vector = default;
		foreach (int ordinal in SourceSinks.Keys.ToList())
		{
			SourceSinks[ordinal] = (computed, computed2, vector);
		}
		Parallel.For(0, AllHydraulicStructures.Count, (int i) =>
		{
			AllHydraulicStructures[i].Apply(absTime, dt, this, waterState, mesh, propTables, hmesh);
		});
		foreach (int ordinal2 in SourceSinks.Keys.ToList())
		{
			double flow = SourceSinks[ordinal2].flow;
			int indexOf = SourceSinks.IndexOf(ordinal2);
			waterState.SourceFlows[indexOf] = flow;
		}
	}

	public void ApplyLTS(double absTime, double dt, MeshWaterState waterState, Mesh mesh, MeshPropertyTables propTables, HydraulicMeshComputer hmesh, int m, int mIncr, bool isFBF)
	{
		double computed = 0.0;
		double computed2 = 0.0;
		Vector vector = default;
		foreach (int ordinal in SourceSinks.Keys.ToList())
		{
			foreach (HydraulicStructure allHydraulicStructure in AllHydraulicStructures)
			{
				if (allHydraulicStructure.UpCells.Union(allHydraulicStructure.DnCells).Contains(ordinal) && ShouldUpdateStructureLTS(allHydraulicStructure, m, mIncr, isFBF))
				{
					SourceSinks[ordinal] = (computed, computed2, vector);
					break;
				}
			}
		}
		Parallel.For(0, AllHydraulicStructures.Count, (int i) =>
		{
			HydraulicStructure hydraulicStructure = AllHydraulicStructures[i];
			if (ShouldUpdateStructureLTS(hydraulicStructure, m, mIncr, isFBF))
			{
				hydraulicStructure.Apply(absTime, dt, this, waterState, mesh, propTables, hmesh);
			}
		});
		foreach (int ordinal2 in SourceSinks.Keys.ToList())
		{
			foreach (HydraulicStructure allHydraulicStructure2 in AllHydraulicStructures)
			{
				if (allHydraulicStructure2.UpCells.Union(allHydraulicStructure2.DnCells).Contains(ordinal2) && ShouldUpdateStructureLTS(allHydraulicStructure2, m, mIncr, isFBF))
				{
					double flow = SourceSinks[ordinal2].flow;
					int indexOf = SourceSinks.IndexOf(ordinal2);
					waterState.SourceFlows[indexOf] = flow;
					break;
				}
			}
		}
	}

	private bool ShouldUpdateStructureLTS(HydraulicStructure hs, int m, int mIncrLts, bool isFBF)
	{
		int cellLevelLts = hs.CellLevelLts;
		int mIncrLtsTerm = m + mIncrLts;
		if (isFBF)
		{
			return m % (1 << cellLevelLts) == 1 << cellLevelLts - 1;
		}
		return mIncrLtsTerm % (1 << cellLevelLts) == 0;
	}
}
