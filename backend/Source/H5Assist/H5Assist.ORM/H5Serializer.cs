namespace H5Assist.ORM;

public abstract class H5Serializer
{
	public abstract void Write(H5Writer hw, string group, string attrPrefix, object o);

	public abstract object Read(H5Reader hr, string group, string attrPrefix);
}
