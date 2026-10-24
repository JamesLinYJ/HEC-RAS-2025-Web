namespace RasMapperLib;

public class BankProperties
{
	public readonly float LeftBankStation;

	public readonly float LeftBankFraction;

	public readonly PointOnPolyline LeftBankPoint;

	public readonly float RightBankStation;

	public readonly float RightBankFraction;

	public readonly PointOnPolyline RightBankPoint;

	public float CenterlineStation;

	public BankProperties(float LBStation, float LBFraction, PointOnPolyline LBPoint, float RBStation, float RBFraction, PointOnPolyline RBPoint)
	{
		LeftBankStation = LBStation;
		LeftBankFraction = LBFraction;
		LeftBankPoint = LBPoint;
		RightBankStation = RBStation;
		RightBankFraction = RBFraction;
		RightBankPoint = RBPoint;
		CenterlineStation = (RBStation + LBStation) / 2f;
	}

	public BankProperties(float LBStation, float LBFraction, PointOnPolyline LBPoint, float RBStation, float RBFraction, PointOnPolyline RBPoint, float centerStation)
	{
		LeftBankStation = LBStation;
		LeftBankFraction = LBFraction;
		LeftBankPoint = LBPoint;
		RightBankStation = RBStation;
		RightBankFraction = RBFraction;
		RightBankPoint = RBPoint;
		CenterlineStation = centerStation;
	}

	public static BankProperties Empty()
	{
		return new BankProperties(0f, 0f, new PointOnPolyline(default, 0, 0.0), 0f, 0f, new PointOnPolyline(default, 0, 0.0));
	}

	public Point2DCollection ProcessProfileIntoMValues(Point2DCollection p2d)
	{
		Point2DCollection point2DCollection = new Point2DCollection(p2d.Count);
		checked
		{
			int count = p2d.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Point2D point2D = p2d[i];
				if (point2D.X == 0f)
				{
					point2DCollection.Add(new Point2D(-2f, point2D.Y));
				}
				else if (point2D.X < LeftBankStation)
				{
					float leftBankStationPoint2DTerm = 1f - (LeftBankStation - point2D.X) / LeftBankStation;
					point2DCollection.Add(new Point2D(leftBankStationPoint2DTerm - 2f, point2D.Y));
				}
				else if (point2D.X == LeftBankStation)
				{
					point2DCollection.Add(new Point2D(-1f, point2D.Y));
				}
				else if (point2D.X < RightBankStation)
				{
					float leftBankStationPoint2DTerm2 = 1f - (LeftBankStation - point2D.X) / LeftBankStation;
					point2DCollection.Add(new Point2D(leftBankStationPoint2DTerm2 - 2f, point2D.Y));
				}
				else
				{
					_ = point2D.X;
					_ = RightBankStation;
				}
			}
			return point2DCollection;
		}
	}
}
