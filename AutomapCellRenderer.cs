// AutomapCellRenderer.cs

using System.Collections.Generic;
using Genkit;
using XRL;
using XRL.Core;
using XRL.UI;
using XRL.World;
using XRL.World.Parts;
using XRL.World.Parts.Skill;

namespace CoQAutoMap
{
    internal static class AutomapCellRenderer
    {
        public static RenderEvent RenderCellForAutomap(
            Cell cell,
            bool visible,
            LightLevel lightLevel,
            bool explored)
        {
            if (cell == null || !explored)
            {
                return null;
            }
            RenderEvent rendered = new RenderEvent();

            RenderCellForAutomap(
                cell,
                rendered,
                visible,
                lightLevel,
                explored,
                Alt: false,
                DisableFullscreenColorEffects: false,
                WantsToPaint: null
            );

            return rendered;
        }

        private static void RenderCellForAutomap(
            Cell cell,
            RenderEvent E,
            bool Visible = true,
            LightLevel Lit = LightLevel.Light,
            bool Explored = true,
            bool Alt = false,
            bool DisableFullscreenColorEffects = false,
            List<GameObject> WantsToPaint = null)
        {
            E.Reset();
            E.DisableFullscreenColorEffects = DisableFullscreenColorEffects;
            E.RenderString = Explored ? "." : " ";
            E.Lit = Lit;
            E.Alt = Alt;
            E.Visible = Visible;

            bool tilesMode = Globals.RenderMode == RenderModeType.Tiles;
            bool lit = Lit > LightLevel.None;
            bool visibleAndLit = lit && Visible;

            GameObject player = The.Player;
            Cell currentCell = player != null ? player.CurrentCell : null;

            GameObject[] objects = cell.Objects.GetArray();
            int count = cell.Objects.Count;

            bool hasComponentRender = false;
            bool hasOverlayRender = false;
            bool hasFinalRender = false;

            for (int i = 0; i < count; i++)
            {
                GameObject obj = objects[i];

                if (obj == null)
                {
                    continue;
                }

                if (obj.HasRegisteredEvent(RenderEvent.ID))
                {
                    hasComponentRender = true;
                }

                if (obj.HasRegisteredEvent(RenderEvent.OverlayID))
                {
                    hasOverlayRender = true;
                }

                if (obj.HasRegisteredEvent(RenderEvent.FinalID))
                {
                    hasFinalRender = true;
                }
            }

            bool solidAlreadyRendered = false;

            if (Explored)
            {
                if (Alt)
                {
                    E.DetailColor = "k";
                    E.ColorString = "&k";
                    E.BackgroundString = "^k";
                }
                else if (XRLCore.RenderFloorTextures)
                {
                    if (tilesMode && !cell.PaintTile.IsNullOrEmpty())
                    {
                        E.Tile = cell.PaintTile;
                    }

                    if (!cell.PaintRenderString.IsNullOrEmpty())
                    {
                        E.RenderString = cell.PaintRenderString;
                    }

                    // Automap-specific difference from Cell.Render:
                    // for explored cells, allow the normal paint/floor colors to remain available
                    // even if the cell is not currently visible.
                    if (tilesMode && !cell.PaintTileColor.IsNullOrEmpty())
                    {
                        E.ColorString = cell.PaintTileColor;
                    }
                    else if (!cell.PaintColorString.IsNullOrEmpty())
                    {
                        E.ColorString = cell.PaintColorString;
                    }

                    if (!cell.PaintDetailColor.IsNullOrEmpty())
                    {
                        E.DetailColor = cell.PaintDetailColor;
                    }
                }

                bool adjacentToPlayer =
                    currentCell != null &&
                    currentCell.ParentZone == cell.ParentZone &&
                    currentCell.DistanceTo(cell.X, cell.Y) <= 1;

                GameObject topObject = null;

                for (int i = 0; i < count; i++)
                {
                    GameObject obj = objects[i];

                    if (obj == null)
                    {
                        continue;
                    }

                    XRL.World.Parts.Render render = obj.Render;

                    if (render == null || render.Never)
                    {
                        continue;
                    }

                    if (obj != player)
                    {
                        GameObject partyLeader = obj.PartyLeader;

                        bool partyFlip =
                            partyLeader != null &&
                            partyLeader.Render != null &&
                            partyLeader.Render.PartyFlip;

                        render.PartyFlip = partyFlip;
                    }
                    else
                    {
                        render.PartyFlip = true;
                    }

                    bool hiddenPlayerRender = XRLCore.RenderHiddenPlayer && obj.IsPlayer();

                    bool solidForRendering =
                        adjacentToPlayer
                            ? obj.ConsiderSolidInRenderingContextFor(player)
                            : obj.ConsiderSolidInRenderingContext();

                    if (!solidAlreadyRendered || hiddenPlayerRender || solidForRendering)
                    {
                        if (render.CustomRender && obj.HasRegisteredEvent("CustomRender"))
                        {
                            obj.FireEvent(Event.New("CustomRender", "RenderEvent", E));
                        }

                        // This intentionally still uses real visibility.
                        // Non-visible cells do not become "visible" just because Automap is rendering them.
                        if (hiddenPlayerRender || render.Visible && (visibleAndLit || render.RenderIfDark))
                        {
                            if (render.RenderLayer >= E.HighestLayer)
                            {
                                topObject = obj;
                                E.HighestLayer = render.RenderLayer;
                            }

                            obj.Seen();

                            if (obj == Sidebar.CurrentTarget)
                            {
                                XRLCore.CludgeTargetRendered = true;
                            }

                            if (!solidAlreadyRendered && solidForRendering)
                            {
                                solidAlreadyRendered = true;
                            }
                        }

                        if (WantsToPaint != null && (obj.Flags & 128) != 0)
                        {
                            WantsToPaint.Add(obj);
                        }
                    }

                    if (Alt && (obj.Flags & 256) != 0)
                    {
                        break;
                    }
                }

                if (topObject != null)
                {
                    if (solidAlreadyRendered &&
                        count > 1 &&
                        (Lit == LightLevel.Radar || Lit == LightLevel.LitRadar) &&
                        XRLCore.CurrentFrame10 % 125 >= 95)
                    {
                        topObject = GetRadarReveal(objects, count) ?? topObject;
                    }

                    XRL.World.Parts.Render render = topObject.Render;

                    cell.ParentZone.RenderedObjects++;

                    if (XRLCore.RenderFloorTextures || render.RenderLayer > 0)
                    {
                        if (!lit && topObject == player)
                        {
                            E.RenderString = render.RenderString;
                            E.ColorString =
                                !tilesMode || string.IsNullOrEmpty(render.TileColor)
                                    ? render.ColorString
                                    : render.TileColor;

                            E.HighestLayer = render.RenderLayer;

                            if (tilesMode)
                            {
                                E.Tile = render.Tile;
                                E.HFlip = render.getHFlip();
                                E.VFlip = render.getVFlip();
                                E.DetailColor = "y";
                            }

                            E.WantsToPaint = false;

                            if (hasComponentRender)
                            {
                                topObject.ComponentRender(E);
                            }

                            if (WantsToPaint != null && E.WantsToPaint)
                            {
                                WantsToPaint.Add(topObject);
                            }
                        }
                        else if (visibleAndLit || render.RenderIfDark)
                        {
                            if (tilesMode)
                            {
                                E.Tile = render.Tile;
                                E.HFlip = render.getHFlip();
                                E.VFlip = render.getVFlip();
                            }

                            if (Alt)
                            {
                                RenderAlt(E, player, topObject, render);
                            }
                            else
                            {
                                E.RenderString = render.RenderString;
                                E.DetailColor = render.DetailColor;
                                E.ColorString =
                                    !tilesMode || string.IsNullOrEmpty(render.TileColor)
                                        ? render.ColorString
                                        : render.TileColor;

                                bool targetPulse = topObject == Sidebar.CurrentTarget;

                                if (targetPulse)
                                {
                                    int frame = XRLCore.CurrentFrame;

                                    if ((frame > 30 && frame < 45) ||
                                        (frame <= 30 && frame >= 15))
                                    {
                                        targetPulse = false;
                                    }
                                }

                                if (targetPulse)
                                {
                                    RenderTarget(E, player, topObject);
                                }
                            }

                            E.HighestLayer = render.RenderLayer;
                            E.WantsToPaint = false;

                            if (Alt && hasOverlayRender)
                            {
                                topObject.OverlayRender(E);
                            }
                            else if (hasComponentRender)
                            {
                                topObject.ComponentRender(E);
                            }

                            if (WantsToPaint != null &&
                                E.WantsToPaint &&
                                !WantsToPaint.Contains(topObject))
                            {
                                WantsToPaint.Add(topObject);
                            }
                        }
                    }
                }
            }

            if (hasFinalRender)
            {
                for (int i = 0; i < count; i++)
                {
                    GameObject obj = objects[i];

                    if (obj == null)
                    {
                        continue;
                    }

                    E.WantsToPaint = false;
                    obj.FinalRender(E);

                    if (WantsToPaint != null &&
                        E.WantsToPaint &&
                        !WantsToPaint.Contains(obj))
                    {
                        WantsToPaint.Add(obj);
                    }
                }
            }

            if (E.RenderString.Length == 1)
            {
                if (E.RenderString[0] == '^')
                {
                    E.RenderString = "^^";
                }
                else if (E.RenderString[0] == '&')
                {
                    E.RenderString = "&&";
                }
            }

            if (Alt || E.CustomDraw)
            {
                return;
            }

            // This is the Automap-specific core change.
            //
            // Vanilla Cell.Render does this here:
            //
            // if (!Visible || !lit)
            // {
            //     E.ColorString = "&K";
            //     if (tilesMode)
            //     {
            //         E.DetailColor = "k";
            //     }
            // }
            //
            // We intentionally do not do that.
            // Explored-but-not-visible cells keep whatever render colors were already produced.

            if (!DisableFullscreenColorEffects && Visible && lit)
            {
                switch (Lit)
                {
                    case LightLevel.Safelight:
                        E.ColorString = "&r";
                        E.DetailColor = "R";
                        break;

                    case LightLevel.Radar:
                    case LightLevel.LitRadar:
                        int frame = XRLCore.CurrentFrame;

                        if (frame >= 27 && frame <= 44 && cell.BlocksRadar())
                        {
                            E.ColorString = "&R";
                            E.DetailColor = "r";
                        }
                        else if (Lit == LightLevel.Radar)
                        {
                            E.ColorString = "&C";
                            E.DetailColor = "c";
                        }

                        break;
                }
            }
        }

