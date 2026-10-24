using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

[StandardModule]
internal static class IPointsExtensions
{
	public static Profile ToProfile(this IPoints ipts, float[] values)
	{
		if (ipts.PointMCount() != values.Length)
		{
			return null;
		}
		Profile profile = new Profile("", "");
		float distanceToSum = 0f;
		checked
		{
			int count = ipts.PointMCount() - 1;
			PointM pointM2 = default;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = ipts.PointM(i);
				if (i != 0)
				{
					distanceToSum += (float)pointM2.DistanceTo(pointM);
				}
				profile.Add(distanceToSum, values[i]);
				pointM2 = pointM;
			}
			return profile;
		}
	}

	public static float[] ToStations(this IPoints ipts)
	{
		if (ipts == null)
		{
			return null;
		}
		if (ipts.PointMCount() == 0)
		{
			return new float[0];
		}
		checked
		{
			float[] floatValues = new float[ipts.PointMCount() - 1 + 1];
			float distanceToSum = 0f;
			int count = ipts.PointMCount() - 1;
			PointM pointM2 = default;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = ipts.PointM(i);
				if (i != 0)
				{
					distanceToSum += (float)pointM2.DistanceTo(pointM);
				}
				floatValues[i] = distanceToSum;
				pointM2 = pointM;
			}
			return floatValues;
		}
	}
}
