namespace Drawbridge.Api.Services
{
    public class ApiSettings
    {
        public string  AwsRegion        { get; set; } = "us-east-2";
        public string? SqsQueueUrl      { get; set; }
        public string  ProductsTable    { get; set; } = "drawbridge-products-prod";
        public string  VersionsTable    { get; set; } = "drawbridge-versions-prod";
        public string  JobsTable        { get; set; } = "drawbridge-jobs-prod";
        public string  AnnotationsTable { get; set; } = "drawbridge-annotations-prod";
        public string  MarkupsTable     { get; set; } = "drawbridge-markups-prod";
        public string? S3Bucket         { get; set; }
        public string? CloudFrontBaseUrl { get; set; }
        public string? ApsClientId      { get; set; }
        public string? ApsClientSecret  { get; set; }
        public string  CognitoUserPoolId { get; set; } = "";
        public string  CognitoClientId   { get; set; } = "";
    }
}
