using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Utility.Systems;

namespace RasMapperLib;

[DebuggerDisplay("({X}, {Y}, {Z}, {M})")]
public struct PointM
{
	public double X;

	public double Y;

	public double Z;

	public double M;

	public static PointM NullPoint()
	{
		return new PointM(double.NaN, double.NaN);
	}

	public bool IsNull()
	{
		if (!double.IsNaN(X))
		{
			return double.IsNaN(Y);
		}
		return true;
	}

	public PointM(double ax, double ay)
	{
		this = default;
		X = ax;
		Y = ay;
		Z = double.NaN;
		M = double.NaN;
	}

	public PointM(Point2Double pt2D)
		: this(pt2D.X, pt2D.Y)
	{
	}

	public PointM(Point2D pt2D)
		: this(pt2D.X, pt2D.Y)
	{
	}

	public PointM(double ax, double ay, double az)
	{
		this = default;
		X = ax;
		Y = ay;
		Z = az;
		M = double.NaN;
	}

	public PointM(double px, double py, double pz, double pm)
	{
		this = default;
		X = px;
		Y = py;
		Z = pz;
		M = pm;
	}

	public void Assign(double ax, double ay)
	{
		X = ax;
		Y = ay;
		Z = double.NaN;
		M = double.NaN;
	}

	public void Assign(double ax, double ay, double az)
	{
		X = ax;
		Y = ay;
		Z = az;
		M = double.NaN;
	}

	public void Assign(double px, double py, double pz, double pm)
	{
		X = px;
		Y = py;
		Z = pz;
		M = pm;
	}

	public void Assign(string delimitedString, char delimeter)
	{
		string[] parts = delimitedString.Split(delimeter);
		X = double.Parse(parts[0]);
		Y = double.Parse(parts[1]);
		if (parts.Length > 2)
		{
			Z = double.Parse(parts[2]);
		}
		else
		{
			Z = double.NaN;
		}
		if (parts.Length > 3)
		{
			M = double.Parse(parts[3]);
		}
		else
		{
			M = double.NaN;
		}
	}

	public static bool TryParse(string commaDelimitedStr, ref PointM point)
	{
		if (string.IsNullOrEmpty(commaDelimitedStr))
		{
			return false;
		}
		string[] parts = commaDelimitedStr.Split(',');
		if (parts.Length < 2)
		{
			return false;
		}
		if (!double.TryParse(parts[0], out var result))
		{
			return false;
		}
		if (!double.TryParse(parts[1], out var result2))
		{
			return false;
		}
		if (parts.Length <= 2 || !double.TryParse(parts[2], out var result3))
		{
			result3 = double.NaN;
		}
		if (parts.Length <= 3 || !double.TryParse(parts[3], out var result4))
		{
			result4 = double.NaN;
		}
		point = new PointM(result, result2, result3, result4);
		return true;
	}

	public void SetToNaN()
	{
		X = double.NaN;
		Y = double.NaN;
		Z = double.NaN;
		M = double.NaN;
	}

	public void RemoveZM()
	{
		Z = double.NaN;
		M = double.NaN;
	}

	public static PointM Undefined()
	{
		return new PointM(double.NaN, double.NaN);
	}

	public bool IsDefined()
	{
		if (double.IsNaN(X) || double.IsNaN(Y))
		{
			return false;
		}
		return true;
	}

	public bool IsUndefined()
	{
		if (double.IsNaN(X) || double.IsNaN(Y))
		{
			return true;
		}
		return false;
	}

	public bool Equal(PointM PointM)
	{
		if (PointM.X == X && PointM.Y == Y && PointM.Z == Z && PointM.M == M)
		{
			return true;
		}
		return false;
	}

	public bool EqualXY(PointM PointM)
	{
		if (X == PointM.X && Y == PointM.Y)
		{
			return true;
		}
		return false;
	}

	public bool EqualXYZ(PointM ptm)
	{
		if (X == ptm.X && Y == ptm.Y && Z == ptm.Z)
		{
			return true;
		}
		return false;
	}

	public bool EqualXYZMWithNaN(PointM ptm)
	{
		if (X == ptm.X && Y == ptm.Y && (Z == ptm.Z || (double.IsNaN(Z) && double.IsNaN(ptm.Z))) && (M == ptm.M || (double.IsNaN(M) && double.IsNaN(ptm.M))))
		{
			return true;
		}
		return false;
	}

	public bool EqualXY(double x1, double y1)
	{
		if (X == x1 && Y == y1)
		{
			return true;
		}
		return false;
	}

	public double DistanceTo(PointM ptM)
	{
		return Math.Pow(Math.Pow(X - ptM.X, 2.0) + Math.Pow(Y - ptM.Y, 2.0), 0.5);
	}

	public double DistanceTo(Point2Double point)
	{
		return Math.Sqrt(Math.Pow(X - point.X, 2.0) + Math.Pow(Y - point.Y, 2.0));
	}

