﻿using System.Diagnostics;
using System.Net.Http;

namespace Microsoft.eShopOnContainers.Services.Basket.API.Controllers;

public class HomeController : Controller
{
    //private readonly AmazonS3Client s3Client = new AmazonS3Client(RegionEndpoint.EUCentral1);
    //private readonly HttpClient httpClient = new HttpClient();

    // GET: /<controller>/
    public IActionResult Index()
    {
        return new RedirectResult("~/swagger");
    }

    //[HttpGet]
    //[Route("/outgoing-http-call")]
    //public string OutgoingHttp()
    //{
    //    _ = httpClient.GetAsync("https://aws.amazon.com").Result;

    //    return GetTraceId();
    //}

    //[HttpGet]
    //[Route("/aws-sdk-call")]
    //public string AWSSDKCall()
    //{
    //    _ = s3Client.ListBucketsAsync().Result;

    //    return GetTraceId();
    //}

    //[HttpGet]
    //[Route("/")]
    //public string Default()
    //{
    //    return "Application started!";
    //}

    //private string GetTraceId()
    //{
    //    var traceId = Activity.Current.TraceId.ToHexString();
    //    var version = "1";
    //    var epoch = traceId.Substring(0, 8);
    //    var random = traceId.Substring(8);
    //    return "{" + "\"traceId\"" + ": " + "\"" + version + "-" + epoch + "-" + random + "\"" + "}";
    //}
}