        private static GameObject GetRadarReveal(GameObject[] objects, int count)
        {
            GameObject radarReveal = null;
            int highestLayer = -1;

            for (int i = 0; i < count; i++)
            {
                GameObject obj = objects[i];

                if (obj == null)
                {
                    continue;
                }

                if (obj.Physics != null &&
                    !obj.Physics.Solid &&
                    obj.IsReal &&
                    obj.Render != null &&
                    obj.Render.Visible &&
                    obj.Render.RenderLayer > highestLayer &&
                    obj.Render.RenderLayer > 0)
                {
                    highestLayer = obj.Render.RenderLayer;
                    radarReveal = obj;
                }
            }

            return radarReveal;
        }

        private static void RenderTarget(RenderEvent E, GameObject player, GameObject obj)
        {
            var brain = obj.Brain;

            if (obj.IsPlayer())
            {
                E.BackgroundString = "^B";
                return;
            }

            if (brain != null)
            {
                GameObject partyLeader = brain.PartyLeader;

                if (partyLeader != null && partyLeader.IsPlayer())
                {
                    E.BackgroundString = "^b";
                    return;
                }
            }

            if (brain != null && brain.IsHostileTowards(player))
            {
                E.BackgroundString = "^r";
            }
            else
            {
                E.BackgroundString = "^g";
            }
        }