	public double DistanceTo(double px, double py)
	{
		return Math.Sqrt(Math.Pow(X - px, 2.0) + Math.Pow(Y - py, 2.0));
	}

	public double DistanceZTo(PointM ptM)
	{
		return DistanceZTo(ptM.X, ptM.Y, ptM.Z);
	}

	public double DistanceZTo(double px, double py, double pz)
	{
		return Math.Sqrt(Math.Pow(px - X, 2.0) + Math.Pow(py - Y, 2.0) + Math.Pow(pz - Z, 2.0));
	}

	public PointM AtFractionBetween(PointM ptB, double t)
	{
		return new PointM(X + (ptB.X - X) * t, Y + (ptB.Y - Y) * t);
	}

	public double GetAngleDegreesTo(PointM ptM)
	{
		return Math.Atan2(ptM.Y - Y, ptM.X - X) * 180.0 / Math.PI;
	}

	public bool CloseTo(PointM point, double distance)
	{
		return CloseTo(point.X, point.Y, distance);
	}

	public bool CloseTo(double px, double py, double distance)
	{
		double pxTerm = Math.Abs(X - px);
		if (pxTerm > distance)
		{
			return false;
		}
		double pyTerm = Math.Abs(Y - py);
		if (pyTerm > distance)
		{
			return false;
		}
		if (Math.Sqrt(Math.Pow(pxTerm, 2.0) + Math.Pow(pyTerm, 2.0)) > distance)
		{
			return false;
		}
		return true;
	}

	public bool CloseTo(double px, double py, double distanceX, double distanceY)
	{
		if (Math.Abs(X - px) > distanceX || Math.Abs(Y - py) > distanceY)
		{
			return false;
		}
		return true;
	}

	public bool HasZ()
	{
		return !double.IsNaN(Z);
	}

	public bool HasM()
	{
		return !double.IsNaN(M);
	}

	public PointM Ratio(double factor)
	{
		return new PointM(X * factor, Y * factor, Z * factor, M * factor);
	}

	public PointM SwapZM()
	{
		return new PointM(X, Y, M, Z);
	}

	public PointM SetZ(double newZ)
	{
		return new PointM(X, Y, newZ, M);
	}

	public PointM SetM(double newM)
	{
		return new PointM(X, Y, Z, newM);
	}

	public PointM SetZM(double newZ, double newM)
	{
		return new PointM(X, Y, newZ, newM);
	}

	public PointM Delta(PointM ptM)
	{
		return new PointM(ptM.X - X, ptM.Y - Y, ptM.Z - Z, ptM.M - M);
	}

	public PointM Translate(double deltaX, double deltaY)
	{
		return new PointM(X + deltaX, Y + deltaY, Z, M);
	}

	public PointM Translate(PointM delta)
	{
		return new PointM(X + delta.X, Y + delta.Y, Z + delta.Z, M + delta.M);
	}

	public PointM MidPoint(PointM ptm)
	{
		return new PointM((X + ptm.X) * 0.5, (Y + ptm.Y) * 0.5, (Z + ptm.Z) * 0.5, (M + ptm.M) * 0.5);
	}

	public PointM NegateXY()
	{
		return new PointM(0.0 - X, 0.0 - Y, Z, M);
	}

	public double VectorLength()
	{
		return Math.Pow(Math.Pow(X, 2.0) + Math.Pow(Y, 2.0), 0.5);
	}

	public PointM UnitVector()
	{
		double vectorLength = VectorLength();
		double px;
		double py;
		if (vectorLength > 0.0)
		{
			px = X / vectorLength;
			py = Y / vectorLength;
		}
		else
		{
			px = 0.0;
			py = 0.0;
		}
		return new PointM(px, py, 0.0, 0.0);
	}

	public PointM UnitVectorZ()
	{
		double computed = Math.Pow(Math.Pow(X, 2.0) + Math.Pow(Y, 2.0) + Math.Pow(Z, 2.0), 0.5);
		double px;
		double py;
		double pz;
		if (computed > 0.0)
		{
			px = X / computed;
			py = Y / computed;
			pz = Z / computed;
		}
		else
		{
			px = 0.0;
			py = 0.0;
			pz = 0.0;
		}
		return new PointM(px, py, pz, 0.0);
	}

	public PointM Add(PointM vector)
	{
		return new PointM(X + vector.X, Y + vector.Y, Z + vector.Z, M + vector.M);
	}

	public PointM Subtract(PointM vector)
	{
		return new PointM(X - vector.X, Y - vector.Y, Z - vector.Z, M - vector.M);
	}

