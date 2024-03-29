﻿using System;
using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Rebus.Exceptions;
using Rebus.Transport;

namespace Rebus.AwsSnsAndSqs.RebusAmazon
{
    using Extensions;

    internal class AmazonSQSQueueContext
    {
        /// <summary>
        ///  Regular expression built for C# on: Thu, Mar 28, 2019, 07:01:21 PM
        ///  Using Expresso Version: 3.1.6224, http://www.ultrapico.com
        ///  
        ///  A description of the regular expression:
        ///  
        ///  Match expression but don't capture it. [http|https]
        ///      Select from 2 alternatives
        ///          http
        ///              http
        ///          https
        ///              https
        ///  \://sqs\.
        ///      Literal :
        ///      //sqs
        ///      Literal .
        ///  [region]: A named capture group. [.*]
        ///      Any character, any number of repetitions
        ///  \.amazonaws\.com/
        ///      Literal .
        ///      amazonaws
        ///      Literal .
        ///      com/
        ///  [accountId]: A named capture group. [\d+]
        ///      Any digit, one or more repetitions
        ///  /
        ///  [queuename]: A named capture group. [.+]
        ///      Any character, one or more repetitions
        ///  
        ///
        /// </summary>

        //TODO AVP:Changed
        public static Regex m_regex_SqsUri = new Regex(@"(?:http|https)\://(sqs\.(?<region>.*)\.amazonaws\.com|(?<localstack>[^:]+)(:(?<Port>\d+))?)/(?<accountId>\d+)/(?<queuename>.+)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private readonly IAmazonInternalSettings m_AmazonInternalSettings;


        private readonly ConcurrentDictionary<string, string> m_concurrentDictionarySqsUris = new ConcurrentDictionary<string, string>();

        private readonly ConcurrentDictionary<string, SqsInfo> m_sqsArnFormUriCache = new ConcurrentDictionary<string, SqsInfo>();

        public AmazonSQSQueueContext(IAmazonInternalSettings m_AmazonInternalSettings)
        {
            this.m_AmazonInternalSettings = m_AmazonInternalSettings;
        }

        public string GetDestinationQueueUrlByName(string address, ITransactionContext transactionContext)
        {
            var url = m_concurrentDictionarySqsUris.GetOrAdd(address.ToLowerInvariant(), key =>
            {
                if (Uri.IsWellFormedUriString(address, UriKind.Absolute))
                {
                    return address;
                }

                var client = m_AmazonInternalSettings.CreateSqsClient(transactionContext);
                var task = client.GetQueueUrlAsync(address);

                AsyncHelpers.RunSync(() => task);

                var urlResponse = task.Result;

                if (urlResponse.HttpStatusCode == HttpStatusCode.OK)
                {
                    return urlResponse.QueueUrl;
                }

                throw new RebusApplicationException($"could not find Url for address: {address} - got errorcode: {urlResponse.HttpStatusCode}");
            });

            return url;
        }

        public SqsInfo GetSqsInformationFromUri(string sqsUri)
        {
            // Has to be a better way to do this...
            return m_sqsArnFormUriCache.GetOrAdd(sqsUri, sqsUriKey =>
            {
                var match = m_regex_SqsUri.Match(sqsUriKey);

                var accountId = match.Groups["accountId"].Value;
                var queuename = match.Groups["queuename"].Value;
                var region = match.Groups["region"].Value;

                //Testing for Localstack
                if (accountId == "000000000000" && string.IsNullOrEmpty(region) &&
                   !string.IsNullOrEmpty(m_AmazonInternalSettings.AmazonSqsConfig.AuthenticationRegion))
                {
                    region = m_AmazonInternalSettings.AmazonSqsConfig.AuthenticationRegion;
                }


                return new SqsInfo { Url = sqsUri, Arn = $"arn:aws:sqs:{region}:{accountId}:{queuename}", AccountId = accountId, Name = queuename, Region = region };
            });
        }

        public string GetInputQueueUrl(string Address)
        {
            try
            {
                using (var scope = new RebusTransactionScope())
                {
                    var inputQueueUrl = GetDestinationQueueUrlByName(Address, scope.TransactionContext);

                    return inputQueueUrl;
                }
            }
            catch (Exception exception)
            {
                throw new RebusApplicationException(exception, $"Could not get URL of own input queue '{Address}'");
            }
        }
    }
}
