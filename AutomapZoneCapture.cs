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

                // Only local coordinate zones can be captured as automap tiles.
                // Normal tile IDs look like:
                //   JoppaWorld.11.22.1.1.14
                if (string.IsNullOrEmpty(zoneId) || !zoneId.Contains("."))
                {
                    return base.HandleEvent(E);
                }

                AutomapController.QueueDeactivatedZoneCapture(
                    zone,
                    "ZoneDeactivatedEvent"
                );
            }
            catch (Exception ex)
            {
                AutomapController.DebugLog(
                    "AutomapZoneCaptureSystem.HandleEvent exception: " +
                    ex.GetType().Name +
                    ": " +
                    ex.Message
                );
            }

            return base.HandleEvent(E);
        }
    }
}