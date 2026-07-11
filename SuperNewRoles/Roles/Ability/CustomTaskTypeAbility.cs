using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SuperNewRoles.Modules;
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
            task = ShipStatus.Instance.LongTasks.FirstOrDefault(x => x.TaskType == TargetTaskType);
        if (task == null)
            task = ShipStatus.Instance.CommonTasks.FirstOrDefault(x => x.TaskType == TargetTaskType);
        if (task == null)
            return null;
        return task;
    }

    public override void AttachToLocalPlayer()
    {
        base.AttachToLocalPlayer();
        // 先に読み込んでおく
        CustomTaskTypePatches.ConsolePatch.GetTargetShip(TargetMap, (_) => { });
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
    [HarmonyPatch(typeof(Console), nameof(Console.Use))]
    public static class ConsolePatch
    {
        private static Minigame preMinigame;

        static void Prefix(Console __instance)
        {
            __instance.CanUse(PlayerControl.LocalPlayer.Data, out bool canUse, out bool _);
            HandlePrefix(__instance.FindTask(PlayerControl.LocalPlayer), canUse);
        }

        static void Postfix(Console __instance)
        {
            __instance.CanUse(PlayerControl.LocalPlayer.Data, out bool canUse, out bool _);
            HandlePostfix(__instance.FindTask(PlayerControl.LocalPlayer), canUse);
        }

        // Console.Use と MapConsole.Use（気象ノード等）の両方から呼べるよう
        // 本体ロジックを internal メソッドとして切り出したもの。
        // 元々は Console.Use の Prefix にすべて直書きされていたが、
        // 気象ノード（FixWeatherNode）が Console ではなく MapConsole を使うため
        // ロジックを共通化して MapConsoleCustomTaskTypePatch からも呼べるようにした。
        internal static void HandlePrefix(PlayerTask task, bool canUse)
        {
            var customTaskTypeAbility = ExPlayerControl.LocalPlayer.GetAbility<CustomTaskTypeAbility>();
            if (customTaskTypeAbility == null) return;
            if (!customTaskTypeAbility.ShouldChangeTask()) return;
            if (!canUse) return;
            if (task == null) return; // MapConsole 経由の場合、対応タスクが無いことがあるため null チェックを追加

            // サボタージュのタスクは対象外（ミニゲームを差し替えない）
            if (task.TaskType is TaskTypes.FixLights or TaskTypes.RestoreOxy or TaskTypes.ResetReactor or
                TaskTypes.ResetSeismic or TaskTypes.FixComms or TaskTypes.StopCharles or TaskTypes.MushroomMixupSabotage)
                return;

            preMinigame = task.MinigamePrefab;
            GetTargetShip(customTaskTypeAbility.TargetMap, (ship) =>
            {
                var targetTask = GetTargetTaskFromShip(ship, customTaskTypeAbility.TargetTaskType);
                if (targetTask != null)
                {
                    task.MinigamePrefab = targetTask.MinigamePrefab;
                }
            });
        }

        // HandlePrefix で差し替えたミニゲームを、タスク終了後に元へ戻すための共通処理。
        internal static void HandlePostfix(PlayerTask task, bool canUse)
        {
            var customTaskTypeAbility = ExPlayerControl.LocalPlayer.GetAbility<CustomTaskTypeAbility>();
            if (customTaskTypeAbility == null) return;
            if (!customTaskTypeAbility.ShouldChangeTask()) return;
            if (!canUse) return;
            if (task == null) return;
            if (preMinigame != null)
            {
                task.MinigamePrefab = preMinigame;
                preMinigame = null;
            }
        }

        public static void GetTargetShip(MapNames? targetMap, Action<ShipStatus> onLoaded)
        {
            // 特定のマップが指定されている場合はそのマップから取得
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
                        MapLoader.LoadMap(MapNames.Fungle, (ship) =>
                        {
                            onLoaded(ship);
                        });
                        // MapLoader.LoadMap は非同期読み込み後にコールバックで onLoaded を呼ぶ。
                        // ここを break にすると switch を抜けた後に末尾の onLoaded(ShipStatus.Instance) が
                        // 同期的に実行されてしまい、onLoaded が二重に呼ばれるバグになるため return する。
                        return;
                    case MapNames.Airship:
                        if (GameOptionsManager.Instance.CurrentGameOptions.MapId == (int)MapNames.Airship)
                        {
                            onLoaded(ShipStatus.Instance);
                            return;
                        }
                        Logger.Info("LoadMap: Airship");
                        MapLoader.LoadMap(MapNames.Airship, (ship) =>
                        {
                            onLoaded(ship);
                        });
                        return; // 上記 Fungle と同じ理由で return（onLoaded 二重呼び出し防止）
                    default:
                        onLoaded(ShipStatus.Instance);
                        return;
                }
            }

            // targetMap が指定されていない場合は現在のマップから取得
            onLoaded(ShipStatus.Instance);
        }

        private static NormalPlayerTask GetTargetTaskFromShip(ShipStatus ship, TaskTypes targetTaskType)
        {
            // CustomTaskTypeAbility.GetTargetTask() と探索順を統一する
            // (Short → Long → Common)。

            // ショートタスクから探す
            var shortTask = ship.ShortTasks.FirstOrDefault(x => x.TaskType == targetTaskType);
            if (shortTask != null) return shortTask;

            // ロングタスクから探す
            var longTask = ship.LongTasks.FirstOrDefault(x => x.TaskType == targetTaskType);
            if (longTask != null) return longTask;

            // コモンタスクから探す
            var commonTask = ship.CommonTasks.FirstOrDefault(x => x.TaskType == targetTaskType);
            if (commonTask != null) return commonTask;

            return null;
        }
    }
}

//Consoleクラスではなく、MapConsoleクラスを使用しているのポーラスのFixWeatherNodeなどにも同様の適用

[HarmonyPatch(typeof(MapConsole), nameof(MapConsole.Use))]
public static class MapConsoleCustomTaskTypePatch
{
    static void Prefix(MapConsole __instance)
    {
        __instance.CanUse(PlayerControl.LocalPlayer.Data, out bool canUse, out bool _);
        CustomTaskTypePatches.ConsolePatch.HandlePrefix(__instance.FindTask(PlayerControl.LocalPlayer), canUse);
    }

    static void Postfix(MapConsole __instance)
    {
        __instance.CanUse(PlayerControl.LocalPlayer.Data, out bool canUse, out bool _);
        CustomTaskTypePatches.ConsolePatch.HandlePostfix(__instance.FindTask(PlayerControl.LocalPlayer), canUse);
    }
}
