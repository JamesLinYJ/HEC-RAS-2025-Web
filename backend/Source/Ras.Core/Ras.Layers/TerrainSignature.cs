using System.Collections.Generic;
using System.Diagnostics;
using Geospatial.IO.ORM;
using H5Assist;
using Utility.Logging;
using Utility.ORM;
using Utility.Progress;

namespace Ras.Layers;

[DebuggerDisplay("{Name}")]
public class TerrainSignature
{
	[Metadata("Name")]
	public string Name { get; set; }

	public List<TerrainInputSignature> LayerSignatures { get; set; }

	public TerrainSignature()
	{
		LayerSignatures = new List<TerrainInputSignature>();
	}

	public OperationResult Compare(TerrainSignature other, ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		OperationResult operationResult = true;
		if (other.LayerSignatures.Count != LayerSignatures.Count)
		{
			operationResult.AddFailure(new Message("Different number of terrain layers: {ThisCount} vs {ThatCount}.", LayerSignatures.Count, other.LayerSignatures.Count));
			return operationResult;
		}
		for (int i = 0; i < LayerSignatures.Count; i++)
		{
			TerrainInputSignature terrainInputSignature = LayerSignatures[i];
			TerrainInputSignature terrainInputSignature2 = other.LayerSignatures[i];
			OperationResult operationResult2 = terrainInputSignature.Compare(terrainInputSignature2, pr);
			if (!operationResult2)
			{
				if (terrainInputSignature.Name != terrainInputSignature2.Name)
				{
					operationResult.AddFailure(new Message("Terrain Layer[{LayerIndex}] mismatch - likely changed order. ({ThisName} -> {ThatName})", i, terrainInputSignature.Name, terrainInputSignature2.Name));
				}
				else
				{
					operationResult.AddFailure(operationResult2);
				}
			}
		}
		return operationResult;
	}

	public static bool TryLoad(H5Reader hr, string group, out TerrainSignature signature)
	{
		signature = new TerrainSignature();
		if (!GeospatialH5Metadata.Read(signature, hr, group))
		{
			signature = null;
			return false;
		}
		signature.LayerSignatures = FeatureIO.Read<TerrainInputSignature>(hr, group, "Inputs");
		return true;
	}

	public void Save(H5Writer hw, string group)
	{
		hw.Delete(group);
		GeospatialH5Metadata.Write(this, hw, group);
		FeatureIO.Write(hw, group, LayerSignatures, "Inputs");
	}
}
