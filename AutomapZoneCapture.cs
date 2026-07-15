using System;
using XRL;
using XRL.World;

namespace CoQAutoMap
{
    [Serializable]
    public sealed class AutomapZoneCaptureSystem : IGameSystem
    {
        public override void Register(XRLGame Game, IEventRegistrar Registrar)
        {
            base.Register(Game, Registrar);
            Registrar.Register(ZoneDeactivatedEvent.ID);
        }

        public override bool HandleEvent(ZoneDeactivatedEvent E)
        {
            try
            {
                Zone zone = E.Zone;

                if (zone == null)
                {
                    return base.HandleEvent(E);
                }

                string zoneId = zone.ZoneID;

                if (string.IsNullOrEmpty(zoneId) || !zoneId.Contains("."))
                {
                    return base.HandleEvent(E);
                }

                AutomapController.QueueDeactivatedZoneCapture(
                    zone,
                    "ZoneDeactivatedEvent"
                );
            }
            catch
            {
            }

            return base.HandleEvent(E);
        }

        private static string SafeActiveZoneId()
        {
            try
            {
                return The.ZoneManager?.ActiveZone?.ZoneID ?? "<null>";
            }
            catch
            {
                return "<active zone lookup failed>";
            }
        }
    }
}