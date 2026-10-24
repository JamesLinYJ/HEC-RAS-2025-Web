using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Utility.Logging;
using Utility.Units;

namespace Ras.Engine.Structures;

public abstract class HydraulicStructure
{
	public enum HSType
	{
		Weir,
		Gate,
		Culvert,
		Pump
	}

	public string Name { get; set; }

	public abstract HSType StructureType { get; }

	public Polyline CenterLine { get; set; }

	public int[] Faces { get; set; } = Array.Empty<int>();

	public int[] UpCells { get; set; } = Array.Empty<int>();

	public int[] DnCells { get; set; } = Array.Empty<int>();

	public sbyte[] FaceSign { get; set; } = Array.Empty<sbyte>();

	public abstract bool TreatFlowAsInternalBoundary { get; }

	public int CellLevelLts { get; set; } = -1;

	public UnitSystem UnitSys { get; set; }

	public virtual double CellWaterTransferFlow => 0.0;

	public HydraulicStructure(string name, Polyline cL)
	{
		Name = name;
		CenterLine = cL;
	}

	public OperationResult IdentifyStructureCellsAndFaces(Mesh mesh)
	{
		if (StructureType == HSType.Gate)
		{
			return IdentifyStructureCellsAndFacesFromLine(mesh);
		}
		if (StructureType == HSType.Weir)
		{
			return IdentifyStructureCellsFromFaces(mesh);
		}
		return IdentifyStructureCellsFromPts(mesh);
	}

	public OperationResult IdentifyStructureCellsAndFacesFromLine(Mesh mesh)
	{
		OperationResult operationResult = new OperationResult();
		List<(int, bool)> source = mesh.FacesAlongPolyline(CenterLine);
		Faces = source.Select(((int, bool) t) => t.Item1).ToArray();
		bool[] boolValues = source.Select(((int, bool) t) => t.Item2).ToArray();
		UpCells = new int[Faces.Length];
		DnCells = new int[Faces.Length];
		FaceSign = new sbyte[Faces.Length];
		for (int intervalIndex = 0; intervalIndex < Faces.Length; intervalIndex++)
		{
			int cellA = mesh.Faces[Faces[intervalIndex]].cellA;
			int cellB = mesh.Faces[Faces[intervalIndex]].cellB;
			if (cellA < 0 || cellB < 0)
			{
				operationResult.AddFailure(StructureMessageCodes.GetStructOnBoundaryError(Name));
				return operationResult;
			}
			if (boolValues[intervalIndex])
			{
				UpCells[intervalIndex] = cellB;
				DnCells[intervalIndex] = cellA;
				FaceSign[intervalIndex] = -1;
			}
			else
			{
				UpCells[intervalIndex] = cellA;
				DnCells[intervalIndex] = cellB;
				FaceSign[intervalIndex] = 1;
			}
		}
		return operationResult;
	}

	public OperationResult IdentifyStructureCellsFromFaces(Mesh mesh)
	{
		OperationResult operationResult = new OperationResult();
		UpCells = new int[Faces.Length];
		DnCells = new int[Faces.Length];
		FaceSign = new sbyte[Faces.Length];
		if (Faces.Length == 0)
		{
			operationResult.AddFailure("No faces found for structure " + Name);
			return operationResult;
		}
		for (int i = 0; i < Faces.Length; i++)
		{
			Face face = mesh.Faces[Faces[i]];
			if (face.cellA < 0 || face.cellB < 0)
			{
				operationResult.AddFailure(StructureMessageCodes.GetStructOnBoundaryError(Name));
				return operationResult;
			}
		}
		if (Faces.Length == 1)
		{
			SetFaceSide(mesh, 0, 1);
			return operationResult;
		}
		for (int j = 0; j < Faces.Length - 1; j++)
		{
			Face face2 = mesh.Faces[Faces[j]];
			Face face3 = mesh.Faces[Faces[j + 1]];
			bool sign = face2.fpB == face3.fpA || face2.fpB == face3.fpB;
			SetFaceSide(mesh, j, (sbyte)(sign ? 1 : (-1)));
		}
		int lengthOffset = Faces.Length - 1;
		Face face4 = mesh.Faces[Faces[lengthOffset]];
		Face face5 = mesh.Faces[Faces[lengthOffset - 1]];
		bool sign2 = ((FaceSign[lengthOffset - 1] == 1) ? face5.fpB : face5.fpA) == face4.fpA;
		SetFaceSide(mesh, lengthOffset, (sbyte)(sign2 ? 1 : (-1)));
		return operationResult;
	}

	private void SetFaceSide(Mesh mesh, int index, sbyte sign)
	{
		Face face = mesh.Faces[Faces[index]];
		FaceSign[index] = sign;
		if (sign == 1)
		{
			UpCells[index] = face.cellA;
			DnCells[index] = face.cellB;
		}
		else
		{
			UpCells[index] = face.cellB;
			DnCells[index] = face.cellA;
		}
	}

	public OperationResult IdentifyStructureCellsFromPts(Mesh mesh)
	{
		OperationResult operationResult = new OperationResult();
		Point pt = CenterLine.Points[0];
		Point pt2 = CenterLine.Points[CenterLine.Points.Count - 1];
		int cell = mesh.GetCell(pt);
		int cell2 = mesh.GetCell(pt2);
		if (cell < 0)
		{
			operationResult.AddFailure(StructureMessageCodes.GetStructEndPtError(Name, pt));
		}
		if (cell2 < 0)
		{
			operationResult.AddFailure(StructureMessageCodes.GetStructEndPtError(Name, pt2));
		}
		if (cell >= 0 && cell == cell2)
		{
			operationResult.AddFailure(StructureMessageCodes.GetStructSingleCellError(Name));
		}
		UpCells = new int[1] { cell };
		DnCells = new int[1] { cell2 };
		return operationResult;
	}

	public abstract void DistributeStructure();

	public abstract void Prepare(SolverControl solverControl, Mesh mesh, MeshPropertyTables meshPropertyTables);

	public void AddSourceSinks(OrderedDictionary<int, (double flow, double grad, Vector momentum)> SourceSinks)
	{
		if (StructureType == HSType.Culvert || StructureType == HSType.Pump)
		{
			double computed = 0.0;
			double computed2 = 0.0;
			Vector vector = default;
			for (int i = 0; i < UpCells.Length; i++)
			{
				SourceSinks.TryAdd(UpCells[i], (computed, computed2, vector));
			}
			for (int j = 0; j < DnCells.Length; j++)
			{
				SourceSinks.TryAdd(DnCells[j], (computed, computed2, vector));
			}
		}
	}

	public virtual void OverwriteFaceFlows(MeshWaterState ws)
	{
	}

	public abstract void Apply(double absTime, double dt, HydraulicStructureCollection hsCollection, MeshWaterState waterState, Mesh mesh, MeshPropertyTables propTables, HydraulicMeshComputer hmesh);

	public virtual bool TryGetCellWaterTransferCells(out int upstreamCell, out int downstreamCell)
	{
		upstreamCell = -1;
		downstreamCell = -1;
		return false;
	}

	public virtual double ComputeFlow(double upstreamWSEL, double downstreamWSEL)
	{
		return 0.0;
	}

	public virtual (double flow, double vel) ComputeFlowAndVel(double upstreamWSEL, double downstreamWSEL)
	{
		return (flow: 0.0, vel: 0.0);
	}

	public HydraulicStructure ShallowCopy()
	{
		return (HydraulicStructure)MemberwiseClone();
	}

	public override string ToString()
	{
		return Name;
	}
}