	public static void Sort3PointsByY(ref PointM a, ref PointM b, ref PointM c)
	{
		if (!(a.Y <= b.Y) || !(b.Y <= c.Y))
		{
			if (a.Y <= c.Y && c.Y <= b.Y)
			{
				Swap(ref b, ref c);
			}
			else if (b.Y <= a.Y && a.Y <= c.Y)
			{
				Swap(ref a, ref b);
			}
			else if (b.Y <= c.Y && c.Y <= a.Y)
			{
				Swap(ref a, ref b);
				Swap(ref b, ref c);
			}
			else if (c.Y <= a.Y && a.Y <= b.Y)
			{
				Swap(ref a, ref b);
				Swap(ref a, ref c);
			}
			else
			{
				Swap(ref a, ref c);
			}
		}
	}

	public static void Swap(ref PointM a, ref PointM b)
	{
		PointM pointM = a;
		a = b;
		b = pointM;
	}

	public override string ToString()
	{
		return ToCSString();
	}

	public string ToCSString()
	{
		return ToSString(",", "", "");
	}

	public string ToCSString(string xyFormat, string zmFormat)
	{
		return ToSString(",", xyFormat, zmFormat);
	}

	public string ToSSString()
	{
		return ToSString(" ", "", "");
	}

	public string ToSSString(string xyFormat, string zmFormat)
	{
		return ToSString(" ", xyFormat, zmFormat);
	}

	public string ToTabSString()
	{
		return ToSString("\t", "", "");
	}

	public string ToSString(string seperatorCharacter, string xyFormat, string zmFormat, bool includeZ = true, bool includeM = true)
	{
		string xyFormatSeperatorCharacterTerm = X.ToString(xyFormat) + seperatorCharacter + Y.ToString(xyFormat);
		bool condition = false;
		if (HasZ() && includeZ)
		{
			xyFormatSeperatorCharacterTerm = xyFormatSeperatorCharacterTerm + seperatorCharacter + Z.ToString(zmFormat);
			condition = true;
		}
		if (HasM() && includeM)
		{
			if (!condition)
			{
				xyFormatSeperatorCharacterTerm += seperatorCharacter;
			}
			xyFormatSeperatorCharacterTerm = xyFormatSeperatorCharacterTerm + seperatorCharacter + M.ToString(zmFormat);
		}
		return xyFormatSeperatorCharacterTerm;
	}

	public static PointM FromSString(string replace)
	{
		replace = replace.Replace(" ", string.Empty);
		bool condition = false;
		int ordinal = 0;
		StringBuilder stringBuilder = new StringBuilder("");
		string textValue = "-.0123456789";
		double result = double.NaN;
		double result2 = double.NaN;
		double result3 = double.NaN;
		double result4 = double.NaN;
		checked
		{
			int count = replace.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				if (textValue.Contains(replace[i]))
				{
					condition = true;
				}
				if (!condition)
				{
					continue;
				}
				if (!textValue.Contains(replace[i]))
				{
					condition = false;
					switch (ordinal)
					{
					case 0:
						if (!double.TryParse(stringBuilder.ToString(), out result))
						{
							result = double.NaN;
						}
						break;
					case 1:
						if (!double.TryParse(stringBuilder.ToString(), out result2))
						{
							result2 = double.NaN;
						}
						break;
					case 2:
						if (!double.TryParse(stringBuilder.ToString(), out result3))
						{
							result3 = double.NaN;
						}
						break;
					case 3:
						if (!double.TryParse(stringBuilder.ToString(), out result4))
						{
							result4 = double.NaN;
						}
						break;
					}
					ordinal++;
				}
				else
				{
					stringBuilder.Append(replace[i]);
				}
			}
			return new PointM(result, result2, result3, result4);
		}
	}

	public static int CompareM(PointM a, PointM b)
	{
		if (a.M < b.M)
		{
			return -1;
		}
		if (a.M == b.M)
		{
			return 0;
		}
		return 1;
	}

	public static int CompareZ(PointM a, PointM b)
	{
		if (a.Z < b.Z)
		{
			return -1;
		}
		if (a.Z == b.Z)
		{
			return 0;
		}
		return 1;
	}

	public static void Stream(BinaryWriter bw, IList<PointM> ptms)
	{
		if (ptms == null)
		{
			double[] arr = new double[0];
			Pipe.WriteArray(bw, arr);
			return;
		}
		checked
		{
			double[] doubleValues = new double[ptms.Count * 2 - 1 + 1];
			int count = ptms.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				doubleValues[i * 2] = ptms[i].X;
				doubleValues[i * 2 + 1] = ptms[i].Y;
			}
			Pipe.WriteArray(bw, doubleValues);
		}
	}

	public static PointM[] Stream(BinaryReader br)
	{
		double[] readArray = Pipe.ReadArray<double>(br);
		if (readArray.Count() == 0)
		{
			return new PointM[0];
		}
		checked
		{
			PointM[] pointMs = new PointM[(int)Math.Round((double)readArray.Count() / 2.0) - 1 + 1];
			int count = pointMs.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				pointMs[i] = new PointM(readArray[i * 2], readArray[i * 2 + 1]);
			}
			return pointMs;
		}
	}
}
