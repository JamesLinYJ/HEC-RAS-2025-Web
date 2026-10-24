using System.Collections.Generic;
using Ras.Layers;
using Utility.Files;

namespace Ras.Process.Helpers;

public sealed class TerrainSignatureModel
{
	public string Name { get; set; }

	public List<TerrainInputSignatureModel> Inputs { get; set; } = new List<TerrainInputSignatureModel>();

	public static TerrainSignatureModel From(TerrainSignature sig)
	{
		TerrainSignatureModel terrainSignatureModel = new TerrainSignatureModel
		{
			Name = sig.Name
		};
		for (int i = 0; i < sig.LayerSignatures.Count; i++)
		{
			TerrainInputSignature terrainInputSignature = sig.LayerSignatures[i];
			TerrainInputSignatureModel terrainInputSignatureModel = new TerrainInputSignatureModel
			{
				Index = i,
				Name = terrainInputSignature.Name,
				TypeName = terrainInputSignature.TypeName
			};
			FileSignature fileSignature = terrainInputSignature.FileSignature;
			if (fileSignature != null && fileSignature.FileSize > 0)
			{
				terrainInputSignatureModel.File = fileSignature.Name;
				terrainInputSignatureModel.FileSize = fileSignature.FileSize;
				terrainInputSignatureModel.LastModifiedUtc = fileSignature.DateLastModifiedUTC;
				terrainInputSignatureModel.Hash = fileSignature.RashXX3;
			}
			else
			{
				terrainInputSignatureModel.DataHash = terrainInputSignature.DataHash;
			}
			terrainSignatureModel.Inputs.Add(terrainInputSignatureModel);
		}
		return terrainSignatureModel;
	}
}
