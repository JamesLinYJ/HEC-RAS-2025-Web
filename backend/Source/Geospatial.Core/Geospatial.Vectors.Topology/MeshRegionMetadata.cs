using System;
using System.IO;
using Utility.Systems;

namespace Geospatial.Vectors.Topology;

public struct MeshRegionMetadata : IStreamableObject
{
	public const PatchType DefaultPatchType = PatchType.TrianglePaver;

	public const SizeFunction DefaultSizeFunction = SizeFunction.Constant;

	public static readonly float DefaultConstantSize = float.NaN;

	public const float DefaultStretchRate = float.NaN;

	public const float DefaultTINResolution = 1f;

	public const bool DefaultMultipoint = true;

	public const string DefaultRecipeSource = "Default()";

	public const float DefaultSizeTolerance = 1f;

	public const float DefaultRotation = 0f;

	public PatchType PatchType;

	public SizeFunction SizeFunction;

	public float UserConstantSize;

	public float ComputedConstantSize;

	public float StretchRate;

	public TopologyRecipe RegionRecipe;

	public float UserSecondaryConstantSize;

	public int CornerRingNodeSW;

	public int CornerRingNodeSE;

	public int CornerRingNodeNE;

	public int CornerRingNodeNW;

	public float RotationDegrees;

	public bool Multipoint;

	public float TINResolution;

	public float SizeTolerance;

	public int ControlPointIndex;

	public float ActualPrimaryCellSize
	{
		get
		{
			if (!float.IsNaN(UserConstantSize))
			{
				return UserConstantSize;
			}
			return ComputedConstantSize;
		}
	}

	public bool HasPrimaryCellSize => !float.IsNaN(UserConstantSize);

	public float ActualSecondaryConstantSize
	{
		get
		{
			if (!float.IsNaN(UserSecondaryConstantSize))
			{
				return UserSecondaryConstantSize;
			}
			return ActualPrimaryCellSize;
		}
	}

	public bool HasSecondaryCellSize => !float.IsNaN(UserSecondaryConstantSize);

	public bool IsEmpty => PatchType == PatchType.Empty;

	public int CornerRingNode(int i)
	{
		return i switch
		{
			0 => CornerRingNodeSW, 
			1 => CornerRingNodeSE, 
			2 => CornerRingNodeNE, 
			3 => CornerRingNodeNW, 
			_ => throw new ArgumentException($"Bad corner index {i}"), 
		};
	}

	public void SetCornerRingNode(int i, int ringNode)
	{
		switch (i)
		{
		case 0:
			CornerRingNodeSW = ringNode;
			break;
		case 1:
			CornerRingNodeSE = ringNode;
			break;
		case 2:
			CornerRingNodeNE = ringNode;
			break;
		case 3:
			CornerRingNodeNW = ringNode;
			break;
		default:
			throw new ArgumentException($"Bad corner index {i}");
		}
	}

	public void SetCornerRingNodes(Span<int> corners)
	{
		CornerRingNodeSW = corners[0];
		CornerRingNodeSE = corners[1];
		CornerRingNodeNE = corners[2];
		CornerRingNodeNW = corners[3];
	}

	public static MeshRegionMetadata NullMetadata()
	{
		MeshRegionMetadata result = new MeshRegionMetadata();
		result.PatchType = PatchType.Empty;
		return result;
	}

	public static bool IsNull(MeshRegionMetadata meta)
	{
		if (meta.PatchType == PatchType.Empty && meta.SizeFunction == SizeFunction.Constant && meta.UserConstantSize == 1f)
		{
			return meta.StretchRate == 0.1f;
		}
		return false;
	}

	public bool IsNull()
	{
		return IsNull(this);
	}

	public MeshRegionMetadata(PatchType pt, SizeFunction sf, float cs, float stretchRate)
	{
		ComputedConstantSize = 0f;
		PatchType = PatchType.TrianglePaver;
		SizeFunction = SizeFunction.Constant;
		UserConstantSize = DefaultConstantSize;
		StretchRate = float.NaN;
		UserSecondaryConstantSize = DefaultConstantSize;
		CornerRingNodeSW = -1;
		CornerRingNodeSE = -1;
		CornerRingNodeNE = -1;
		CornerRingNodeNW = -1;
		RotationDegrees = 0f;
		Multipoint = true;
		TINResolution = 1f;
		SizeTolerance = 1f;
		ControlPointIndex = -1;
		PatchType = pt;
		SizeFunction = sf;
		UserConstantSize = cs;
		StretchRate = stretchRate;
		RegionRecipe = new TopologyRecipe();
	}

	public MeshRegionMetadata(PatchType pt, SizeFunction sf, float cs)
	{
		ComputedConstantSize = 0f;
		PatchType = PatchType.TrianglePaver;
		SizeFunction = SizeFunction.Constant;
		UserConstantSize = DefaultConstantSize;
		StretchRate = float.NaN;
		UserSecondaryConstantSize = DefaultConstantSize;
		CornerRingNodeSW = -1;
		CornerRingNodeSE = -1;
		CornerRingNodeNE = -1;
		CornerRingNodeNW = -1;
		RotationDegrees = 0f;
		Multipoint = true;
		TINResolution = 1f;
		SizeTolerance = 1f;
		ControlPointIndex = -1;
		PatchType = pt;
		SizeFunction = sf;
		UserConstantSize = cs;
		StretchRate = float.NaN;
		RegionRecipe = new TopologyRecipe();
	}

	public MeshRegionMetadata()
	{
		ComputedConstantSize = 0f;
		RegionRecipe = null;
		PatchType = PatchType.TrianglePaver;
		SizeFunction = SizeFunction.Constant;
		UserConstantSize = DefaultConstantSize;
		StretchRate = float.NaN;
		UserSecondaryConstantSize = DefaultConstantSize;
		CornerRingNodeSW = -1;
		CornerRingNodeSE = -1;
		CornerRingNodeNE = -1;
		CornerRingNodeNW = -1;
		RotationDegrees = 0f;
		Multipoint = true;
		TINResolution = 1f;
		SizeTolerance = 1f;
		ControlPointIndex = -1;
	}

	public void StreamOut(BinaryWriter bw)
	{
		bw.Write((int)PatchType);
		bw.Write((int)SizeFunction);
		bw.Write(UserConstantSize);
		RegionRecipe.StreamOut(bw);
		bw.Write(StretchRate);
		bw.Write(UserSecondaryConstantSize);
		bw.Write(RotationDegrees);
		bw.Write(Multipoint);
		bw.Write(TINResolution);
		bw.Write(SizeTolerance);
	}

	public void StreamIn(BinaryReader br)
	{
		PatchType = (PatchType)br.ReadInt32();
		SizeFunction = (SizeFunction)br.ReadInt32();
		UserConstantSize = br.ReadSingle();
		RegionRecipe = new TopologyRecipe();
		RegionRecipe.StreamIn(br);
		StretchRate = br.ReadSingle();
		UserSecondaryConstantSize = br.ReadSingle();
		RotationDegrees = br.ReadSingle();
		Multipoint = br.ReadBoolean();
		TINResolution = br.ReadSingle();
		SizeTolerance = br.ReadSingle();
	}
}
