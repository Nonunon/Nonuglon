using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Lumina.Excel.Sheets;

namespace Nonuglon.Support;

/// <summary>
/// Ported from ffxiv-bundleoftweaks' clib submodule (clib.Extensions.IGameObjectExtensions)
/// since we're not vendoring all of clib just for these couple of helpers.
/// </summary>
public static unsafe class GameObjectPillionExtensions
{
    public static Character* Character(this IGameObject obj) => (Character*)obj.Address;
    public static BattleChara* BattleChara(this IGameObject obj) => (BattleChara*)obj.Address;

    /// <summary>
    /// True whether this object is riding as the mount's driver OR as a pillion
    /// passenger. ConditionFlag.Mounted (Dalamud's usual "am I mounted" check) only
    /// reflects being the driver, so it never flips for a passenger - reading the
    /// Mount struct's MountId directly works for both.
    /// </summary>
    public static bool IsMounted(this IGameObject? obj) => obj != null && obj.Character()->Mount.MountId != 0;

    public static bool CanRidePillion(this IGameObject? obj)
    {
        if (obj == null) return false;
        var mount = obj.Character()->Mount;
        var extraSeats = Svc.Data.GetExcelSheet<Mount>().TryGetRow(mount.MountId, out var mountRow) ? mountRow.ExtraSeats : 0;
        return mount.MountedEntityIds[1..].ToArray().Count(x => x != 0) < extraSeats;
    }

    /// <summary>Every player-visible object matching a name, used as a lightweight
    /// stand-in for clib's IObjectTable.PlayerObjects.</summary>
    public static IGameObject? FindPlayerByName(string name) =>
        Svc.Objects.FirstOrDefault(o =>
            o.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc &&
            o.Name.TextValue == name);
}
