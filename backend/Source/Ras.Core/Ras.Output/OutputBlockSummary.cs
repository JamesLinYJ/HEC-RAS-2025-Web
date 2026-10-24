using System;

namespace Ras.Output;

public class OutputBlockSummary
{
	public string RootPath { get; }

	public string CrossSectionOutput(OutputVariable ov)
	{
		throw new NotImplementedException();
	}

	public string MeshOutput(string meshName, OutputVariable ov)
	{
		throw new NotImplementedException();
	}
}
