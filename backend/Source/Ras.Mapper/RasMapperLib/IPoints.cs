namespace RasMapperLib;

public interface IPoints
{
	int PointMCount();

	PointM PointM(int index);

	PointM FirstPointM();

	PointM LastPointM();

	Extent ComputeExtent();
}
