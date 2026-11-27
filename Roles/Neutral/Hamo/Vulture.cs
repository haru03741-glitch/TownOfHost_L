using System.Linq;

using System.Collections.Generic;

using AmongUs.GameOptions;

using HarmonyLib;

using UnityEngine;



using TownOfHostL.Roles.Core;

using TownOfHostL.Roles.Core.Interfaces;


using TownOfHostL.Roles.Vanilla;



using System;
using static TownOfHostL.Translator;
namespace TownOfHostL.Roles.Neutral;



public sealed class Vulture : RoleBase, IKillFlashSeeable, IAdditionalWinner

{

    // 役職の元: TheOtherRoles (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =

        SimpleRoleInfo.Create(
            typeof(Vulture),
            player => new Vulture(player),
            CustomRoles.Vulture,
            () => OptionCanUseVent.GetBool() ? RoleTypes.Engineer : RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.Hamo + 55300,
            SetupOptionItem,
            "Vu",
            "#6f4204",
            isDesyncImpostor: false
        );

    public Vulture(PlayerControl player)

    : base(

        RoleInfo,

        player,

        () => HasTask.ForRecompute

    )

    {

        EatCount = 0;

        turneat = 0;

        // 食べた死体の一覧は全員の共有なので、ここでは消さない (試合の途中で誰かがこの役職になった時に、ほかの人が食べた死体が通報できるようになる)。試合の始めは Init が空にする

        Viperkilledplayers = new();



        OptAddWinEatcount = OptionAddWinEatCount.GetInt();

        OptWinEatcount = OptionWinEatCount.GetInt();

        OptEatShape = OptionEatShape.GetBool();

        OptKillflashTaskcount = OptionKillflashtaskcount.GetInt();

        OptOnikuArrowtaskcount = OptionOnikuArrowtskcount.GetInt();

        OptVentInTime = OptionVentIntime.GetFloat();

        OptVentCooldown = OptionVentCooldown.GetFloat();



        Holdings.Subscribe(CustomRoleManager.MarkOthers, GetMarkOthers);




    }

    static OptionItem OptionAddWinEatCount; static int OptAddWinEatcount;//追加勝利に必要なつまみ数

    static OptionItem OptionWinEatCount; static int OptWinEatcount; //勝利に必要なつまみ食い数

    static OptionItem OptionEatShape; static bool OptEatShape;//つまみぐいしたらシェイプ出る

    static OptionItem OptionKillflashtaskcount; static int OptKillflashTaskcount;//キルフラ見えるタスク数

    static OptionItem OptionOnikuArrowtskcount; static int OptOnikuArrowtaskcount;//矢印

    static OptionItem OptionCanUseVent;//ベント使えるか

    static OptionItem OptionVentCooldown; static float OptVentCooldown;//ベントクール

    static OptionItem OptionVentIntime; static float OptVentInTime;//ベント最大



    int EatCount;//食べたかず

    Dictionary<byte, Vector2> DiePlayerPos = new();//死体の矢印

    Dictionary<byte, float> Viperkilledplayers = new();//とける予定の死体

    static List<byte> staticEatedPlayers = new();//食べられたおにく

    [Attributes.GameModuleInitializer]

    public static void Init() => staticEatedPlayers.Clear();

    int turneat;

    enum OptionName

    {

        VultrueCanSeeKillFlushTaskCount,

        VultrueCanSeeOnikuArrowTaskCount,

        VultrueWinEatcount,

        VultrueAddWinEatcount,

        VultrueEatShape

    }



    private static void SetupOptionItem()

