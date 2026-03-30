using Amazon.S3;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Options;

namespace Drawbridge.ConversionWorker.Services
{
    public class S3Service
    {
        private readonly IAmazonS3 _s3;

        public S3Service(IOptions<WorkerSettings> settings)
        {
            _s3 = new AmazonS3Client(
                Amazon.RegionEndpoint.GetBySystemName(settings.Value.AwsRegion));
        }

        public async Task UploadFileAsync(string bucket, string key, string filePath, CancellationToken ct)
        {
            using var transfer = new TransferUtility(_s3);
            await transfer.UploadAsync(filePath, bucket, key, ct);
        }

        public async Task UploadBytesAsync(
            string bucket, string key, byte[] bytes, string contentType, CancellationToken ct)
        {
            await _s3.PutObjectAsync(new Amazon.S3.Model.PutObjectRequest
            {
                BucketName  = bucket,
                Key         = key,
                InputStream = new MemoryStream(bytes),
                ContentType = contentType,
            }, ct);
        }
    }
}
