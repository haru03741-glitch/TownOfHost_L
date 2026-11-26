using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
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
    partial class LateTask
    {
        public string name;
        public float timer;
        public Action action;
        private readonly GameSession owner;
        private readonly string callerFile;
        private readonly int callerLine;
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
        /// <param name="callerFile">作った場所。例外の記録に出す (呼び元は渡さない)。</param>
        public LateTask(Action action, float time, string name = "No Name Task", bool outlivesSession = false,
            [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
        {
            this.callerFile = callerFile;
            this.callerLine = callerLine;
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
            // 走らせた処理が新しい LateTask を作ると Tasks が伸びる。伸びた分も同じフレームで見るが、作られる前の時間は引かない
            // (引くと、待ちが 1 フレームの長さ以下の「終わったらまた自分を作る」処理が、同じフレームの中で終わらなくなる)
            int existing = Tasks.Count;
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
                    if (task.Run(i < existing ? deltaTime : 0f))
                    {
                        if (task.name != "")
                            Log.Info("LateTask", $"\"{task.name}\" is finished");
                        task.done = anyDone = true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"{ex.GetType()}: {ex.Message}  in \"{task.name}\" (made at {Path.GetFileName(task.callerFile)}:{task.callerLine})\n{ex.StackTrace}", "LateTask.Error", false);
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
