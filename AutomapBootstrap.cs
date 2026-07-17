using XRL;
using XRL.World;

namespace CoQAutoMap
{
    [PlayerMutator]
    [HasCallAfterGameLoaded]
    public sealed class AutomapBootstrap : IPlayerMutator
    {
        public void mutate(XRL.World.GameObject player)
        {
            AutomapController.EnsureInstalled("PlayerMutator.mutate");
        }

        [CallAfterGameLoaded]
        public static void AfterGameLoaded()
        {
            AutomapController.EnsureInstalled("CallAfterGameLoaded");
        }
    }
}