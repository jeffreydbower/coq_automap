using System;

namespace CoQAutoMap
{
    internal struct AutomapZoneCoord
    {
        public string World;
        public int ParasangX;
        public int ParasangY;
        public int ZoneX;
        public int ZoneY;
        public int Z;

        public int GlobalZoneX
        {
            get { return ParasangX * 3 + ZoneX; }
        }

        public int GlobalZoneY
        {
            get { return ParasangY * 3 + ZoneY; }
        }

        public string ZoneId
        {
            get
            {
                return
                    World + "." +
                    ParasangX + "." +
                    ParasangY + "." +
                    ZoneX + "." +
                    ZoneY + "." +
                    Z;
            }
        }

        public static bool TryParse(string zoneId, out AutomapZoneCoord coord)
        {
            coord = default(AutomapZoneCoord);

            if (string.IsNullOrEmpty(zoneId))
            {
                return false;
            }

            string[] parts = zoneId.Split('.');

            if (parts.Length != 6)
            {
                return false;
            }

            int parasangX;
            int parasangY;
            int zoneX;
            int zoneY;
            int z;

            if (!int.TryParse(parts[1], out parasangX))
            {
                return false;
            }

            if (!int.TryParse(parts[2], out parasangY))
            {
                return false;
            }

            if (!int.TryParse(parts[3], out zoneX))
            {
                return false;
            }

            if (!int.TryParse(parts[4], out zoneY))
            {
                return false;
            }

            if (!int.TryParse(parts[5], out z))
            {
                return false;
            }

            coord.World = parts[0];
            coord.ParasangX = parasangX;
            coord.ParasangY = parasangY;
            coord.ZoneX = zoneX;
            coord.ZoneY = zoneY;
            coord.Z = z;

            return true;
        }
    }
}