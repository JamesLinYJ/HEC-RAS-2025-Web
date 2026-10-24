namespace RasMapperLib;

public class CulvertChartNumbers
{
	public static CulvertChartNumber ConcretePipeCulvert = new CulvertChartNumber("Concrete pipe culvert", 1, CulvertScaleNumbers.SquareEdgeWithHeadwall, CulvertScaleNumbers.GrooveEndEntranceWHW, CulvertScaleNumbers.GrooveEndEntrancePPFF);

	public static CulvertChartNumber CorrMetalPipeCulvert = new CulvertChartNumber("Corrugated metal pipe culvert", 2, CulvertScaleNumbers.Headwall, CulvertScaleNumbers.MiteredToConformToSlope, CulvertScaleNumbers.PipeProjectingFromFill);

	public static CulvertChartNumber ConcPipeBevRingEntrance = new CulvertChartNumber("Concrete pipe; Beveled ring entrance", 3, CulvertScaleNumbers.ASmallBevel, CulvertScaleNumbers.BLargeBevel);

	public static CulvertChartNumber FlaredWingwalls = new CulvertChartNumber("Flared wingwalls", 8, CulvertScaleNumbers.WingwallFlared30To75Deg, CulvertScaleNumbers.WingwallFlared90Or15Deg, CulvertScaleNumbers.WingwallFlared0Deg);

	public static CulvertChartNumber FlaredWingwallsAndITEB = new CulvertChartNumber("Flared wingwalls and inlet top edge bevel", 9, CulvertScaleNumbers.FortyfiveDegWingwallFlare, CulvertScaleNumbers.WingwallFlared18To33Deg);

	public static CulvertChartNumber NinetyDegreeHeadwall = new CulvertChartNumber("90 Degree headwall; Chamfered or beveled inlet", 10, CulvertScaleNumbers.InletEdgesChamfered, CulvertScaleNumbers.InletEdgesBeveledHalfInch, CulvertScaleNumbers.InletEdgesBeveled1Inch);

	public static CulvertChartNumber SkewedHeadWall = new CulvertChartNumber("Skewed Headwall; Chamfered or beveled nlet", 11, CulvertScaleNumbers.HeadwallSkewed45DegInletChamfered, CulvertScaleNumbers.HeadwallSkewed30Deg, CulvertScaleNumbers.HeadwallSkewed15Deg, CulvertScaleNumbers.HeawallSkewed10To45Deg);

	public static CulvertChartNumber NonOffsetFlaredWingwalls = new CulvertChartNumber("Non-offset flared wingwalls; 3/4-inch chamfered inlet", 12, CulvertScaleNumbers.WingwallsFlared45Deg, CulvertScaleNumbers.WingwallsFlared18Deg3To1, CulvertScaleNumbers.WingwallsFlared18Deg1To1);

	public static CulvertChartNumber OffsetFlaredWingwalls = new CulvertChartNumber("Offset flared wingalls; Beveled edge at top of inlet", 13, CulvertScaleNumbers.WingwallsFlared45TopEdgeBezel, CulvertScaleNumbers.WingwallsFlared33Deg, CulvertScaleNumbers.WingwallsFlared18DegInletTopEdgeBez);

	public static CulvertChartNumber CorrugatedMetalBoxCulvert16 = new CulvertChartNumber("Corrugated metal box culvert", 16, CulvertScaleNumbers.NinetyDegreeHeadwall, CulvertScaleNumbers.ThickWallProjecting, CulvertScaleNumbers.ThinWallProjecting3);

	public static CulvertChartNumber CorrugatedMetalBoxCulvert17 = new CulvertChartNumber("Corrugated metal box culvert", 17, CulvertScaleNumbers.NinetyDegreeHeadwall, CulvertScaleNumbers.ThickWallProjecting, CulvertScaleNumbers.ThinWallProjecting3);

	public static CulvertChartNumber CorrugatedMetalBoxCulvert18 = new CulvertChartNumber("Corrugated metal box culvert", 18, CulvertScaleNumbers.NinetyDegreeHeadwall, CulvertScaleNumbers.ThickWallProjecting, CulvertScaleNumbers.ThinWallProjecting3);

	public static CulvertChartNumber CorrugatedMetalBoxCulvert19 = new CulvertChartNumber("Corrugated metal box culvert", 19, CulvertScaleNumbers.NinetyDegreeHeadwall, CulvertScaleNumbers.ThickWallProjecting, CulvertScaleNumbers.ThinWallProjecting3);

	public static CulvertChartNumber HorizontalEllipseConcrete = new CulvertChartNumber("Horizontal ellipse; Concrete", 29, CulvertScaleNumbers.SquareEdgeWithHeadwall, CulvertScaleNumbers.GroovedEndWithHeadwall, CulvertScaleNumbers.GroovedEndProjecting);

