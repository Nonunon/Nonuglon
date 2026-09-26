using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Lumina.Excel.Sheets;

namespace Nonuglon.Support;

/// <summary>Ported from ffxiv-bundleoftweaks' clib
/// (clib.Extensions.IGameObjectExtensions), not vendoring all of clib for just
/// these helpers.</summary>
public static unsafe class GameObjectPillionExtensions
{
    public static Character* Character(this IGameObject obj) => (Character*)obj.Address;
    public static BattleChara* BattleChara(this IGameObject obj) => (BattleChara*)obj.Address;

    /// <summary>True for both mount driver and pillion passenger - unlike
    /// ConditionFlag.Mounted, which only reflects being the driver.</summary>
    public static bool IsMounted(this IGameObject? obj) => obj != null && obj.Character()->Mount.MountId != 0;

    public static bool CanRidePillion(this IGameObject? obj)
    {
        if (obj == null) return false;
        var mount = obj.Character()->Mount;
        var extraSeats = Svc.Data.GetExcelSheet<Mount>().TryGetRow(mount.MountId, out var mountRow) ? mountRow.ExtraSeats : 0;
        return mount.MountedEntityIds[1..].ToArray().Count(x => x != 0) < extraSeats;
    }

    /// <summary>Matches name AND home world (the original tweak only checked
    /// name), to avoid offering a ride to the wrong same-named player in
    /// cross-world content.</summary>
    public static IGameObject? FindPlayerByNameAndWorld(string name, uint worldId) =>
        Svc.Objects.FirstOrDefault(o =>
            o.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc &&
            o is IPlayerCharacter pc &&
            pc.HomeWorld.RowId == worldId &&
            pc.Name.TextValue == name);
}
