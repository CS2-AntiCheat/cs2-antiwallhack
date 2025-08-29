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
    public override string ModuleVersion => "v1";
    public override string ModuleAuthor => "schwarper";
    public override string ModuleDescription => "Prevents wall hacks from working";

    private const uint MASK_STANDARD_SOLID = 0x1 | 0x4000 | 0x80 | 0x2000;
    private ConVar? mp_teammates_are_enemies;
    private readonly Dictionary<CCSPlayerController, List<nint>> _playerDataList = [];
    private int _tickCount = 0;

    public override void Load(bool hotReload)
    {
        mp_teammates_are_enemies = ConVar.Find("mp_teammates_are_enemies");

        if (hotReload)
        {
            var players = Utilities.GetPlayers().Where(p => !p.IsBot);
            foreach (var player in players)
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
        if (@event.Userid is not { } player || player.IsBot || player.PlayerPawn.Value?.Handle is not { } handle)
            return HookResult.Continue;

        foreach (var playerData in _playerDataList.Values)
            playerData.Remove(handle);

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

            foreach (var handle in _playerDataList[player])
            {
                info.TransmitEntities.Remove(new CCSPlayerPawn(handle));
            }
        }
    }

    [ListenerHandler<OnTick>]
    public void OnTick()
    {
        _tickCount++;
        if (_tickCount % 10 != 0)
        {
            return;
        }
        _tickCount = 0;

        var players = Utilities.GetPlayers()
            .Where(p => !p.IsBot && p.PlayerPawn.Value?.LifeState == (byte)LifeState_t.LIFE_ALIVE)
            .ToList();

        var newPlayerDataList = new Dictionary<CCSPlayerController, List<nint>>();
        foreach (var player in players)
        {
            newPlayerDataList[player] = [];
        }

        _playerDataList.Clear();

        foreach (var player in players)
        {
            foreach (var target in players)
            {
                if (target == player)
                    continue;

                if (mp_teammates_are_enemies?.GetPrimitiveValue<bool>() is false && player.TeamNum == target.TeamNum)
                    continue;

                if (IsAbleToSee(player.PlayerPawn.Value!, target.PlayerPawn.Value!))
                    continue;

                newPlayerDataList[player].Add(target.PlayerPawn.Value!.Handle);
            }
        }

        foreach (var item in newPlayerDataList)
        {
            _playerDataList[item.Key] = item.Value;
        }
    }

    private static bool IsAbleToSee(CCSPlayerPawn playerPawn, CCSPlayerPawn targetPawn)
    {
        Vector3? playerEyePos = GetEyePosition(playerPawn);
        if (playerEyePos == null)
            return false;

        Vector3 targetOrigin = ConvertToVector3(targetPawn.AbsOrigin!);

        if (!IsFOV(playerEyePos.Value, playerPawn.EyeAngles, targetOrigin))
            return false;

        if (IsPointVisible(playerEyePos.Value, targetOrigin))
            return true;

        Vector3? targetEyePos = GetEyePosition(targetPawn);
        if (targetEyePos == null)
            return false;

        if (IsFwdVecVisible(playerEyePos.Value, targetPawn.EyeAngles, targetEyePos.Value))
            return true;

        var mins = ConvertToVector3(targetPawn.Collision.Mins);
        var maxs = ConvertToVector3(targetPawn.Collision.Maxs);

        mins.X -= 5;
        mins.Y -= 30;
        maxs.X += 5;
        maxs.Y += 5;

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

    private static bool IsFwdVecVisible(Vector3 start, QAngle angles, Vector3 end)
    {
        Vector3 fwd = GetAngleVectors(angles) * 60.0f;
        fwd += end;
        return IsPointVisible(start, fwd);
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

        foreach (var corner in corners)
        {
            if (IsPointVisible(corner, startVec))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsPointVisible(Vector3 start, Vector3 end)
    {
        var startVector = new Vector(start.X, start.Y, start.Z);
        var endVector = new Vector(end.X, end.Y, end.Z);
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
        var absOrigin = playerPawn.AbsOrigin;
        return absOrigin != null
            ? new Vector3(absOrigin.X, absOrigin.Y, absOrigin.Z + playerPawn.ViewOffset.Z)
            : (Vector3?)null;
    }
}