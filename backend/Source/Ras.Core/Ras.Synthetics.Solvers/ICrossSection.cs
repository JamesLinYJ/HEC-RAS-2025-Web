namespace Ras.Synthetics.Solvers;

public interface ICrossSection
{
	double Area(double depth);

	double WettedPerimeter(double depth);

	double TopWidth(double depth);

	void HydraulicProperties(double depth, out double area, out double wettedPerimeter, out double topWidth);
}
