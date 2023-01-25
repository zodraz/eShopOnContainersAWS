﻿using Amazon.Extensions.NETCore.Setup;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus;
using System.Collections.Generic;

namespace Microsoft.eShopOnContainers.Services.Ordering.API;

public class OrderingBkgSettings
{
    public string ConnectionString { get; set; }
    public int GracePeriodTime { get; set; }
    public int CheckUpdateTime { get; set; }
    public bool UseAWS { get; set; }
    public bool UseVault { get; set; }
    public AWSOptions AWSOptions { get; set; }
    public LocalStack LocalStack { get; set; }
    public EventBusSettings EventBus { get; set; }
    public Jaeger Jaeger { get; set; }
    public string OtlpEndpoint { get; set; }
    public VaultSettings Vault { get; set; }
}

public class LocalStack
{
    public bool UseLocalStack { get; set; }
    public string LocalStackUrl { get; set; }
}

public class Jaeger
{
    public string Host { get; set; }
    public int Port { get; set; }
}

public class VaultSettings
{
    /// <summary>
    /// The allowed secret groups, e.g. Shared or MyAppsSecrets
    /// </summary>
    public List<string> SecretGroups { get; set; }
}
