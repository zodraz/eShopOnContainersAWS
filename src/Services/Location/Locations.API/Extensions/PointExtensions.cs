using Locations.API.Model.Core;
using System.Linq;

namespace Locations.API.Extensions
{
    public static class PointExtensions
    {
        public static bool IsInPolygon(this LocationPoint point, AreaLocationPolygon polygon)
        {
            bool result = false;
            var a = polygon.Coordinates.Last();
            foreach (var b in polygon.Coordinates)
            {
                if ((b.Latitude == point.Latitude) && (b.Longitude == point.Longitude))
                    return true;

                if ((b.Longitude == a.Longitude) && (point.Longitude == a.Longitude))
                {
                    if ((a.Latitude <= point.Latitude) && (point.Latitude <= b.Latitude))
                        return true;

                    if ((b.Latitude <= point.Latitude) && (point.Latitude <= a.Latitude))
                        return true;
                }

                if ((b.Longitude < point.Longitude) && (a.Longitude >= point.Longitude) || (a.Longitude < point.Longitude) && (b.Longitude >= point.Longitude))
                {
                    if (b.Latitude + (point.Longitude - b.Longitude) / (a.Longitude - b.Longitude) * (a.Latitude - b.Latitude) <= point.Latitude)
                        result = !result;
                }
                a = b;
            }
            return result;
        }
    }
}
