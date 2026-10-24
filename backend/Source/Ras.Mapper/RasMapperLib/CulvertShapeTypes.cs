namespace RasMapperLib;

public class CulvertShapeTypes
{
	public static CulvertShapeType Circle = new CulvertShapeType("Circular", 1, CulvertChartNumbers.ConcretePipeCulvert, CulvertChartNumbers.CorrMetalPipeCulvert, CulvertChartNumbers.ConcPipeBevRingEntrance, CulvertChartNumbers.CircularCulvert, CulvertChartNumbers.EllipticalInletFace);

	public static CulvertShapeType Box = new CulvertShapeType("Box", 2, CulvertChartNumbers.FlaredWingwalls, CulvertChartNumbers.FlaredWingwallsAndITEB, CulvertChartNumbers.NinetyDegreeHeadwall, CulvertChartNumbers.SkewedHeadWall, CulvertChartNumbers.NonOffsetFlaredWingwalls, CulvertChartNumbers.OffsetFlaredWingwalls, CulvertChartNumbers.CorrugatedMetalBoxCulvert16, CulvertChartNumbers.CorrugatedMetalBoxCulvert17, CulvertChartNumbers.CorrugatedMetalBoxCulvert18, CulvertChartNumbers.CorrugatedMetalBoxCulvert19, CulvertChartNumbers.Rectangular, CulvertChartNumbers.RectangularConcrete58, CulvertChartNumbers.RectangularConcrete59);

	public static CulvertShapeType PipeArch = new CulvertShapeType("Pipe Arch", 3, CulvertChartNumbers.EighteenInchCornerRadius34, CulvertChartNumbers.EighteenInchCornerRadius35, CulvertChartNumbers.ThirtyoneInchCornerRadius);

	public static CulvertShapeType Ellipse = new CulvertShapeType("Ellipse", 4, CulvertChartNumbers.HorizontalEllipseConcrete, CulvertChartNumbers.VerticalEllipseConcrete);

	public static CulvertShapeType Arch = new CulvertShapeType("Arch", 5, CulvertChartNumbers.ArchCorrugatedMetal41, CulvertChartNumbers.ArchCorrugatedMetal42, CulvertChartNumbers.ArchCorrugatedMetal43);

	public static CulvertShapeType SemiCircle = new CulvertShapeType("Semi-Circle", 6, CulvertChartNumbers.ArchCorrugatedMetal41, CulvertChartNumbers.ArchCorrugatedMetal42, CulvertChartNumbers.ArchCorrugatedMetal43);

	public static CulvertShapeType LowArch = new CulvertShapeType("Low Arch", 7, CulvertChartNumbers.LowAndHighCorrugatedMetalArch);

	public static CulvertShapeType HighArch = new CulvertShapeType("High Arch", 8, CulvertChartNumbers.LowAndHighCorrugatedMetalArch);

	public static CulvertShapeType ConspanArch = new CulvertShapeType("Conspan Arch", 9, CulvertChartNumbers.SpanRiseRatioApproximate21, CulvertChartNumbers.SpanRiseRatioApproximate41);
}
