using System;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Geospatial.Vectors.Rasterizers;
using Microsoft.Extensions.Logging;
using Ras.Output;
using Utility.Logging;
using Utility.Memory;

namespace Ras.Mapping;

public class VelocityMapper : Mapper
{
	private readonly DepthMapper _depths;

	private static readonly ILogger _logger = LogHelper.GetLogger("VelocityMapper");

	public override MapTypes MapType { get; } = MapTypes.Velocity;

	public VelocityMapper(IHorizontalOutputBlock ob, MeshPropertyTables[] tables, MapperCache cache = null)
		: base(ob, tables, cache)
	{
		_depths = new DepthMapper(ob, tables, cache);
	}

	public override void Compute(int profile, GeometryMapPoints mps, Memory<float> clipTerrain, Memory<float> resultVelocities)
	{
		int inputCount = mps.InputCount;
		if (inputCount < 0)
		{
			throw new ArgumentException("Input points <= 0");
		}
		if (resultVelocities.Length != inputCount)
		{
			throw new ArgumentException("Output buffer size does not match the number of input points.");
		}
		resultVelocities.Span.Fill(-9999f);
		Memory<float> mem;
		using (SharedMemoryPool<float>.Rent(mps.InputCount * 3, -9999f, out mem))
		{
			Memory<float> outVelocityX = mem.Slice(0, mps.InputCount);
			Memory<float> outVelocityY = mem.Slice(mps.InputCount, mps.InputCount);
			Memory<float> outDepth = mem.Slice(mps.InputCount * 2, mps.InputCount);
			Compute(profile, mps, clipTerrain, outVelocityX, outVelocityY, resultVelocities, outDepth);
		}
	}

	public void Compute(int profile, GeometryMapPoints mps, Memory<float> clipTerrain, Memory<float> resultVelocities, Memory<float> resultDepth)
	{
		int inputCount = mps.InputCount;
		if (inputCount < 0)
		{
			throw new ArgumentException("Input points <= 0");
		}
		if (resultVelocities.Length != inputCount || resultDepth.Length != inputCount)
		{
			throw new ArgumentException("Output buffer size does not match the number of input points.");
		}
		resultVelocities.Span.Fill(-9999f);
		Memory<float> mem;
		using (SharedMemoryPool<float>.Rent(inputCount * 2, -9999f, out mem))
		{
			Memory<float> outVelocityX = mem.Slice(0, inputCount);
			Memory<float> outVelocityY = mem.Slice(inputCount, inputCount);
			Compute(profile, mps, clipTerrain, outVelocityX, outVelocityY, resultVelocities, resultDepth);
		}
	}

	public void Compute(int profile, GeometryMapPoints mps, Memory<float> clipTerrain, Memory<float> outVelocityX, Memory<float> outVelocityY, Memory<float> outVelocityMag)
	{
		int inputCount = mps.InputCount;
		if (inputCount < 0)
		{
			throw new ArgumentException("Input points <= 0");
		}
		if (outVelocityMag.Length != inputCount || outVelocityX.Length != inputCount || outVelocityY.Length != inputCount)
		{
			throw new ArgumentException("Output buffer size does not match the number of input points.");
		}
		outVelocityMag.Span.Fill(-9999f);
		outVelocityY.Span.Fill(-9999f);
		outVelocityX.Span.Fill(-9999f);
		Memory<float> mem;
		using (SharedMemoryPool<float>.RentNoFill(inputCount, out mem))
		{
			Compute(profile, mps, clipTerrain, outVelocityX, outVelocityY, outVelocityMag, mem);
		}
	}

	public void ComputeNoFill(int profile, GeometryMapPoints mps, Memory<float> clipTerrain, Memory<float> outVelocityX, Memory<float> outVelocityY, Memory<float> outVelocityMag, Memory<float> outDepth)
	{
		Compute(profile, mps, clipTerrain, outVelocityX, outVelocityY, outVelocityMag, outDepth);
	}

	private void Compute(int profile, GeometryMapPoints mps, Memory<float> clipTerrain, Memory<float> outVelocityX, Memory<float> outVelocityY, Memory<float> outVelocityMag, Memory<float> outDepth)
	{
		_depths.Compute(profile, mps, clipTerrain, outDepth);
		if (mps.HasMapped2DCells)
		{
			ComputeVelocity2D(mps, profile, outDepth, outVelocityX, outVelocityY, outVelocityMag);
		}
	}

	private void ComputeVelocity2D(GeometryMapPoints mps, int profileIdx, Memory<float> clipPixelDepths, Memory<float> valueX, Memory<float> valueY, Memory<float> valueMagnitude)
	{
		VectorMeshOutputs vectorMeshOutputs = new VectorMeshOutputs();
		vectorMeshOutputs.OutputVx = valueX;
		vectorMeshOutputs.OutputVy = valueY;
		vectorMeshOutputs.OutputMagnitude = valueMagnitude;
		if (mps.SlopingMapped2DAreas != null && mps.SlopingMapped2DAreas.Count > 0)
		{
			foreach (SlopingMeshMap slopingMapped2DArea in mps.SlopingMapped2DAreas)
			{
				int meshIndex = slopingMapped2DArea.MeshIndex;
				using ImOnLoan<VectorMeshInputs> imOnLoan = _mapperCache.Get2DVelocityInputs(meshIndex, profileIdx);
				if (imOnLoan == null || imOnLoan.Item == null)
				{
					throw new Exception("Error getting velocity values.");
				}
				VectorMeshInputs vectorMeshInputs = imOnLoan.Item;
				MeshClip meshClip = new MeshClip();
				meshClip.PixelDepths = clipPixelDepths;
				meshClip.CellWaterSurfaces = vectorMeshInputs.WaterSurfaceInputs.CellWaterSurfaces;
				slopingMapped2DArea.Render2D_8Stencil(vectorMeshInputs, vectorMeshOutputs, meshClip, RenderOptions);
			}
		}
		if (mps.FlatMapped2DAreas == null || mps.FlatMapped2DAreas.Count <= 0)
		{
			return;
		}
		foreach (FlatMeshMap flatMapped2DArea in mps.FlatMapped2DAreas)
		{
			int meshIndex2 = flatMapped2DArea.MeshIndex;
			using ImOnLoan<VectorMeshInputs> imOnLoan2 = _mapperCache.Get2DVelocityInputs(meshIndex2, profileIdx);
			if (imOnLoan2 == null || imOnLoan2.Item == null)
			{
				throw new Exception("Error getting velocity values.");
			}
			VectorMeshInputs vectorMeshInputs2 = imOnLoan2.Item;
			MeshClip meshClip2 = new MeshClip
			{
				PixelDepths = clipPixelDepths,
				CellWaterSurfaces = vectorMeshInputs2.WaterSurfaceInputs.CellWaterSurfaces
			};
			float[] cellMinimumElevations = flatMapped2DArea.Tables.CellMinimumElevations;
			Span<float> cellWaterSurfaces = meshClip2.CellWaterSurfaces.Span;
			foreach (FlatCellMap cell in flatMapped2DArea.Cells)
			{
				int index = cell.Index;
				if (!(cellWaterSurfaces[index] <= cellMinimumElevations[index]))
				{
					Vector cellValue_FacePerpLeastSquares = vectorMeshInputs2.ComputeCellValue_FacePerpLeastSquares(cell.Index);
					cell.SimplePaint(cellValue_FacePerpLeastSquares, vectorMeshOutputs, clipPixelDepths);
				}
			}
		}
	}
}