	public static CulvertChartNumber VerticalEllipseConcrete = new CulvertChartNumber("Vertical ellipse; Concrete", 30, CulvertScaleNumbers.SquareEdgeWithHeadwall, CulvertScaleNumbers.GroovedEndWithHeadwall, CulvertScaleNumbers.GroovedEndProjecting);

	public static CulvertChartNumber EighteenInchCornerRadius34 = new CulvertChartNumber("18 Inch corner radius; Corrugated metal", 34, CulvertScaleNumbers.NinetyDegreeHeadwall, CulvertScaleNumbers.MiteredToConformToSlope, CulvertScaleNumbers.Projecting3);

	public static CulvertChartNumber EighteenInchCornerRadius35 = new CulvertChartNumber("18 Inch corner radius; Corrugated metal", 35, CulvertScaleNumbers.Projecting1, CulvertScaleNumbers.NoBeveles, CulvertScaleNumbers.ThirtyThreeDegBevels);

	public static CulvertChartNumber ThirtyoneInchCornerRadius = new CulvertChartNumber("31 Inch corner radius; Corrugated metal", 36, CulvertScaleNumbers.Projecting1, CulvertScaleNumbers.NoBeveles, CulvertScaleNumbers.ThirtyThreeDegBevels);

	public static CulvertChartNumber ArchCorrugatedMetal41 = new CulvertChartNumber("Arch; Corrugated metal", 41, CulvertScaleNumbers.NinetyDegreeHeadwall, CulvertScaleNumbers.MiteredToSlope2, CulvertScaleNumbers.ThinWallProjecting3);

	public static CulvertChartNumber ArchCorrugatedMetal42 = new CulvertChartNumber("Arch; Corrugated metal", 42, CulvertScaleNumbers.NinetyDegreeHeadwall, CulvertScaleNumbers.MiteredToSlope2, CulvertScaleNumbers.ThinWallProjecting3);

	public static CulvertChartNumber ArchCorrugatedMetal43 = new CulvertChartNumber("Arch; Corrugated metal", 43, CulvertScaleNumbers.NinetyDegreeHeadwall, CulvertScaleNumbers.MiteredToSlope2, CulvertScaleNumbers.ThinWallProjecting3);

	public static CulvertChartNumber LowAndHighCorrugatedMetalArch = new CulvertChartNumber("Low and high corrugated metal arch", 52, CulvertScaleNumbers.MiteredToSlope1, CulvertScaleNumbers.ThinWallProjecting2, CulvertScaleNumbers.SquareEdges90DegreesHW, CulvertScaleNumbers.BeveledEdges90DegreesHW);

	public static CulvertChartNumber CircularCulvert = new CulvertChartNumber("Circular culvert", 55, CulvertScaleNumbers.SmoothTaperedInletThroat, CulvertScaleNumbers.RoughTaperedInletThroat);

	public static CulvertChartNumber EllipticalInletFace = new CulvertChartNumber("Elliptical inlet face", 56, CulvertScaleNumbers.TaperedInletBeveledEdges, CulvertScaleNumbers.TaperedInletSquareEdges, CulvertScaleNumbers.TaperedInletThinEdgeProjecting);

	public static CulvertChartNumber Rectangular = new CulvertChartNumber("Rectangular", 57, CulvertScaleNumbers.TaperedInletThroat);

	public static CulvertChartNumber RectangularConcrete58 = new CulvertChartNumber("Rectangular concerte", 58, CulvertScaleNumbers.SideTaperedLessFavorable, CulvertScaleNumbers.SideTaperedMoreFavorable);

	public static CulvertChartNumber RectangularConcrete59 = new CulvertChartNumber("Rectangular concrete", 59, CulvertScaleNumbers.SlopeTaperedLessFavorable, CulvertScaleNumbers.SlopeTaperedMoreFavorable);

	public static CulvertChartNumber SpanRiseRatioApproximate21 = new CulvertChartNumber("Span/Rise ratio approximate 2:1", 60, CulvertScaleNumbers.ZeroDegreeWingWallAngle, CulvertScaleNumbers.FortyFiveDegreeWingWallAngle, CulvertScaleNumbers.NinetyDegreeHeadwall);

	public static CulvertChartNumber SpanRiseRatioApproximate41 = new CulvertChartNumber("Span/Rise ratio approximate 4:1", 61, CulvertScaleNumbers.ZeroDegreeWingWallAngle, CulvertScaleNumbers.FortyFiveDegreeWingWallAngle, CulvertScaleNumbers.NinetyDegreeHeadwall);
}
