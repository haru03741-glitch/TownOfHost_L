using System.Collections.Generic;
using UnityEngine;

namespace TownOfHostL
{
    /// <summary>
    /// タスクの種類ごとの無効化。「タスク無効化」の下に 短い・共通・長い・その他 の 4 つの見出しを置き、その下にタスクを並べる。
    /// タスクの名前は本編の翻訳 (TranslationController.GetString(TaskTypes)) をそのまま出す。
    /// 分類と見出しは Endless Host Roles (Gurge44/EndlessHostRoles) のタスク無効化による。
    /// </summary>
    public static class TaskDisableOptions
    {
        // 分類は代表的な長さで分けた目安 (同じタスクでもマップで長さが変わる物がある)
        private static readonly (string group, TaskTypes[] tasks)[] Groups =
        {
            ("DisableShortTasks", new[]
            {
                TaskTypes.VentCleaning, TaskTypes.CalibrateDistributor, TaskTypes.ChartCourse, TaskTypes.StabilizeSteering,
                TaskTypes.CleanO2Filter, TaskTypes.UnlockManifolds, TaskTypes.PrimeShields, TaskTypes.MeasureWeather,
                TaskTypes.BuyBeverage, TaskTypes.AssembleArtifact, TaskTypes.SortSamples, TaskTypes.ProcessData,
                TaskTypes.RunDiagnostics, TaskTypes.RepairDrill, TaskTypes.AlignTelescope, TaskTypes.RecordTemperature,
                TaskTypes.FillCanisters, TaskTypes.MonitorOxygen, TaskTypes.StoreArtifacts, TaskTypes.PutAwayPistols,
                TaskTypes.PutAwayRifles, TaskTypes.MakeBurger, TaskTypes.CleanToilet, TaskTypes.Decontaminate,
                TaskTypes.SortRecords, TaskTypes.FixShower, TaskTypes.PickUpTowels, TaskTypes.PolishRuby,
                TaskTypes.DressMannequin, TaskTypes.RoastMarshmallow, TaskTypes.CollectSamples, TaskTypes.ReplaceParts,
            }),
            ("DisableCommonTasks", new[]
            {
                TaskTypes.SwipeCard, TaskTypes.FixWiring, TaskTypes.EnterIdCode, TaskTypes.InsertKeys,
                TaskTypes.ScanBoardingPass, TaskTypes.CollectVegetables, TaskTypes.MineOres, TaskTypes.ExtractFuel,
                TaskTypes.CatchFish, TaskTypes.PolishGem, TaskTypes.HelpCritter, TaskTypes.HoistSupplies,
            }),
            ("DisableLongTasks", new[]
            {
                TaskTypes.SubmitScan, TaskTypes.UnlockSafe, TaskTypes.StartReactor, TaskTypes.ResetBreakers,
                TaskTypes.AlignEngineOutput, TaskTypes.InspectSample, TaskTypes.EmptyChute, TaskTypes.ClearAsteroids,
                TaskTypes.WaterPlants, TaskTypes.OpenWaterways, TaskTypes.ReplaceWaterJug, TaskTypes.RebootWifi,
                TaskTypes.DevelopPhotos, TaskTypes.RewindTapes, TaskTypes.StartFans, TaskTypes.FixAntenna,
                TaskTypes.BuildSandcastle, TaskTypes.CrankGenerator, TaskTypes.MonitorMushroom, TaskTypes.PlayVideogame,
                TaskTypes.TuneRadio, TaskTypes.TestFrisbee, TaskTypes.LiftWeights, TaskTypes.CollectShells,
            }),
            ("DisableOtherTasks", new[]
            {
                TaskTypes.UploadData, TaskTypes.EmptyGarbage, TaskTypes.FuelEngines, TaskTypes.DivertPower,
                TaskTypes.FixWeatherNode,
            }),
        };

        private static readonly Dictionary<TaskTypes, OptionItem> byTask = new();

        /// <summary>見出し 1 つ + タスク 1 つずつの設定を作る (見出しで 4、タスクで 73)。</summary>
        public static void Create(OptionItem parent)
        {
            byTask.Clear();
            foreach (var (groupName, tasks) in Groups)
            {
                var group = StringOptionItem.Create(groupName, new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
                    .SetParent(parent)
                    .SetColor(Color.green)
                    .SetGameMode(CustomGameMode.All);
                foreach (var task in tasks)
                {
                    var t = task;
                    byTask[t] = StringOptionItem.Create("DisableTask." + t, new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
                        .SetParent(group)
                        .SetOptionName(() => TranslationController.InstanceExists ? TranslationController.Instance.GetString(t) : t.ToString())
                        .SetGameMode(CustomGameMode.All);
                }
            }
        }

        /// <summary>このタスクを配らない設定か (親の「タスク無効化」と見出しが OFF なら false)</summary>
        public static bool IsDisabled(TaskTypes task)
            => byTask.TryGetValue(task, out var opt) && opt.GetBool();
    }
}
