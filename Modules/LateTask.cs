using System;
using System.Collections.Generic;
using HarmonyLib;
using TownOfHostL.Core;
using TownOfHostL.Core.Modes;
using UnityEngine;

namespace TownOfHostL
{
    /// <summary>
    /// 指定した秒数の後にメインスレッドで 1 回だけ走る処理。
    /// 試合中に作った物はその試合 (GameSession) に属し、試合が閉じたら (ロビーに戻った・切断) 走らせずに捨てる。
    /// </summary>
    class LateTask
    {
        public string name;
        public float timer;
        public Action action;
        private readonly GameSession owner;
        private bool done;

        public static List<LateTask> Tasks = new();
        private static readonly Predicate<LateTask> isDone = static task => task.done;

        public bool Run(float deltaTime)
        {
            timer -= deltaTime;
            if (timer <= 0)
            {
                action();
                return true;
            }
            return false;
        }
        /// <param name="outlivesSession">true なら試合が閉じても捨てない。試合の外まで残る static な状態を戻す処理に使う。</param>
        public LateTask(Action action, float time, string name = "No Name Task", bool outlivesSession = false)
        {
            this.action = action;
            this.timer = time;
            this.name = name;
            owner = outlivesSession ? null : GameSession.Current;
            Tasks.Add(this);
            if (name != "")
                Log.Info("LateTask", $"\"{name}\" is created");
        }
        public static void Update(float deltaTime)
        {
            if (Tasks.Count == 0) return;
            bool anyDone = false;
            // 走らせた処理が新しい LateTask を作ると Tasks が伸びる。伸びた分も同じフレームで数える
            for (int i = 0; i < Tasks.Count; i++)
            {
                var task = Tasks[i];
                if (task.done) continue;
                if (task.owner is { IsClosed: true })
                {
                    if (task.name != "")
                        Log.Info("LateTask", $"\"{task.name}\" は試合 #{task.owner.Id} が閉じたので捨てる");
                    task.done = anyDone = true;
                    continue;
                }
                try
                {
                    if (task.Run(deltaTime))
                    {
                        if (task.name != "")
                            Log.Info("LateTask", $"\"{task.name}\" is finished");
                        task.done = anyDone = true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"{ex.GetType()}: {ex.Message}  in \"{task.name}\"\n{ex.StackTrace}", "LateTask.Error", false);
                    task.done = anyDone = true;
                }
            }
            if (anyDone) Tasks.RemoveAll(isDone);
        }

        // 本編のかくれんぼで動かす処理も LateTask を使うので、通常モードの毎フレームの処理とは別の束に置く
        [PatchBundle(PatchBundles.Core)]
        [HarmonyPatch(typeof(ModManager), nameof(ModManager.LateUpdate))]
        private static class DrivePatch
        {
            public static void Prefix() => Update(Time.deltaTime);
        }
    }
}
