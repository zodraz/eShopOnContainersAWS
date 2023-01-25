using System.Collections.Generic;

namespace Locations.API.Model.Core
{
    public class AreaLocationPolygon
    {
        public AreaLocationPolygon()
        {
        }

        public AreaLocationPolygon(List<LocationPoint> coordinatesList)
        {
            Coordinates = coordinatesList;
        }

        public List<LocationPoint> Coordinates { get; private set; } = new List<LocationPoint>();
    }
}