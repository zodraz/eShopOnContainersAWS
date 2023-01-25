namespace Microsoft.eShopOnContainers.Services.Catalog.API;

public class CatalogSettings
{
    public string PicBaseUrl { get; set; }

    public bool UseCustomizationData { get; set; }

    public bool S3Enabled { get; set; }

    public EventBusSettings EventBus { get; set; }
}