    {

        SoloWinOption.Create(RoleInfo, defo: 1);

        OptionAddWinEatCount = IntegerOptionItem.Create(RoleInfo, OptionName.VultrueAddWinEatcount, new(0, 14, 1), 2, false).SetZeroNotation(OptionZeroNotation.Off);

        OptionWinEatCount = IntegerOptionItem.Create(RoleInfo, OptionName.VultrueWinEatcount, new(1, 14, 1), 3, false);

        OptionEatShape = BooleanOptionItem.Create(RoleInfo, OptionName.VultrueEatShape, true, false);

        OptionCanUseVent = BooleanOptionItem.Create(RoleInfo, GeneralOption.CanVent, true, false);

        OptionVentCooldown = FloatOptionItem.Create(RoleInfo, StringNames.EngineerCooldown, new(0, 180, 0.5f), 15, false, OptionCanUseVent).SetValueFormat(OptionFormat.Seconds);

        OptionVentIntime = FloatOptionItem.Create(RoleInfo, StringNames.EngineerInVentCooldown, new(0, 180, 0.5f), 5, false, OptionCanUseVent).SetZeroNotation(OptionZeroNotation.Infinity).SetValueFormat(OptionFormat.Seconds);

        OptionKillflashtaskcount = IntegerOptionItem.Create(RoleInfo, OptionName.VultrueCanSeeKillFlushTaskCount, new(0, 255, 1), 3, false);

        OptionOnikuArrowtskcount = IntegerOptionItem.Create(RoleInfo, OptionName.VultrueCanSeeOnikuArrowTaskCount, new(0, 255, 1), 5, false);



        Options.OverrideTasksData.Create(RoleInfo);

    }

    public override void ApplyGameOptions(IGameOptions opt)

    {

        AURoleOptions.EngineerCooldown = OptVentCooldown;

        AURoleOptions.EngineerInVentMaxTime = OptVentInTime;

    }

