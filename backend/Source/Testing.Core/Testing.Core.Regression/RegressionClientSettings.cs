namespace Testing.Core.Regression;

public class RegressionClientSettings
{
	public const string DefaultServerUrl = "https://rastesting-api.hecdev.net";

	public string BaseUrl { get; set; }

	public string S3Server { get; set; }

	public string Bucket { get; set; }

	public string S3Key { get; set; }

	public string S3Secret { get; set; }
}
