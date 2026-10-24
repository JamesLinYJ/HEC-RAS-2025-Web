using System.Collections.Generic;
using System.Diagnostics;
using Geospatial.IO.ORM;
using H5Assist;
using Utility.Logging;
using Utility.ORM;
using Utility.Progress;

namespace Ras.Layers;

[DebuggerDisplay("{Name}")]
public class ClassificationSignature
{
	[Metadata("Name")]
	public string Name { get; set; }

	public List<ClassificationInputSignature> LayerSignatures { get; set; }

	public ClassificationSignature()
	{
		LayerSignatures = new List<ClassificationInputSignature>();
	}

	public OperationResult Compare(ClassificationSignature other, ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		OperationResult operationResult = true;
		if (other.LayerSignatures.Count != LayerSignatures.Count)
		{
			operationResult.AddFailure(new Message("Different number of classification layers: {ThisCount} vs {ThatCount}.", LayerSignatures.Count, other.LayerSignatures.Count));
			return operationResult;
		}
		for (int i = 0; i < LayerSignatures.Count; i++)
		{
			ClassificationInputSignature classificationInputSignature = LayerSignatures[i];
			ClassificationInputSignature classificationInputSignature2 = other.LayerSignatures[i];
			OperationResult operationResult2 = classificationInputSignature.Compare(classificationInputSignature2, pr);
			if (!operationResult2)
			{
				if (classificationInputSignature.Name != classificationInputSignature2.Name)
				{
					operationResult.AddFailure(new Message("Classification Layer[{LayerIndex}] mismatch - likely changed order. ({ThisName} -> {ThatName})", i, classificationInputSignature.Name, classificationInputSignature2.Name));
				}
				else
				{
					operationResult.AddFailure(operationResult2);
				}
			}
		}
		return operationResult;
	}

	public static bool TryLoad(H5Reader hr, string group, out ClassificationSignature signature)
	{
		signature = new ClassificationSignature();
		if (!GeospatialH5Metadata.Read(signature, hr, group))
		{
			signature = null;
			return false;
		}
		signature.LayerSignatures = FeatureIO.Read<ClassificationInputSignature>(hr, group, "Inputs");
		return true;
	}

	public void Save(H5Writer hw, string group)
	{
		GeospatialH5Metadata.Write(this, hw, group);
		FeatureIO.Write(hw, group, LayerSignatures, "Inputs");
	}
}
