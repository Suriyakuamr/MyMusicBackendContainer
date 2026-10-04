using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;

namespace MusicPlatform.API.Services
{
    public class R2StorageService : IR2StorageService
    {
        private readonly IAmazonS3 _s3Client;
        private readonly string _bucketName;

        public R2StorageService(IConfiguration configuration)
        {
            var r2Settings = configuration.GetSection("CloudflareR2");
            var serviceUrl = r2Settings["ServiceUrl"] ?? throw new ArgumentNullException("CloudflareR2:ServiceUrl");
            var accessKey = r2Settings["AccessKey"] ?? throw new ArgumentNullException("CloudflareR2:AccessKey");
            var secretKey = r2Settings["SecretKey"] ?? throw new ArgumentNullException("CloudflareR2:SecretKey");
            _bucketName = r2Settings["BucketName"] ?? throw new ArgumentNullException("CloudflareR2:BucketName");

            var credentials = new BasicAWSCredentials(accessKey, secretKey);
            var config = new AmazonS3Config
            {
                ServiceURL = serviceUrl,
                ForcePathStyle = true
            };

            _s3Client = new AmazonS3Client(credentials, config);
        }

        public async Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType)
        {
            var fileKey = $"{Guid.NewGuid()}_{fileName}";
            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = fileKey,
                InputStream = fileStream,
                ContentType = contentType,
                DisablePayloadSigning = true
            };

            await _s3Client.PutObjectAsync(request);
            return fileKey;
        }

        public async Task<(Stream Stream, string ContentType, long ContentLength)> GetFileStreamAsync(string fileKey)
        {
            var request = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = fileKey
            };

            var response = await _s3Client.GetObjectAsync(request);
            return (response.ResponseStream, response.Headers.ContentType, response.Headers.ContentLength);
        }

        public async Task DeleteFileAsync(string fileKey)
        {
            var request = new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = fileKey
            };

            await _s3Client.DeleteObjectAsync(request);
        }
    }
}
