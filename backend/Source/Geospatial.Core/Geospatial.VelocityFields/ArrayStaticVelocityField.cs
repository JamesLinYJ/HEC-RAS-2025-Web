using System;
using System.IO;
using Geospatial.Vectors;

namespace Geospatial.VelocityFields;

public class ArrayStaticVelocityField : IStaticVelocityField
{
	private Extent _bbox;

	private Vector[,] _velocities;

	public ArrayStaticVelocityField(Vector[,] velocities, Extent viewWindow)
	{
		_velocities = velocities;
		_bbox = viewWindow;
	}

	public Vector GetVelocity(Point point)
	{
		if (_bbox.IsOOB(point))
		{
			return Vector.NullVelocity;
		}
		float pointLeftTerm = (float)((point.X - _bbox.Left) / _bbox.Width);
		float topBboxTerm = (float)((_bbox.Top - point.Y) / _bbox.Height);
		int getLengthVelocitiesTerm = (int)Math.Floor(pointLeftTerm * (float)_velocities.GetLength(1));
		int getLengthVelocitiesTerm2 = (int)Math.Floor(topBboxTerm * (float)_velocities.GetLength(0));
		getLengthVelocitiesTerm = Math.Min(Math.Max(getLengthVelocitiesTerm, 0), _velocities.GetLength(1) - 1);
		getLengthVelocitiesTerm2 = Math.Min(Math.Max(getLengthVelocitiesTerm2, 0), _velocities.GetLength(0) - 1);
		return _velocities[getLengthVelocitiesTerm2, getLengthVelocitiesTerm];
	}

	public void ExportFLT(string filenameVX, string filenameVY)
	{
		File.Delete(filenameVX);
		File.Delete(filenameVY);
		using FileStream output = new FileStream(filenameVX, FileMode.CreateNew);
		using FileStream output2 = new FileStream(filenameVY, FileMode.CreateNew);
		using BinaryWriter binaryWriter = new BinaryWriter(output);
		using BinaryWriter binaryWriter2 = new BinaryWriter(output2);
		for (int i = 0; i < _velocities.GetLength(0); i++)
		{
			for (int j = 0; j < _velocities.GetLength(1); j++)
			{
				binaryWriter.Write(_velocities[i, j].X);
				binaryWriter2.Write(_velocities[i, j].Y);
			}
		}
	}
}
