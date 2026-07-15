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

            AutomapController.DebugLog(
                "AutomapZoneCaptureSystem: registered for ZoneDeactivatedEvent."
            );
        }

        public override bool HandleEvent(ZoneDeactivatedEvent E)
        {
            try
            {
                Zone zone = E.Zone;

                if (zone == null)
                {
                    AutomapController.DebugLog(
                        "AutomapZoneCaptureSystem: ZoneDeactivatedEvent had null zone."
                    );

                    return base.HandleEvent(E);
                }

                string zoneId = zone.ZoneID ?? "<null>";

                if (!zoneId.Contains("."))
                {
                    AutomapController.DebugLog(
                        "AutomapZoneCaptureSystem: skipped non-local/world zone on deactivate: " +
                        zoneId
                    );

                    return base.HandleEvent(E);
                }

                AutomapController.DebugLog(
                    "AutomapZoneCaptureSystem: ZoneDeactivatedEvent for " +
                    zoneId +
                    " stale=" +
                    zone.Stale +
                    " suspended=" +
                    zone.Suspended +
                    " activeZone=" +
                    SafeActiveZoneId()
                );

                AutomapController.QueueDeactivatedZoneCapture(
                    zone,
                    "ZoneDeactivatedEvent"
                );
            }
            catch (Exception ex)
            {
                AutomapController.DebugLog(
                    "AutomapZoneCaptureSystem.HandleEvent ZoneDeactivatedEvent EXCEPTION: " +
                    ex
                );
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