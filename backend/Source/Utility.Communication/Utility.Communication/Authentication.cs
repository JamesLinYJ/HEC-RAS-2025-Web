namespace Utility.Communication;

public class Authentication
{
	public string UserName { get; set; }

	public string Password { get; set; }

	public static Authentication Default()
	{
		return new Authentication();
	}

	public Authentication Clone()
	{
		return new Authentication
		{
			UserName = UserName,
			Password = Password
		};
	}
}
