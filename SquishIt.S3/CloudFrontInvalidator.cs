﻿using System;
using System.Collections.Generic;
using System.Linq;
using Amazon.CloudFront;
using Amazon.CloudFront.Model;

namespace SquishIt.S3
{
    public interface IInvalidator
    {
        void InvalidateObject(string bucket, string key);
    }

    public class CloudFrontInvalidator : IDisposable, IInvalidator
    {
        const string amazonBucketUriSuffix = ".s3.amazonaws.com";
        const string dateFormatWithMilliseconds = "yyyy-MM-dd hh:mm:ss.ff";
        readonly IAmazonCloudFront cloudFrontClient;
        private CreateInvalidationRequest pendingRequest;
        private readonly object pendingRequestLockTarget = new object();

        public CloudFrontInvalidator(IAmazonCloudFront cloudFrontClient)
        {
            this.cloudFrontClient = cloudFrontClient;
        }

        public void InvalidateObject(string bucket, string key)
        {
            var distId = GetDistributionIdFor(bucket);

            if (!string.IsNullOrWhiteSpace(distId))
            {
                var preparedKey = key.StartsWith("/") ? key : "/" + key;

                lock(pendingRequestLockTarget)
                {
                    if (pendingRequest == null)
                    {
                        pendingRequest = new CreateInvalidationRequest()
                        {
                            DistributionId = distId,
                            InvalidationBatch = new InvalidationBatch()
                            {
                                CallerReference = DateTime.Now.ToString(dateFormatWithMilliseconds),
                                Paths = new Paths
                                {
                                    Quantity = 1,
                                    Items = new List<string> {preparedKey}
                                }
                            }
                        };
                    }
                    else
                    {
                        pendingRequest.InvalidationBatch.Paths.Items.Add(preparedKey);
                    }
                }
            }
        }

        public void Flush()
        {
            lock(pendingRequestLockTarget)
            {
                if (pendingRequest != null)
                {
                    cloudFrontClient.CreateInvalidation(pendingRequest);
                    pendingRequest = null;
                }
            }
        }

        Dictionary<string, string> distributionNameAndIds;

        string GetDistributionIdFor(string bucketName)
        {
            distributionNameAndIds = distributionNameAndIds ??
                cloudFrontClient.ListDistributions().DistributionList.Items
                .ToDictionary(cfd => cfd.Origins.Items[0].DomainName.Replace(amazonBucketUriSuffix, string.Empty), cfd => cfd.Id);

            string id = null;
            distributionNameAndIds.TryGetValue(bucketName, out id);
            return id;
        }

        public void Dispose()
        {
            Flush();
            cloudFrontClient.Dispose();
        }
    }
}
