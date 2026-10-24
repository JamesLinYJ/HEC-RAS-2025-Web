using Utility.Attributes;

namespace Ras.Hydraulics.Culverts;

public enum BarrelOpeningType
{
	[ChartScale(-1, -1)]
	[Alias("Custom")]
	[IsDebugOnly]
	Custom,
	[ChartScale(-1, -1)]
	[Alias("None")]
	None,
	[ChartScale(1, 1)]
	[Alias("1:1")]
	ConcretePipeCulvert_SquareEdgeWithHeadwall,
	[ChartScale(1, 2)]
	[Alias("1:2")]
	ConcretePipeCulvert_GrooveEndWithHeadwall,
	[ChartScale(1, 3)]
	[Alias("1:3")]
	ConcretePipeCulvert_GrooveEndProjectingFromFill,
	[ChartScale(2, 1)]
	[Alias("2:1")]
	CorrugatedMetalPipeCulvert_Headwall,
	[ChartScale(2, 2)]
	[Alias("2:2")]
	CorrugatedMetalPipeCulvert_MiteredToSlope,
	[ChartScale(2, 3)]
	[Alias("2:3")]
	CorrugatedMetalPipeCulvert_ProjectingFromFill,
	[ChartScale(3, 1)]
	[Alias("3:1")]
	ConcretePipeCulvert_BevelSmall,
	[ChartScale(3, 2)]
	[Alias("3:2")]
	ConcretePipeCulvert_BevelLarge,
	[ChartScale(8, 1)]
	[Alias("8:1")]
	BoxCulvert_FlaredWingwalls30to75,
	[ChartScale(8, 2)]
	[Alias("8:2")]
	BoxCulvert_FlaredWingwalls90or15,
	[ChartScale(8, 3)]
	[Alias("8:3")]
	BoxCulvert_FlaredWingwalls0Straight,
	[ChartScale(9, 1)]
	[Alias("9:1")]
	BoxCulvert_Flared45_Bevel0_043D,
	[ChartScale(9, 2)]
	[Alias("9:2")]
	BoxCulvert_Flared18to33_7_Bevel0_083D,
	[ChartScale(10, 1)]
	[Alias("10:1")]
	BoxCulvert_Headwall90_Chamfer3_4Inch,
	[ChartScale(10, 2)]
	[Alias("10:2")]
	BoxCulvert_Headwall90_Bevel2InPerFt_45deg,
	[ChartScale(10, 3)]
	[Alias("10:3")]
	BoxCulvert_Headwall90_Bevel1InPerFt_33_7deg,
	[ChartScale(11, 1)]
	[Alias("11:1")]
	BoxCulvert_Skew45_Chamfer3_4Inch,
	[ChartScale(11, 2)]
	[Alias("11:2")]
	BoxCulvert_Skew30_Chamfer3_4Inch,
	[ChartScale(11, 3)]
	[Alias("11:3")]
	BoxCulvert_Skew15_Chamfer3_4Inch,
	[ChartScale(11, 4)]
	[Alias("11:4")]
	BoxCulvert_Skew10to45_BeveledEdges,
	[ChartScale(12, 1)]
	[Alias("12:1")]
	BoxCulvert_NonOffset_Flared45_NotSkewed,
	[ChartScale(12, 2)]
	[Alias("12:2")]
	BoxCulvert_NonOffset_Flared18_4_NotSkewed,
	[ChartScale(12, 3)]
	[Alias("12:3")]
	BoxCulvert_NonOffset_Flared18_4_Skew30,
	[ChartScale(13, 1)]
	[Alias("13:1")]
	BoxCulvert_Offset_Flared45_Bevel0_042D,
	[ChartScale(13, 2)]
	[Alias("13:2")]
	BoxCulvert_Offset_Flared33_7_Bevel0_083D,
	[ChartScale(13, 3)]
	[Alias("13:3")]
	BoxCulvert_Offset_Flared18_4_Bevel0_083D,
	[ChartScale(16, 1)]
	[Alias("16:1")]
	CorrugatedMetalBoxCulvert_Headwall90,
	[ChartScale(16, 2)]
	[Alias("16:2")]
	CorrugatedMetalBoxCulvert_ThickWallProjecting,
	[ChartScale(16, 3)]
	[Alias("16:3")]
	CorrugatedMetalBoxCulvert_ThinWallProjecting,
	[ChartScale(29, 1)]
	[Alias("29:1")]
	HorizontalEllipseConcrete_SquareEdgeHeadwall,
	[ChartScale(29, 2)]
	[Alias("29:2")]
	HorizontalEllipseConcrete_GroovedEndHeadwall,
	[ChartScale(29, 3)]
	[Alias("29:3")]
	HorizontalEllipseConcrete_GroovedEndProjecting,
	[ChartScale(30, 1)]
	[Alias("30:1")]
	VerticalEllipseConcrete_SquareEdgeHeadwall,
	[ChartScale(30, 2)]
	[Alias("30:2")]
	VerticalEllipseConcrete_GroovedEndHeadwall,
	[ChartScale(30, 3)]
	[Alias("30:3")]
	VerticalEllipseConcrete_GroovedEndProjecting,
	[ChartScale(34, 1)]
	[Alias("34:1")]
	PipeArch18CorrMetal_Headwall90,
	[ChartScale(34, 2)]
	[Alias("34:2")]
	PipeArch18CorrMetal_MiteredToSlope,
	[ChartScale(34, 3)]
	[Alias("34:3")]
	PipeArch18CorrMetal_Projecting,
	[ChartScale(35, 1)]
	[Alias("35:1")]
	PipeArch18CorrMetal_ProjectingOnly,
	[ChartScale(35, 2)]
	[Alias("35:2")]
	PipeArch18CorrMetal_NoBevels,
	[ChartScale(35, 3)]
	[Alias("35:3")]
	PipeArch18CorrMetal_Bevel33_7deg,
	[ChartScale(36, 1)]
	[Alias("36:1")]
	PipeArch31CorrMetal_ProjectingOnly,
	[ChartScale(36, 2)]
	[Alias("36:2")]
	PipeArch31CorrMetal_NoBevels,
	[ChartScale(36, 3)]
	[Alias("36:3")]
	PipeArch31CorrMetal_Bevel33_7deg,
	[ChartScale(41, 1)]
	[Alias("41:1")]
	CorrugatedMetalLowProfileArch_Headwall90,
	[ChartScale(41, 2)]
	[Alias("41:2")]
	CorrugatedMetalLowProfileArch_MiteredToSlope,
	[ChartScale(41, 3)]
	[Alias("41:3")]
	CorrugatedMetalLowProfileArch_ThinWallProjecting,
	[ChartScale(55, 1)]
	[Alias("55:1")]
	CircularCulvert_SmoothTaperedInletThroat,
	[ChartScale(55, 2)]
	[Alias("55:2")]
	CircularCulvert_RoughTaperedInletThroat,
	[ChartScale(56, 1)]
	[Alias("56:1")]
	EllipticalInletFace_TaperedBeveledEdges,
	[ChartScale(56, 2)]
	[Alias("56:2")]
	EllipticalInletFace_TaperedSquareEdges,
	[ChartScale(56, 3)]
	[Alias("56:3")]
	EllipticalInletFace_TaperedThinEdgeProjecting,
	[ChartScale(57, 1)]
	[Alias("57:1")]
	Rectangular_TaperedInletThroat,
	[ChartScale(58, 1)]
	[Alias("58:1")]
	RectangularConcrete_SideTaperedLessFavorableEdges,
	[ChartScale(58, 2)]
	[Alias("58:2")]
	RectangularConcrete_SideTaperedMoreFavorableEdges,
	[ChartScale(59, 1)]
	[Alias("59:1")]
	RectangularConcrete_SlopeTaperedLessFavorableEdges,
	[ChartScale(59, 2)]
	[Alias("59:2")]
	RectangularConcrete_SlopeTaperedMoreFavorableEdges,
	[ChartScale(60, 1)]
	[Alias("60:1")]
	ConSpan2to1_Wingwall0deg,
	[ChartScale(60, 2)]
	[Alias("60:2")]
	ConSpan2to1_Wingwall45deg,
	[ChartScale(60, 3)]
	[Alias("60:3")]
	ConSpan2to1_Wingwall90deg,
	[ChartScale(61, 1)]
	[Alias("61:1")]
	ConSpan4to1_Wingwall0deg,
	[ChartScale(61, 2)]
	[Alias("61:2")]
	ConSpan4to1_Wingwall45deg,
	[ChartScale(61, 3)]
	[Alias("61:3")]
	ConSpan4to1_Wingwall90deg
}
