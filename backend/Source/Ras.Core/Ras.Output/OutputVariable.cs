using Geospatial.Vectors;

namespace Ras.Output;

public class OutputVariable
{
	public string Name { get; }

	public bool ValidIn1D { get; }

	public bool ValidInSA { get; }

	public bool ValidIn2D { get; }

	public string CrossSectionSpecificName { get; set; }

	public bool IsDebug { get; set; }

	public MeshElementType MeshElement { get; }

	public OutputVariable(string name, bool d1, bool sa, bool d2, MeshElementType meshElType)
	{
		Name = name;
		ValidIn1D = d1;
		ValidInSA = sa;
		ValidIn2D = d2;
		MeshElement = meshElType;
	}

	public string BestNameFor1D()
	{
		if (string.IsNullOrEmpty(CrossSectionSpecificName))
		{
			return Name;
		}
		return CrossSectionSpecificName;
	}
}
