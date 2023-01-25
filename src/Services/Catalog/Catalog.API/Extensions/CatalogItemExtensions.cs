namespace Microsoft.eShopOnContainers.Services.Catalog.API.Model;

public static class CatalogItemExtensions
{
    public static void FillProductUrl(this CatalogItem item, string picBaseUrl, bool s3Enabled)
    {
        if (item != null)
        {
            item.PictureUri = s3Enabled
                ? picBaseUrl + item.PictureFileName
                : picBaseUrl.Replace("[0]", item.Id.ToString());
        }
    }
}
