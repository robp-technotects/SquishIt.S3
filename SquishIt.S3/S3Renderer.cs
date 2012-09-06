﻿using System;
using System.Collections.Specialized;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using Amazon.CloudFront;
using Amazon.S3;
using Amazon.S3.Model;
using SquishIt.Framework.Renderers;

namespace SquishIt.S3
{
    public class S3Renderer : IRenderer, IDisposable
    {
        string bucket;
        AmazonS3 s3client;
        IKeyBuilder keyBuilder;
        IInvalidator invalidator;
        bool overwrite;
        S3CannedACL cannedACL = S3CannedACL.NoACL;
        NameValueCollection headers;
        bool compress;
        ICompressor compressor;

        public void Render(string content, string outputPath)
        {
            if(string.IsNullOrEmpty(outputPath) || string.IsNullOrEmpty(content)) throw new InvalidOperationException("Can't render to S3 with missing key/content.");

            var key = keyBuilder.GetKeyFor(outputPath);
            if(overwrite || !FileExists(key))
            {
                if (compress)
                {
                    UploadCompressedContent(key, content);
                }
                else
                {
                    UploadContent(key, content);
                }

            }
        }

        void UploadCompressedContent(string key, string content)
        {

            var request = new PutObjectRequest()
                .WithBucketName(bucket)
                .WithKey(key)
                .WithCannedACL(cannedACL);
            request.InputStream = compressor.Compress(content);

            if (headers != null) request.AddHeaders(headers);

            //TODO: handle exceptions properly
            s3client.PutObject(request);

            if (invalidator != null) invalidator.InvalidateObject(bucket, key);
        }

        void UploadContent(string key, string content)
        {
            var request = new PutObjectRequest()
                .WithBucketName(bucket)
                .WithKey(key)
                .WithCannedACL(cannedACL)
                .WithContentBody(content);

            if(headers != null) request.AddHeaders(headers);

            //TODO: handle exceptions properly
            s3client.PutObject(request);

            if(invalidator != null) invalidator.InvalidateObject(bucket, key);
        }

        bool FileExists(string key)
        {
            try
            {
                var request = new GetObjectMetadataRequest()
                    .WithBucketName(bucket)
                    .WithKey(key);

                var response = s3client.GetObjectMetadata(request);

                return true;
            }
            catch(AmazonS3Exception ex)
            {
                if(ex.StatusCode == HttpStatusCode.NotFound)
                {
                    return false;
                }
                throw;
            }
        }

        public void Dispose()
        {
            s3client.Dispose();
        }

        #region setup
        public static S3Renderer Create(AmazonS3 s3client)
        {
            return new S3Renderer(s3client);
        }

        public S3Renderer WithBucketName(string bucketName)
        {
            this.bucket = bucketName;
            return this;
        }

        public S3Renderer WithCompression(ICompressor textCompressor)
        {
            this.compressor = textCompressor;
            this.compress = true;

            if (headers == null)
            {
                headers = new NameValueCollection();
            }
            
            headers.Add(textCompressor.Headers);
            return this;
        }

        public S3Renderer WithGZipCompressionEnabled()
        {
            return WithCompression(new GZipCompressor());
        }

        public S3Renderer WithKeyBuilder(IKeyBuilder builder)
        {
            this.keyBuilder = builder;
            return this;
        }

        public S3Renderer WithDefaultKeyBuilder(string physicalApplicationPath, string virtualDirectory)
        {
            return WithKeyBuilder(new KeyBuilder(physicalApplicationPath, virtualDirectory));
        }

        public S3Renderer WithCloudfrontClient(AmazonCloudFront client)
        {
            return WithInvalidator(new CloudFrontInvalidator(client));
        }

        public S3Renderer WithInvalidator(IInvalidator instance)
        {
            this.invalidator = instance;
            return this;
        }

        public S3Renderer WithOverwriteBehavior(bool overwrite)
        {
            this.overwrite = overwrite;
            return this;
        }

        public S3Renderer WithCannedAcl(S3CannedACL acl)
        {
            this.cannedACL = acl;
            return this;
        }

        public S3Renderer WithHeaders(NameValueCollection headers)
        {
            this.headers = headers;
            return this;
        }

        private S3Renderer(AmazonS3 s3client)
        {
            this.s3client = s3client;
        }
        #endregion
    }
}
