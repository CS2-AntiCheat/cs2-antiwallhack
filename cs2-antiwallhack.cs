using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using CS2TraceRay.Class;
using static CounterStrikeSharp.API.Core.Listeners;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace AntiWallhack;

public class AntiWallhack : BasePlugin
{
    public override string ModuleName => "Anti Wallhack";
    public override string ModuleVersion => "v3";
    public override string ModuleAuthor => "schwarper";
    public override string ModuleDescription => "Prevents wall hacks from working";

    private const uint MASK_STANDARD_SOLID = 0x1 | 0x4000 | 0x80 | 0x2000;
    private ConVar? mp_teammates_are_enemies;
    private readonly Dictionary<CCSPlayerController, List<uint>> _playerDataList = [];
    private bool _tickIgnore;

    public override void Load(bool hotReload)
    {
        mp_teammates_are_enemies = ConVar.Find("mp_teammates_are_enemies");

        if (hotReload)
        {
            IEnumerable<CCSPlayerController> players = Utilities.GetPlayers().Where(p => !p.IsBot);
            foreach (CCSPlayerController? player in players)
                _playerDataList[player] = [];
        }
    }

    [GameEventHandler]
    public HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo _)
    {
        if (@event.Userid is not { } player || player.IsBot)
            return HookResult.Continue;

        _playerDataList[player] = [];
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo _)
    {
        if (@event.Userid is not { } player || player.IsBot)
            return HookResult.Continue;

        _playerDataList.Remove(player);
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo _)
    {
        if (@event.Userid is not { } player || player.IsBot || player.PlayerPawn.Value?.Index is not { } index)
            return HookResult.Continue;

        foreach (List<uint> playerData in _playerDataList.Values)
            playerData.Remove(index);

        _playerDataList[player] = [];

        return HookResult.Continue;
    }

    [ListenerHandler<CheckTransmit>]
    public void CheckTransmit(CCheckTransmitInfoList infoList)
    {
        foreach ((CCheckTransmitInfo info, CCSPlayerController? player) in infoList)
        {
            if (player == null || !_playerDataList.ContainsKey(player))
                continue;

            foreach (uint index in _playerDataList[player])
            {
                info.TransmitEntities.Remove(index);
            }
        }
    }

    [ListenerHandler<OnTick>]
    public void OnTick()
    {
        _tickIgnore = !_tickIgnore;

        if (_tickIgnore)
            return;

        List<CCSPlayerController> players = [.. Utilities.GetPlayers().Where(p => !p.IsBot && p.PlayerPawn.Value?.LifeState == (byte)LifeState_t.LIFE_ALIVE)];

        Dictionary<CCSPlayerController, List<uint>> newPlayerDataList = [];
        foreach (CCSPlayerController? player in players)
        {
            newPlayerDataList[player] = [];
        }

        _playerDataList.Clear();

        foreach (CCSPlayerController? player in players)
        {
            foreach (CCSPlayerController? target in players)
            {
                if (target == player)
                    continue;

                if (mp_teammates_are_enemies?.GetPrimitiveValue<bool>() is false && player.TeamNum == target.TeamNum)
                    continue;

                if (IsAbleToSee(player.PlayerPawn.Value!, target.PlayerPawn.Value!))
                    continue;

                newPlayerDataList[player].Add(target.PlayerPawn.Value!.Index);
            }
        }

        foreach (KeyValuePair<CCSPlayerController, List<uint>> item in newPlayerDataList)
        {
            _playerDataList[item.Key] = item.Value;
        }
    }

    private static bool IsAbleToSee(CCSPlayerPawn playerPawn, CCSPlayerPawn targetPawn)
    {
        var playerEyePos = GetEyePosition(playerPawn);
        if (playerEyePos == null)
            return false;

        var targetEyePos = GetEyePosition(targetPawn);
        if (targetEyePos == null)
            return false;

        Vector3 targetOrigin = ConvertToVector3(targetPawn.AbsOrigin!);
        if (!IsFOV(playerEyePos.Value, playerPawn.EyeAngles, targetOrigin))
            return false;

        if (IsPointVisible(playerEyePos.Value, targetEyePos.Value))
            return true;

        Vector3 mins = ConvertToVector3(targetPawn.Collision.Mins);
        Vector3 maxs = ConvertToVector3(targetPawn.Collision.Maxs);

        mins.X -= 15;
        mins.Y -= 50;
        maxs.X += 15;
        maxs.Y += 50;

        Vector3 vBoxPrimeMins = targetOrigin + mins;
        Vector3 vBoxPrimeMaxs = targetOrigin + maxs;

        return IsBoxVisible(vBoxPrimeMins, vBoxPrimeMaxs, playerEyePos.Value);
    }

    private static bool IsFOV(Vector3 start, QAngle angles, Vector3 end)
    {
        Vector3 normal = GetAngleVectors(angles);
        Vector3 plane = Vector3.Normalize(end - start);
        return Vector3.Distance(start, end) < 75.0 || Vector3.Dot(plane, normal) > 0.0;
    }

    private static bool IsBoxVisible(Vector3 bottomCornerVec, Vector3 upperCornerVec, Vector3 startVec)
    {
        Vector3[] corners =
        [
            bottomCornerVec,
            new Vector3(upperCornerVec.X, bottomCornerVec.Y, bottomCornerVec.Z),
            new Vector3(upperCornerVec.X, upperCornerVec.Y, bottomCornerVec.Z),
            new Vector3(bottomCornerVec.X, upperCornerVec.Y, bottomCornerVec.Z),
            upperCornerVec,
            new Vector3(bottomCornerVec.X, upperCornerVec.Y, upperCornerVec.Z),
            new Vector3(upperCornerVec.X, bottomCornerVec.Y, upperCornerVec.Z),
            new Vector3(bottomCornerVec.X, bottomCornerVec.Y, upperCornerVec.Z),
        ];

        foreach (Vector3 corner in corners)
            if (IsPointVisible(corner, startVec))
                return true;

        return false;
    }

    private static bool IsPointVisible(Vector3 start, Vector3 end)
    {
        Vector startVector = new(start.X, start.Y, start.Z);
        Vector endVector = new(end.X, end.Y, end.Z);
        TraceRay.TraceShapeWithResult(startVector, endVector, MASK_STANDARD_SOLID, 4, 0, out bool result);
        return !result;
    }

    private static Vector3 GetAngleVectors(QAngle angles)
    {
        float pitch = angles.X * (float)Math.PI / 180.0f;
        float yaw = angles.Y * (float)Math.PI / 180.0f;

        return new Vector3
        {
            X = (float)(Math.Cos(pitch) * Math.Cos(yaw)),
            Y = (float)(Math.Cos(pitch) * Math.Sin(yaw)),
            Z = (float)-Math.Sin(pitch)
        };
    }

    private static Vector3 ConvertToVector3(Vector vector)
    {
        return new Vector3(vector.X, vector.Y, vector.Z);
    }

    private static Vector3? GetEyePosition(CCSPlayerPawn playerPawn)
    {
        Vector? absOrigin = playerPawn.AbsOrigin;
        return absOrigin != null
            ? new Vector3(absOrigin.X, absOrigin.Y, absOrigin.Z + playerPawn.ViewOffset.Z)
            : null;
    }
}