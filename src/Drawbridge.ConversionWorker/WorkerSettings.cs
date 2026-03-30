namespace Drawbridge.ConversionWorker
{
    public class WorkerSettings
    {
        public string AwsRegion           { get; set; } = "us-east-2";
        public string SqsQueueUrl         { get; set; } = "";
        public string S3Bucket            { get; set; } = "";
        public string DynamoProductsTable { get; set; } = "drawbridge-products-prod";
        public string DynamoVersionsTable { get; set; } = "drawbridge-versions-prod";
        public string DynamoJobsTable     { get; set; } = "drawbridge-jobs-prod";
        public string VaultName           { get; set; } = "CreativeWorks";
        public string VaultRootPath       { get; set; } = @"C:\CreativeWorks";
        public string LocalWorkDir        { get; set; } = @"C:\DrawbridgeWork";
        public string ApsClientId         { get; set; } = "";
        public string ApsClientSecret     { get; set; } = "";
        public string ApsBucketKey        { get; set; } = "drawbridge-models";
        public string CloudFrontBaseUrl   { get; set; } = "";
    }
}
