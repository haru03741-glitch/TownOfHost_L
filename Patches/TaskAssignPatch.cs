using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.AddOns.Crewmate;
using TownOfHostL.Roles.Crewmate;
using System.Linq;
using TownOfHostL.Roles.Neutral;

namespace TownOfHostL
{
    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.AddTasksFromList))]
    class AddTasksFromListPatch
    {
        public static void Prefix(ShipStatus __instance,
            [HarmonyArgument(1)] int count,
            [HarmonyArgument(4)] Il2CppSystem.Collections.Generic.List<NormalPlayerTask> unusedTasks)
        {
            if (!AmongUsClient.Instance.AmHost) return;

            if (!Options.DisableTasks.GetBool()) return;
            List<NormalPlayerTask> disabledTasks = new();
            for (var i = 0; i < unusedTasks.Count; i++)
            {
                var task = unusedTasks[i];
                if (TaskDisableOptions.IsDisabled(task.TaskType)) disabledTasks.Add(task);
            }
            if (disabledTasks.Count == 0) return;
            // 残りが配る数より少ないと本編の ShipStatus.Begin が RemoveRange で例外を出し、タスクが 1 つも配られない
            // (2026-11-08 実機: Skeld の共通タスクを 2 つとも外して共通 2 個で再現)。配る数の分だけは外さずに残す
            var removable = Math.Min(disabledTasks.Count, unusedTasks.Count - count);
            if (removable < disabledTasks.Count)
                Logger.Warn($"外したタスクが多く、配る数 ({count}) に足りないので {disabledTasks.Count - Math.Max(removable, 0)} 個を残す", "AddTasksFromList");
            for (var i = 0; i < removable; i++)
                unusedTasks.Remove(disabledTasks[i]);
        }
    }

    [HarmonyPatch(typeof(NetworkedPlayerInfo), nameof(NetworkedPlayerInfo.RpcSetTasks))]
    class RpcSetTasksPatch
    {
       
        public static bool SkipPatch = false;
        public static Il2CppSystem.Collections.Generic.Dictionary<byte, Il2CppStructArray<byte>> taskIds = new();

        public static void Prefix(NetworkedPlayerInfo __instance,
        [HarmonyArgument(0)] ref Il2CppStructArray<byte> taskTypeIds)
        {
           
            if (SkipPatch) return;


            if (Main.RealOptionsData == null)
            {               
                return;
            }

            var pc = Utils.GetPlayerById(__instance.PlayerId);
            CustomRoles? RoleNullable = pc?.GetCustomRole();
            if (RoleNullable == null) return;
            CustomRoles role = RoleNullable.Value;

            // デフォルトのタスク数
            bool hasCommonTasks = true;
            int numCommonTasks = -1; 
            int NumLongTasks = Main.NormalOptions.NumLongTasks;
            int NumShortTasks = Main.NormalOptions.NumShortTasks;

            if (Options.OverrideTasksData.AllData.TryGetValue(role, out var data) && data.doOverride.GetBool())
            {
                numCommonTasks = data.assignCommonTasks.GetInt();
                hasCommonTasks = numCommonTasks > 0;
                NumLongTasks = data.numLongTasks.GetInt();
                NumShortTasks = data.numShortTasks.GetInt();
            }

            // 数の決まりの順: 設定の上書き → 属性 (ワークホース) → 役職の登録の TaskCount
            if (pc.Is(CustomRoles.Workhorse))
                (hasCommonTasks, NumLongTasks, NumShortTasks) = Workhorse.TaskData;
            var roleInfo = role.GetRoleInfo();
            if (roleInfo?.TaskCount?.Invoke(pc) is { } roleTaskCount)
                (hasCommonTasks, NumLongTasks, NumShortTasks) = roleTaskCount;
            bool onlyVentCleaning = roleInfo?.OnlyVentCleaningTask == true;

            if (taskTypeIds.Count == 0) hasCommonTasks = false;
            if (!hasCommonTasks && NumLongTasks == 0 && NumShortTasks == 0) NumShortTasks = 1;
                       
            if (!onlyVentCleaning
                && hasCommonTasks && numCommonTasks < 0
                && NumLongTasks == Main.NormalOptions.NumLongTasks
                && NumShortTasks == Main.NormalOptions.NumShortTasks)
            {
                taskIds[__instance.PlayerId] = taskTypeIds;
                return;
            }

            Il2CppSystem.Collections.Generic.List<byte> TasksList = new();
            foreach (var num in taskTypeIds)
                TasksList.Add(num);

            int defaultCommonTasksNum = Main.RealOptionsData.GetInt(Int32OptionNames.NumCommonTasks);
            int commonToKeep = numCommonTasks >= 0
                ? (numCommonTasks < defaultCommonTasksNum ? numCommonTasks : defaultCommonTasksNum)
                : defaultCommonTasksNum;
            if (hasCommonTasks) TasksList.RemoveRange(commonToKeep, TasksList.Count - commonToKeep);
            else TasksList.Clear();

            Il2CppSystem.Collections.Generic.HashSet<TaskTypes> usedTaskTypes = new();
            int start2 = 0;
            int start3 = 0;

            Il2CppSystem.Collections.Generic.List<NormalPlayerTask> LongTasks = new();
            foreach (var task in ShipStatus.Instance.LongTasks)
                LongTasks.Add(task);
            Shuffle<NormalPlayerTask>(LongTasks);

            Il2CppSystem.Collections.Generic.List<NormalPlayerTask> ShortTasks = new();
            foreach (var task in ShipStatus.Instance.ShortTasks)
                ShortTasks.Add(task);
            Shuffle<NormalPlayerTask>(ShortTasks);

           
            if (onlyVentCleaning)
            {
                TasksList.Clear();
                ShortTasks.Clear();

                var ventTask = ShipStatus.Instance.ShortTasks.FirstOrDefault(task => task.TaskType == TaskTypes.VentCleaning);
                if (ventTask != null)
                    ShortTasks.Add(ventTask);              
            }

            ShipStatus.Instance.AddTasksFromList(
                ref start2,
                NumLongTasks,
                TasksList,
                usedTaskTypes,
                LongTasks
            );
            ShipStatus.Instance.AddTasksFromList(
                ref start3,
                NumShortTasks,
                TasksList,
                usedTaskTypes,
                ShortTasks
            );

            taskTypeIds = new Il2CppStructArray<byte>(TasksList.Count);
            for (int i = 0; i < TasksList.Count; i++)
            {
                taskTypeIds[i] = TasksList[i];
            }

            taskIds[__instance.PlayerId] = taskTypeIds;
        }

        public static void Shuffle<T>(Il2CppSystem.Collections.Generic.List<T> list)
        {
            for (int i = 0; i < list.Count - 1; i++)
            {
                T obj = list[i];
                int rand = IRandom.Instance.Next(i, list.Count);
                list[i] = list[rand];
                list[rand] = obj;
            }
        }
    }
}