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

            AutomapWindowPocController.DebugLog(
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
                    AutomapWindowPocController.DebugLog(
                        "AutomapZoneCaptureSystem: ZoneDeactivatedEvent had null zone."
                    );

                    return base.HandleEvent(E);
                }

                string zoneId = zone.ZoneID ?? "<null>";

                if (!zoneId.Contains("."))
                {
                    AutomapWindowPocController.DebugLog(
                        "AutomapZoneCaptureSystem: skipped non-local/world zone on deactivate: " +
                        zoneId
                    );

                    return base.HandleEvent(E);
                }

                AutomapWindowPocController.DebugLog(
                    "AutomapZoneCaptureSystem: ZoneDeactivatedEvent for " +
                    zoneId +
                    " stale=" +
                    zone.Stale +
                    " suspended=" +
                    zone.Suspended +
                    " activeZone=" +
                    SafeActiveZoneId()
                );

                AutomapWindowPocController.QueueDeactivatedZoneCapture(
                    zone,
                    "ZoneDeactivatedEvent"
                );
            }
            catch (Exception ex)
            {
                AutomapWindowPocController.DebugLog(
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