using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SuperNewRoles.Modules;
using SuperNewRoles.Events;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SuperNewRoles.Roles.Ability;

/// <summary>
/// プレイヤーのタスクタイプを指定されたタスクタイプに変更するAbility
/// </summary>
public class CustomTaskTypeAbility : AbilityBase
{
    public TaskTypes TargetTaskType { get; }
    public MapNames? TargetMap { get; }
    public bool ChangeAllTasks { get; }

    public CustomTaskTypeAbility(TaskTypes targetTaskType, bool changeAllTasks = false, MapNames? targetMap = null)
    {
        TargetTaskType = targetTaskType;
        ChangeAllTasks = changeAllTasks;
        TargetMap = targetMap;
    }

    public bool ShouldChangeTask()
    {
        return ChangeAllTasks || (byte)TargetMap != GameOptionsManager.Instance.CurrentGameOptions.MapId;
    }

    public NormalPlayerTask GetTargetTask()
    {
        var task = ShipStatus.Instance.ShortTasks.FirstOrDefault(x => x.TaskType == TargetTaskType);
        if (task == null)
            task = ShipStatus.Instance.CommonTasks.FirstOrDefault(x => x.TaskType == TargetTaskType);
        if (task == null)
            task = ShipStatus.Instance.LongTasks.FirstOrDefault(x => x.TaskType == TargetTaskType);
        if (task == null)
            return null;
        return task;
    }

    public override void AttachToLocalPlayer()
    {
        base.AttachToLocalPlayer();
        // 先に読み込んでおく
        CustomTaskTypePatches.GetTargetShip(TargetMap, (_) => { });
    }

    public void AssignTasks(int count)
    {
        var taskList = new List<byte>();
        var task = GetTargetTask();
        for (int i = 0; i < count; i++)
        {
            taskList.Add((byte)task.Index);
        }
        // タスクをプレイヤーに割り当てる
        if (taskList.Count > 0)
        {
            CustomTaskAbility.RpcUncheckedSetTasks(Player, taskList);
        }
    }
}

// タスクタイプ変更のパッチ
[HarmonyPatch]
public static class CustomTaskTypePatches
{
    // Console.Use と MapConsole.Use の共通処理を一か所にまとめる。
    // FixWeatherNode（気象ノード）など一部タスクは Console ではなく
    // MapConsole コンポーネントを使うため、両方のパッチが必要。
    private static Minigame _preMinigame;

    private static void SharedPrefix(PlayerTask task, bool canUse)
    {
        if (!canUse || task == null) return;

        var customTaskTypeAbility = ExPlayerControl.LocalPlayer.GetAbility<CustomTaskTypeAbility>();
        if (customTaskTypeAbility == null) return;
        if (!customTaskTypeAbility.ShouldChangeTask()) return;

        // サボタージュタスクは変換しない。
        // ただし TaskType だけで判定すると、ポーラスの研究室スイッチ（ResetSeismic）のように
        // 通常タスクとサボタージュが同じコンソールを共有するケースで
        // 通常タスク時でも変換されなくなってしまう。
        // そのため「実際にサボタージュが発動中かどうか」も合わせて確認する。
        bool isSabotageTask = task.TaskType is TaskTypes.FixLights or TaskTypes.RestoreOxy or
            TaskTypes.ResetReactor or TaskTypes.ResetSeismic or TaskTypes.FixComms or
            TaskTypes.StopCharles or TaskTypes.MushroomMixupSabotage;

        if (isSabotageTask)
        {
            bool sabotageActive = SaboStateTracker.activeSaboTypes.Count > 0;
            if (sabotageActive || task.TaskType is not TaskTypes.ResetSeismic)
                return;
        }

        _preMinigame = task.MinigamePrefab;
        GetTargetShip(customTaskTypeAbility.TargetMap, (ship) =>
        {
            var targetTask = GetTargetTaskFromShip(ship, customTaskTypeAbility.TargetTaskType);
            if (targetTask != null)
                task.MinigamePrefab = targetTask.MinigamePrefab;
        });
    }

    private static void SharedPostfix(PlayerTask task, bool canUse)
    {
        if (!canUse || task == null) return;

        var customTaskTypeAbility = ExPlayerControl.LocalPlayer.GetAbility<CustomTaskTypeAbility>();
        if (customTaskTypeAbility == null) return;
        if (!customTaskTypeAbility.ShouldChangeTask()) return;

        if (_preMinigame != null)
        {
            task.MinigamePrefab = _preMinigame;
            _preMinigame = null;
        }
    }

    // 通常コンソール（大多数のタスク）
    [HarmonyPatch(typeof(Console), nameof(Console.Use))]
    public static class ConsolePatch
    {
        static void Prefix(Console __instance)
        {
            __instance.CanUse(PlayerControl.LocalPlayer.Data, out bool canUse, out bool _);
            SharedPrefix(__instance.FindTask(PlayerControl.LocalPlayer), canUse);
        }

        static void Postfix(Console __instance)
        {
            __instance.CanUse(PlayerControl.LocalPlayer.Data, out bool canUse, out bool _);
            SharedPostfix(__instance.FindTask(PlayerControl.LocalPlayer), canUse);
        }
    }

    // MapConsole（FixWeatherNode など一部タスクが使うコンソール種別）
    [HarmonyPatch(typeof(MapConsole), nameof(MapConsole.Use))]
    public static class MapConsolePatch
    {
        static void Prefix(MapConsole __instance)
        {
            __instance.CanUse(PlayerControl.LocalPlayer.Data, out bool canUse, out bool _);
            SharedPrefix(__instance.FindTask(PlayerControl.LocalPlayer), canUse);
        }

        static void Postfix(MapConsole __instance)
        {
            __instance.CanUse(PlayerControl.LocalPlayer.Data, out bool canUse, out bool _);
            SharedPostfix(__instance.FindTask(PlayerControl.LocalPlayer), canUse);
        }
    }

    public static void GetTargetShip(MapNames? targetMap, Action<ShipStatus> onLoaded)
    {
        if (targetMap.HasValue)
        {
            switch (targetMap.Value)
            {
                case MapNames.Fungle:
                    if (GameOptionsManager.Instance.CurrentGameOptions.MapId == (int)MapNames.Fungle)
                    {
                        onLoaded(ShipStatus.Instance);
                        return;
                    }
                    Logger.Info("LoadMap: Fungle");
                    MapLoader.LoadMap(MapNames.Fungle, (ship) => { onLoaded(ship); });
                    break;
                case MapNames.Airship:
                    if (GameOptionsManager.Instance.CurrentGameOptions.MapId == (int)MapNames.Airship)
                    {
                        onLoaded(ShipStatus.Instance);
                        return;
                    }
                    Logger.Info("LoadMap: Airship");
                    MapLoader.LoadMap(MapNames.Airship, (ship) => { onLoaded(ship); });
                    break;
                default:
                    onLoaded(ShipStatus.Instance);
                    break;
            }
        }

        // 現在のマップから取得
        onLoaded(ShipStatus.Instance);
    }

    private static NormalPlayerTask GetTargetTaskFromShip(ShipStatus ship, TaskTypes targetTaskType)
    {
        var shortTask = ship.ShortTasks.FirstOrDefault(x => x.TaskType == targetTaskType);
        if (shortTask != null) return shortTask;
        var longTask = ship.LongTasks.FirstOrDefault(x => x.TaskType == targetTaskType);
        if (longTask != null) return longTask;
        var commonTask = ship.CommonTasks.FirstOrDefault(x => x.TaskType == targetTaskType);
        if (commonTask != null) return commonTask;
        return null;
    }
}
