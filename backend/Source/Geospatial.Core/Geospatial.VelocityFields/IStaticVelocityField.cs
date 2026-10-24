using Geospatial.Vectors;

namespace Geospatial.VelocityFields;

public interface IStaticVelocityField
{
	Vector GetVelocity(Point point);
}
