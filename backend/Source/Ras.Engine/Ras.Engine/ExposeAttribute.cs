using System;
using Geospatial.Vectors;

namespace Ras.Engine;

public class ExposeAttribute : Attribute
{
	public MeshElementType ElementType { get; set; }

	public ExposeAttribute(MeshElementType elType)
	{
		ElementType = elType;
	}
}
