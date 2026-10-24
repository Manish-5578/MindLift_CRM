using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using MindLift.Application.Abstractions;

namespace MindLift.Infrastructure.Files;

/// <summary>Settings for <see cref="S3FileStorage"/>. Works with Amazon S3 and with S3-compatible stores (Cloudflare R2, MinIO, Backblaze, Wasabi).</summary>
public sealed class S3StorageOptions
{
    public string Bucket { get; set; } = string.Empty;

    /// <summary>Amazon region, for example "me-central-1". Not needed when <see cref="ServiceUrl"/> is set.</summary>
    public string? Region { get; set; }

    /// <summary>The address of an S3-compatible store, for example "https://ACCOUNT.r2.cloudflarestorage.com". Empty for Amazon S3.</summary>
    public string? ServiceUrl { get; set; }

    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }

    /// <summary>Most S3-compatible stores want path-style addresses (bucket in the path, not the host name).</summary>
    public bool ForcePathStyle { get; set; } = true;

    /// <summary>A folder inside the bucket, so one bucket can serve several environments.</summary>
    public string? Prefix { get; set; }
}

/// <summary>
/// Stores files in a bucket instead of on the server's disk, so files survive any server change and several servers can share them.
/// Keys are checked the same way as on disk: no absolute paths and no "..".
/// </summary>
public sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly S3StorageOptions _options;

    public S3FileStorage(S3StorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Bucket))
        {
            throw new InvalidOperationException("Storage:S3:Bucket is required when Storage:Provider is S3.");
        }

        _options = options;
        var config = new AmazonS3Config { ForcePathStyle = options.ForcePathStyle };
        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.ServiceURL = options.ServiceUrl;
        }
        else if (!string.IsNullOrWhiteSpace(options.Region))
        {
            config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(options.Region);
        }

        _client = string.IsNullOrWhiteSpace(options.AccessKey)
            ? new AmazonS3Client(config)   // credentials from the environment or the machine's role
            : new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
    }

    public async Task<string> SaveAsync(string key, Stream content, CancellationToken cancellationToken = default)
    {
        // The SDK needs to know the length; buffer streams that cannot tell it.
        Stream body = content;
        MemoryStream? buffer = null;
        if (!content.CanSeek)
        {
            buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            body = buffer;
        }

        try
        {
            await _client.PutObjectAsync(new PutObjectRequest { BucketName = _options.Bucket, Key = ObjectKey(key), InputStream = body, AutoCloseStream = false }, cancellationToken);
            return key;
        }
        finally
        {
            buffer?.Dispose();
        }
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.GetObjectAsync(_options.Bucket, ObjectKey(key), cancellationToken);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException("The stored file does not exist.", key);
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        => await _client.DeleteObjectAsync(_options.Bucket, ObjectKey(key), cancellationToken);

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.GetObjectMetadataAsync(_options.Bucket, ObjectKey(key), cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private string ObjectKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.StartsWith('/') || key.StartsWith('\\') || key.Contains("..") || Path.IsPathRooted(key))
        {
            throw new ArgumentException("Invalid storage key.", nameof(key));
        }

        var clean = key.Replace('\\', '/');
        return string.IsNullOrWhiteSpace(_options.Prefix) ? clean : $"{_options.Prefix.TrimEnd('/')}/{clean}";
    }

    public void Dispose() => _client.Dispose();
}
