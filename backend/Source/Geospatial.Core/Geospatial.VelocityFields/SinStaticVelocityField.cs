using System;
using Geospatial.Vectors;

namespace Geospatial.VelocityFields;

public class SinStaticVelocityField : IStaticVelocityField
{
	public SinMode Mode { get; set; }

	public SinStaticVelocityField(SinMode mode)
	{
		Mode = mode;
	}

	public Vector GetVelocity(Point point)
	{
		double xComponent = Math.Sin(point.X);
		double yComponent = Math.Sin(point.Y);
		double xComponent2 = Math.Cos(point.X);
		double yComponent2 = Math.Cos(point.Y);
		return Mode switch
		{
			SinMode.SinXSinY => new Vector(xComponent, yComponent), 
			SinMode.SinXCosY => new Vector(xComponent, yComponent2), 
			SinMode.CosXSinY => new Vector(xComponent2, yComponent), 
			SinMode.CosXCosY => new Vector(xComponent2, yComponent2), 
			SinMode.SinYSinX => new Vector(yComponent, xComponent), 
			SinMode.SinYCosX => new Vector(yComponent, xComponent2), 
			SinMode.CosYSinX => new Vector(yComponent2, xComponent), 
			SinMode.CosYCosX => new Vector(yComponent2, xComponent2), 
			SinMode.SinXSinX => new Vector(xComponent, xComponent), 
			SinMode.SinXCosX => new Vector(xComponent, xComponent2), 
			SinMode.CosXSinX => new Vector(xComponent2, xComponent), 
			SinMode.CosXCosX => new Vector(xComponent2, xComponent2), 
			SinMode.SinYSinY => new Vector(yComponent, yComponent), 
			SinMode.SinYCosY => new Vector(yComponent, yComponent2), 
			SinMode.CosYSinY => new Vector(yComponent2, yComponent), 
			SinMode.CosYCosY => new Vector(yComponent2, yComponent2), 
			_ => throw new NotImplementedException(), 
		};
	}
}
