namespace RasMapperLib;

public class CulvertScaleNumbers
{
	public static CulvertScaleNumber SquareEdgeEntranceWHW = new CulvertScaleNumber("Square edge entrance with headwall", 1);

	public static CulvertScaleNumber GrooveEndEntranceWHW = new CulvertScaleNumber("Groove end entrance with headwall", 2);

	public static CulvertScaleNumber GrooveEndEntrancePPFF = new CulvertScaleNumber("Groove end entrance; pipe projecting from fill", 3);

	public static CulvertScaleNumber Headwall = new CulvertScaleNumber("Headwall", 1);

	public static CulvertScaleNumber MiteredToConformToSlope = new CulvertScaleNumber("Mitered to conform to slope", 2);

	public static CulvertScaleNumber PipeProjectingFromFill = new CulvertScaleNumber("Pipe projecting from fill", 3);

	public static CulvertScaleNumber ASmallBevel = new CulvertScaleNumber("A Small bevel; b/D=0.042;a/D=0.063;c/D=0.042;d/D=0.083", 1);

	public static CulvertScaleNumber BLargeBevel = new CulvertScaleNumber("B Large bevel' b/D=0.083; a/D=0.125;c/D=0.042;d/D=0.125", 2);

	public static CulvertScaleNumber SmoothTaperedInletThroat = new CulvertScaleNumber("Smooth tapered inlet throat", 1);

	public static CulvertScaleNumber RoughTaperedInletThroat = new CulvertScaleNumber("Rough tapered inlet throat", 2);

	public static CulvertScaleNumber TaperedInletBeveledEdges = new CulvertScaleNumber("Tapered inlet; Beveled edges", 1);

	public static CulvertScaleNumber TaperedInletSquareEdges = new CulvertScaleNumber("Tapered inlet; Square edges", 2);

	public static CulvertScaleNumber TaperedInletThinEdgeProjecting = new CulvertScaleNumber("Tapered inlet; Thin edge projecting", 3);

	public static CulvertScaleNumber WingwallFlared30To75Deg = new CulvertScaleNumber("Wingwall flared 30 to 75 deg.", 1);

	public static CulvertScaleNumber WingwallFlared90Or15Deg = new CulvertScaleNumber("Wingwall flared 90 or 15 deg.", 2);

	public static CulvertScaleNumber WingwallFlared0Deg = new CulvertScaleNumber("Wingwall flared 0 deg. (sides extended straight)", 3);

	public static CulvertScaleNumber FortyfiveDegWingwallFlare = new CulvertScaleNumber("45 deg Wingwall flare; inlet top edge bevel=0.043D", 1);

	public static CulvertScaleNumber WingwallFlared18To33Deg = new CulvertScaleNumber("Wingwall flared 18 to 33.7 deg.; inlet top edge bevel=0.083D", 2);

	public static CulvertScaleNumber InletEdgesChamfered = new CulvertScaleNumber("Inlet edges chamfered 3/4 inch", 1);

	public static CulvertScaleNumber InletEdgesBeveledHalfInch = new CulvertScaleNumber("Inlet edges beveled 1/2 inch at 45 degrees (1:1)", 2);

	public static CulvertScaleNumber InletEdgesBeveled1Inch = new CulvertScaleNumber("Inlet edges beveled 1 in/ft at 33.7 degrees (1:1.5)", 3);

	public static CulvertScaleNumber HeadwallSkewed45DegInletChamfered = new CulvertScaleNumber("Headwall skewed 45 deg.; inlet edges chamfered 3/4 inch", 1);

	public static CulvertScaleNumber HeadwallSkewed30Deg = new CulvertScaleNumber("Headwall skewed 30 deg.; inlet edges chamfered 3/4 inch", 2);

	public static CulvertScaleNumber HeadwallSkewed15Deg = new CulvertScaleNumber("Headwall skewed 15 deg.; inlet edges chamfered 3/4 inch", 3);

	public static CulvertScaleNumber HeawallSkewed10To45Deg = new CulvertScaleNumber("Headwall skewed 10 to 45 deg.; inlet edges beveled", 4);

