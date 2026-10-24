using System;

namespace RasMapperLib;

public class Affine : Transform
{
	private SegmentM _cur;

	private SegmentM _trans;

	public readonly double AngleRotationDegrees;

	private double _sinRotationAngle;

	private double _cosRotationAngle;

	private double _scale;

	public Affine(SegmentM curSegment, SegmentM transSegment)
	{
		_cur = curSegment;
		_trans = transSegment;
		double bearingTransSegmentTerm = transSegment.Bearing() - curSegment.Bearing();
		AngleRotationDegrees = bearingTransSegmentTerm * 180.0 / Math.PI;
		_sinRotationAngle = Math.Sin(AngleRotationDegrees);
		_cosRotationAngle = Math.Cos(AngleRotationDegrees);
		if (curSegment.Length > 0.0)
		{
			_scale = transSegment.Length / curSegment.Length;
		}
		else
		{
			_scale = 1.0;
		}
	}

	public override void TransformPointM(ref PointM PointM)
	{
		double pointMCurTerm = PointM.X - _cur.A.X;
		double pointMCurTerm2 = PointM.Y - _cur.A.Y;
		PointM.X = _trans.A.X + (pointMCurTerm * _cosRotationAngle - pointMCurTerm2 * _sinRotationAngle) * _scale;
		PointM.Y = _trans.A.Y + (pointMCurTerm * _sinRotationAngle + pointMCurTerm2 * _cosRotationAngle) * _scale;
	}

	public override Transform InverseTransform()
	{
		return new Affine(_trans, _cur);
	}
}
