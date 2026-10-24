using Geospatial.Vectors;
using Ras.Engine.Structures.Culverts;
using Ras.Engine.Structures.Weirs;

namespace Ras.Synthetics;

public static class StructureFactories
{
	public static Culvert GenerateCulvert(Polyline centerLine, double usInvert, double dsInvert, CulvertShape shape)
	{
		Culvert culvert = new Culvert("TestCulvert1", centerLine);
		CulvertPhysProps physProps = culvert.PhysProps;
		physProps.InvertUS = usInvert;
		physProps.InvertDS = dsInvert;
		physProps.Mann = 0.025;
		physProps.CoefEntranceLoss = 0.5;
		physProps.CoefExitLoss = 1.0;
		physProps.CoefEntranceLossReverse = 0.5;
		physProps.CoefExitLossReverse = 1.0;
		physProps.Shape = shape;
		switch (shape)
		{
		case CulvertShape.Circular:
			physProps.FullRise = 2.0;
			physProps.Span = 0.0;
			physProps.InletCoefUS.K = 0.034;
			physProps.InletCoefUS.M = 1.5;
			physProps.InletCoefUS.C = 0.0553;
			physProps.InletCoefUS.Y = 0.54;
			physProps.InletCoefUS.SlopeCorrection = -0.5;
			physProps.InletCoefUS.EqnForm = 1;
			break;
		case CulvertShape.Box:
			physProps.FullRise = 2.0;
			physProps.Span = 2.0;
			physProps.InletCoefUS.K = 0.034;
			physProps.InletCoefUS.M = 1.5;
			physProps.InletCoefUS.C = 0.0496;
			physProps.InletCoefUS.Y = 0.57;
			physProps.InletCoefUS.SlopeCorrection = -0.5;
			physProps.InletCoefUS.EqnForm = 1;
			break;
		case CulvertShape.SemiCircular:
			physProps.FullRise = 2.0;
			physProps.Span = 4.0;
			physProps.InletCoefUS.K = 0.0083;
			physProps.InletCoefUS.M = 2.0;
			physProps.InletCoefUS.C = 0.0379;
			physProps.InletCoefUS.Y = 0.69;
			physProps.InletCoefUS.SlopeCorrection = -0.5;
			physProps.InletCoefUS.EqnForm = 1;
			break;
		case CulvertShape.Ellipse:
			physProps.FullRise = 2.0;
			physProps.Span = 4.0;
			physProps.InletCoefUS.K = 0.0018;
			physProps.InletCoefUS.M = 2.5;
			physProps.InletCoefUS.C = 0.0292;
			physProps.InletCoefUS.Y = 0.74;
			physProps.InletCoefUS.SlopeCorrection = -0.5;
			physProps.InletCoefUS.EqnForm = 1;
			break;
		default:
			physProps.FullRise = 2.0;
			physProps.Span = 3.2334797;
			physProps.InletCoefUS.K = 0.0083;
			physProps.InletCoefUS.M = 2.0;
			physProps.InletCoefUS.C = 0.0379;
			physProps.InletCoefUS.Y = 0.69;
			physProps.InletCoefUS.SlopeCorrection = -0.5;
			physProps.InletCoefUS.EqnForm = 1;
			break;
		}
		physProps.InletCoefDS = physProps.InletCoefUS.Clone();
		return culvert;
	}

	public static Weir GenerateWeir(Polyline centerLine)
	{
		return new Weir("TestWeir1", centerLine)
		{
			PhysProps = 
			{
				WeirCoef = 3.33
			}
		};
	}
}
