using System.Collections.Generic;

namespace RasMapperLib;

public interface IEditableFeature
{
	void TransformFeature(Transform transformer);

	void TransformPoint(Transform transformer, int index);

	void TransFormPoints(Transform transformer, IList<int> indexes);

	void DeletePoints(IList<int> indexes);

	void DeletePoints(int start, int count);

	void InsertPoint(PointM point, int index);

	void InsertPoints(int startIdx, IList<PointM> points);

	void InsertPoints(IList<int> sortedIndexes, IList<PointM> points);

	PointM Point(int index);

	PointM[] GetPoints(int[] indices);

	Feature Save();
}