	public static CulvertScaleNumber WingwallsFlared45Deg = new CulvertScaleNumber("Wingwalls flared 45 deg. (1:1); inlet not skewed", 1);

	public static CulvertScaleNumber WingwallsFlared18Deg3To1 = new CulvertScaleNumber("Wingwalls flared 18.4 deg. (3:1); inlet not skewed", 2);

	public static CulvertScaleNumber WingwallsFlared18Deg1To1 = new CulvertScaleNumber("Wingwalls flared 18.4 deg. (1:1); inlet skewed 30 degrees", 3);

	public static CulvertScaleNumber WingwallsFlared45TopEdgeBezel = new CulvertScaleNumber("Wingwalls flared 45 deg. (1:1); inlet top edge bevel=0.042D", 1);

	public static CulvertScaleNumber WingwallsFlared33Deg = new CulvertScaleNumber("Wingwalls flared 33.7 deg. (1.5:1); inlet top edge bevel=0.083D", 2);

	public static CulvertScaleNumber WingwallsFlared18DegInletTopEdgeBez = new CulvertScaleNumber("Wingwalls flared 18.4 deg. (3:1); inlet top edge bevel=0.083D", 3);

	public static CulvertScaleNumber NinetyDegreeHeadwall = new CulvertScaleNumber("90 degree headwall", 1);

	public static CulvertScaleNumber ThickWallProjecting = new CulvertScaleNumber("Thick wall projecting", 2);

	public static CulvertScaleNumber ThinWallProjecting3 = new CulvertScaleNumber("Thin wall projecting", 3);

	public static CulvertScaleNumber TaperedInletThroat = new CulvertScaleNumber("Tapered inlet throat", 1);

	public static CulvertScaleNumber SideTaperedLessFavorable = new CulvertScaleNumber("Side tapered; Less favorable edges", 1);

	public static CulvertScaleNumber SideTaperedMoreFavorable = new CulvertScaleNumber("Side tapered; More favorable edges", 2);

	public static CulvertScaleNumber SlopeTaperedLessFavorable = new CulvertScaleNumber("Slope tapered; Less favorable edges", 1);

	public static CulvertScaleNumber SlopeTaperedMoreFavorable = new CulvertScaleNumber("Slope tapered; More favorable edges", 2);

	public static CulvertScaleNumber MiteredToSlope2 = new CulvertScaleNumber("Mitered to slope", 2);

	public static CulvertScaleNumber Projecting3 = new CulvertScaleNumber("Projecting", 3);

	public static CulvertScaleNumber Projecting1 = new CulvertScaleNumber("Projecting", 1);

	public static CulvertScaleNumber NoBeveles = new CulvertScaleNumber("No bevels", 2);

	public static CulvertScaleNumber ThirtyThreeDegBevels = new CulvertScaleNumber("33.7 degree bevels", 3);

	public static CulvertScaleNumber SquareEdgeWithHeadwall = new CulvertScaleNumber("Square edge with headwall", 1);

	public static CulvertScaleNumber GroovedEndWithHeadwall = new CulvertScaleNumber("Grooved end with headwall", 2);

	public static CulvertScaleNumber GroovedEndProjecting = new CulvertScaleNumber("Grooved end projecting", 3);

	public static CulvertScaleNumber MiteredToSlope1 = new CulvertScaleNumber("Mitered to slope", 1);

	public static CulvertScaleNumber ThinWallProjecting2 = new CulvertScaleNumber("Thin wall projecting", 2);

	public static CulvertScaleNumber SquareEdges90DegreesHW = new CulvertScaleNumber("Square edges; 90 Degree headwall", 3);

	public static CulvertScaleNumber BeveledEdges90DegreesHW = new CulvertScaleNumber("Beveled edges; 90 Degree headwall", 4);

	public static CulvertScaleNumber ZeroDegreeWingWallAngle = new CulvertScaleNumber("0 degree wing wall angle", 1);

	public static CulvertScaleNumber FortyFiveDegreeWingWallAngle = new CulvertScaleNumber("45 degree wing wall angle", 2);

	public static CulvertScaleNumber NinetyDegreeWingWallAngle = new CulvertScaleNumber("90 degree wing wall angle", 3);
}
