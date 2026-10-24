namespace RasMapperLib;

public abstract class Transform
{
	public abstract void TransformPointM(ref PointM PointM);

	public abstract Transform InverseTransform();
}