    public override bool CancelReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target, ref DontReportReason reason)

    {

        if (reporter.PlayerId == Player.PlayerId && target != null && !staticEatedPlayers.Contains(target?.PlayerId ?? 250))

        {

            Utils.AddGameLog("Vultrue", $"{Player.ColoredName()}: {target.ColoredName()}をつまみぐい！");

            Logger.Info($"{EatCount + 1}個目のお食事", "Vulture");

            reason = DontReportReason.Eat;

            EatCount++;

            turneat++;

            Utils.NotifyRoles(SpecifySeer: Player);

            staticEatedPlayers.Add(target.PlayerId);

            DiePlayerPos.Where(poss => poss.Key == target.PlayerId).Do(poss => GetArrow.Remove(Player.PlayerId, poss.Value));

            EraseEatenBody(target.PlayerId);




            //勝利の確認

            if (OptWinEatcount <= EatCount)

            {

                Logger.Info($"ごちそうさまでした！", "Vulture");

                if (CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Vulture, Player.PlayerId, true))

                {

                    CustomWinnerHolder.NeutralWinnerIds.Add(Player.PlayerId);

                }

            }



            //食べたときにシェイプを

            if (OptEatShape)

            {

                var dummyshapetarget = Main.AllPlayerControls.Where(pc => pc.PlayerId != Player.PlayerId).OrderBy(x => Guid.NewGuid()).FirstOrDefault(PlayerControl.LocalPlayer);
                Player.RpcShapeshift(dummyshapetarget, true);

                void Revert()
                {
                    if (Player == null || !Player.IsAlive()) return;
                    // 会議や追放の演出の間は変身の電文を送らず、明けてから戻す
                    if (GameStates.InMeetingWindow)
                    {
                        _ = new LateTask(Revert, 1f, "VultureShapeEnd");
                        return;
                    }
                    Player.RpcShapeshift(Player, false);
                }
                _ = new LateTask(Revert, 1.5f + Main.LagTime, "VultureShapeEnd");
            }

            if (turneat > 2)

            {


            }



            return true;

        }

        if (reporter?.PlayerId != target?.PlayerId && target != null && staticEatedPlayers.Contains(target?.PlayerId ?? 250))

        {

            reason = DontReportReason.Eat;

            Logger.Info($"{target?.PlayerName ?? "???"}は食事済みだからキャンセル", "Vulture");

            return true;

        }



        return false;

    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)

    {

        //矢印の削除

        DiePlayerPos.Do(oniku => GetArrow.Remove(Player.PlayerId, oniku.Value));

        //保存データの削除

        DiePlayerPos.Clear();




    }



    public bool? CheckKillFlash(MurderInfo info)

    {

        //矢印の保存。万が一GetTruePositionがずれたことを考えてposで統一。

        var pos = info.AppearanceTarget.GetTruePosition();

        // 生き返った人がまた死ぬと同じ番号が来るので、足すのではなく上書きする
        if (DiePlayerPos.TryGetValue(info.AppearanceTarget.PlayerId, out var oldPos)) GetArrow.Remove(Player.PlayerId, oldPos);
        DiePlayerPos[info.AppearanceTarget.PlayerId] = pos;

        GetArrow.Add(Player.PlayerId, pos);

        //Viperの死体なら削除検知を

        // 見積もりは客へ送っている溶ける秒数と、キルの動きの分
        if (info.AppearanceKiller.Is(RoleTypes.Viper)) Viperkilledplayers[info.AppearanceTarget.PlayerId] = TownOfHostL.Modules.DissolveKill.ViperSeconds + TownOfHostL.Modules.DissolveKill.KillSettle;




        //キルフラッシュが見えるタスク数なら～

        return MyTaskState.HasCompletedEnoughCountOfTasks(OptKillflashTaskcount);

    }

    //現在の食事状況を

    public override string GetProgressText(bool comms = false) => $" <color={RoleInfo.RoleColorCode}>({EatCount}/{OptWinEatcount})</color>";

    //死体位置の矢印表示

    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)

    {

        seen ??= seer;

        //死亡済み or 会議中 or 他人にマークの場合は空

        if (!Player.IsAlive() || seer.PlayerId != seen.PlayerId) return "";



        var text = "";

        //タスク数に達しているか

        if (MyTaskState.HasCompletedEnoughCountOfTasks(OptOnikuArrowtaskcount) && !isForMeeting)

        {

            var str = $" <color={RoleInfo.RoleColorCode}>";



            //事前に保存しておいた奴を出す

            foreach (var arrow in DiePlayerPos)

            {

                str += GetArrow.GetArrows(seer, arrow.Value);

            }

            text = $"{str}</color>";

        }

        if (OptAddWinEatcount > 0)

        {

            if (OptAddWinEatcount <= EatCount)

                text += "<#349121>★</color>";

        }

        return text;

    }

    public static string GetMarkOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)

    {

        seen ??= seer;

        if (isForMeeting)

        {

            if (staticEatedPlayers.Contains(seen.PlayerId)) return $"<color={RoleInfo.RoleColorCode}>×</color>";

        }

        return "";

    }

    public override void AfterMeetingTasks()

    {

        Viperkilledplayers.Clear();

        staticEatedPlayers.Clear();

    }

    public bool CheckWin(ref CustomRoles winnerRole)

    {

        if (OptAddWinEatcount is 0) return false;

        //君勝ってるやないか

        if (CustomWinnerHolder.WinnerTeam is CustomWinner.Vulture && CustomWinnerHolder.WinnerIds.Contains(Player.PlayerId)) return false;



        //生きてて小腹が満たせれば勝

        return OptAddWinEatcount <= EatCount && Player.IsAlive();

    }

    public override void OnFixedUpdate(PlayerControl player)

    {

        if (AmongUsClient.Instance.AmHost is false || Viperkilledplayers.Count is 0) return;



        // キーは死んだ人の PlayerId なので、0 からの添字ではなくキーで回す
        foreach (byte i in Viperkilledplayers.Keys.ToArray())

        {

            if (Viperkilledplayers.TryGetValue(i, out var timer))

            {

                timer -= Time.fixedDeltaTime;

                Viperkilledplayers[i] = timer;



                if (timer <= 0)

                {

                    Viperkilledplayers.Remove(i);

                    DiePlayerPos.Where(poss => poss.Key == i).Do(poss => GetArrow.Remove(Player.PlayerId, poss.Value));

                    EraseEatenBody(i);

                }

            }

        }

    }



    public void EraseEatenBody(byte targetId)

    {

        if (!AmongUsClient.Instance.AmHost) return;

        Modules.DeadBodyEraser.Erase(targetId);
    }
}