        private static void RenderAlt(
            RenderEvent E,
            GameObject player,
            GameObject obj,
            XRL.World.Parts.Render render)
        {
            E.RenderString = "Û";

            string overlayColor = obj.GetPropertyOrTag("OverlayColor");

            if (overlayColor != null)
            {
                E.RenderString = render.RenderString;
                E.ColorString = overlayColor;
                E.BackgroundString = "^k";

                if (E.ColorString.Length > 1)
                {
                    E.DetailColor = E.ColorString.Substring(1, 1);
                }
            }
            else
            {
                E.ColorString = "&k";
                E.BackgroundString = "^k";
                E.DetailColor = "k";
            }

            string overlayDetailColor = obj.GetStringProperty("OverlayDetailColor");
            if (overlayDetailColor != null)
            {
                E.DetailColor = overlayDetailColor;
            }

            string overlayRenderString = obj.GetStringProperty("OverlayRenderString");
            if (overlayRenderString != null)
            {
                E.RenderString = overlayRenderString;
            }

            string overlayTile = obj.GetStringProperty("OverlayTile");
            if (overlayTile != null)
            {
                E.Tile = overlayTile;
            }

            if (obj.Brain != null && player != null)
            {
                var brain = obj.Brain;

                if (obj.IsPlayer())
                {
                    E.BackgroundString = "^k";
                    E.ColorString = "&B";
                    E.DetailColor = "B";
                }
                else
                {
                    GameObject partyLeader = brain.PartyLeader;

                    if (partyLeader != null && partyLeader.IsPlayer())
                    {
                        E.BackgroundString = "^k";
                        E.ColorString = "&b";
                        E.DetailColor = "b";
                    }
                    else if (brain.IsHostileTowards(player))
                    {
                        E.RenderString = render.RenderString;
                        E.ColorString = "&R";
                        E.BackgroundString = "^k";
                        E.DetailColor = "R";
                    }
                    else
                    {
                        E.RenderString = render.RenderString;
                        E.ColorString = "&G";
                        E.BackgroundString = "^k";
                        E.DetailColor = "G";
                    }
                }
            }
            else
            {
                Tinkering_Mine mine;

                if (obj.TryGetPart<Tinkering_Mine>(out mine))
                {
                    if (mine.Timer != -1 || mine.ConsiderHostile(player))
                    {
                        E.RenderString = render.RenderString;
                        E.ColorString = "&R";
                        E.BackgroundString = "^k";
                        E.DetailColor = "R";
                    }
                    else
                    {
                        E.RenderString = render.RenderString;
                        E.ColorString = "&G";
                        E.BackgroundString = "^k";
                        E.DetailColor = "G";
                    }
                }
                else
                {
                    if (player == null)
                    {
                        return;
                    }

                    if (player.HasPart<TrashRifling>() && obj.HasPart<Garbage>())
                    {
                        E.RenderString = render.RenderString;
                        E.DetailColor = "w";
                        E.ColorString = "&w";
                        E.BackgroundString = "^k";
                    }
                    else
                    {
                        Harvestable harvestable;

                        if (player.HasPart<CookingAndGathering_Harvestry>() &&
                            obj.TryGetPart<Harvestable>(out harvestable) &&
                            harvestable != null &&
                            harvestable.Ripe)
                        {
                            E.RenderString = render.RenderString;
                            E.DetailColor = "w";
                            E.ColorString = "&w";
                            E.BackgroundString = "^k";
                        }
                    }
                }
            }
        }
    }
}