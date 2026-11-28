using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;
using static TownOfHostL.Translator;

using TownOfHostL.Core.Modes;

namespace TownOfHostL.Roles
{
    public static class RoleAssignManager
    {
        class RandomAssignOptions
        {
            public int Min => min();
            private Func<int> min;
            public int Max => max();
            private Func<int> max;

            private RandomAssignOptions(OptionItem parent, CustomRoleTypes roleTypes, int maxCount)
            {
                var replacementDictionary = new Dictionary<string, string>()
                { { "%roleType%", GetString( $"CustomRoleTypes.{roleTypes}") } };

                var minOption = IntegerOptionItem.Create("RoleTypeMin", new(0, maxCount, 1), 0, TabGroup.ModMainSettings, false)
                    .SetParent(parent)
                    .SetKeyName($"RoleTypeMin.{roleTypes}")
                    .SetValueFormat(OptionFormat.Players);
                var maxOption = IntegerOptionItem.Create("RoleTypeMax", new(0, maxCount, 1), 0, TabGroup.ModMainSettings, false)
                    .SetParent(parent)
                    .SetKeyName($"RoleTypeMax.{roleTypes}")
                    .SetValueFormat(OptionFormat.Players);

                minOption.ReplacementDictionary =
                maxOption.ReplacementDictionary = replacementDictionary;

                min = () => minOption.GetInt();
                max = () => maxOption.GetInt();

                RandomAssignOptionsCollection.Add(roleTypes, this);
            }
            public static RandomAssignOptions Create(OptionItem parent, CustomRoleTypes roleTypes, int maxCount = 15)
                => new(parent, roleTypes, maxCount);
        }
        private static AssignAlgorithm AssignMode => assignMode();
        private static Func<AssignAlgorithm> assignMode;
        private enum AssignAlgorithm
        {
            Fixed,
            Random
        }
        private static readonly string[] AssignModeSelections =
        {
            "AssignAlgorithm.Fixed",
            "AssignAlgorithm.Random"
        };
        private static readonly CustomRoles[] AllMainRoles = CustomRolesHelper.AllMainRoles;
        public static OptionItem OptionAssignMode;
        private static Dictionary<CustomRoleTypes, RandomAssignOptions> RandomAssignOptionsCollection = new(CustomRolesHelper.AllRoleTypes.Length);
        private static Dictionary<CustomRoleTypes, int> AssignCount = new(CustomRolesHelper.AllRoleTypes.Length);
        private static List<CustomRoles> AssignRoleList = new(CustomRolesHelper.AllRoles.Length);
        // 試合の途中で付く属性 (ワークホース・ラストインポスターなど) の、この試合で出すと決まった分。
        // 開始時に配らないよう AssignRoleList には入れず、IsPresent・GetRealCount の数にだけ足す
        private static List<CustomRoles> LaterAddOnList = new();
        public static void SetupOptionItem()
        {
            OptionAssignMode = StringOptionItem.Create("AssignMode", AssignModeSelections, 0, TabGroup.ModMainSettings, false)
                .SetColor(Palette.LightBlue);

            assignMode = () => (AssignAlgorithm)OptionAssignMode.GetInt();
            RandomAssignOptionsCollection.Clear();
            RandomAssignOptions.Create(OptionAssignMode, CustomRoleTypes.Impostor, 3);
            RandomAssignOptions.Create(OptionAssignMode, CustomRoleTypes.Madmate);
            RandomAssignOptions.Create(OptionAssignMode, CustomRoleTypes.Crewmate);
            RandomAssignOptions.Create(OptionAssignMode, CustomRoleTypes.Neutral);
            PlayerCountComposition.SetupOptionItem();
            SetupComboOptions();
        }
        public static bool CheckRoleCount()
        {
            var playerCount = GameData.Instance.PlayerCount;
            Func<CustomRoleTypes, (int min, int max)> range;
            if (PlayerCountComposition.IsOn) range = t => PlayerCountComposition.Get(playerCount, t);
            else if (AssignMode == AssignAlgorithm.Fixed)
            {
                // 固定のアサイン: 有効にした役職の数の合計が人数より多いと、配られない役職が出る
                int setCount = GetCandidateRoleList(1).Sum(r => r.GetAssignUnitRolesArray().Length);
                if (setCount > playerCount) WarnOverPlayers(setCount, playerCount);
                return true;
            }
            else range = t => (RandomAssignOptionsCollection[t].Min, RandomAssignOptionsCollection[t].Max);
            var result = true;

            // インポスターの数は本編の「インポスター数」ではなく、ここの最少と最多で決める (本編の数で頭打ちにしない)
            var (min, max) = range(CustomRoleTypes.Impostor);
            if (min > max || min >= playerCount)
            {
                var msg = GetString("Warning.NotMatchImpostorCount");
                Logger.SendInGame(msg);
                Logger.Warn(msg, "BeginGame");
                result = false;
            }
            var roleMinCount = 0;
            foreach (var type in RandomAssignOptionsCollection.Keys)
                roleMinCount += range(type).min;
            if (roleMinCount > playerCount)
            {
                var msg = GetString("Warning.NotMatchRoleCount");
                Logger.SendInGame(msg);
                Logger.Warn(msg, "BeginGame");
                result = false;
            }
            else
            {
                // インポスター・マッドメイト・ニュートラルの最多の合計が人数より多い (クルーは残りを埋めるので数えない)
                int maxSum = 0;
                foreach (var type in RandomAssignOptionsCollection.Keys)
                    if (type != CustomRoleTypes.Crewmate) maxSum += range(type).max;
                if (maxSum > playerCount) WarnOverPlayers(maxSum, playerCount);
            }

            return result;
        }
        /// <summary>
        /// 設定から決まっているインポスターの数。ランダムのアサイン・人数ごとの構成で最少と最多が違う (くじ引きで決まる) 時は null。
        /// 客の画面でも設定の値から出せる (くじの結果は送られてこないので、決まっている時だけ数を出す)
        /// </summary>
        public static int? KnownImpostorCount(int players)
        {
            (int min, int max) r;
            if (PlayerCountComposition.IsOn) r = PlayerCountComposition.Get(players, CustomRoleTypes.Impostor);
            else if (AssignMode == AssignAlgorithm.Random && RandomAssignOptionsCollection.TryGetValue(CustomRoleTypes.Impostor, out var o)) r = (o.Min, o.Max);
            else return Main.NormalOptions?.NumImpostors;
            if (Math.Max(r.min, r.max) != r.min) return null;
            return Math.Clamp(r.min, 0, Math.Max(0, players - 1));
        }
        static void WarnOverPlayers(int setCount, int playerCount)
        {
            var msg = string.Format(GetString("Warning.RoleCountOverPlayers"), setCount, playerCount);
            Logger.SendInGame(msg);
            Logger.Warn(msg, "BeginGame");
        }
        /// <summary>
        /// ランダムのアサイン (と人数ごとの構成) で決めたインポスターの数。
        /// 本編の「インポスター数」の代わりに、本編の役職の割り当てで使う (ロビーに戻ったら消す)
        /// </summary>
        public static int? ImpostorCountOverride;
        public static void SelectAssignRoles()
        {
            AssignCount.Clear();
            AssignRoleList.Clear();
            LaterAddOnList.Clear();

            ImpostorCountOverride = null;
            // 開始の直前にも「設定する人数」を今の人数に合わせる
            PlayerCountComposition.FollowPlayerCount();
            if (PlayerCountComposition.IsOn)
            {
                // 人数ごとに決めた最少と最多で、ランダムのアサインと同じく配る
                var players = GameData.Instance.PlayerCount;
                Logger.Info($"人数ごとの構成を使う ({players} 人)", "AssignCount");
                SetRandomAssignCount(t => PlayerCountComposition.Get(players, t));
                SetRandomAssignRoleList();
                SetAddOnsList(false);
            }
            else switch (AssignMode)
            {
                case AssignAlgorithm.Fixed:
                    SetFixedAssignRole();
                    SetAddOnsList(true);
                    break;
                case AssignAlgorithm.Random:
                    SetRandomAssignCount(t => (RandomAssignOptionsCollection[t].Min, RandomAssignOptionsCollection[t].Max));
                    SetRandomAssignRoleList();
                    SetAddOnsList(false);
                    break;
            }

            ApplyComboRules();
            // 固定のアサインの枠数は配る役職から数えたので、組み合わせで変えた後に数え直す
            if (!PlayerCountComposition.IsOn && AssignMode == AssignAlgorithm.Fixed)
                foreach (var roleType in CustomRolesHelper.AllRoleTypes)
                    AssignCount[roleType] = AssignRoleList.Count(role => role < CustomRoles.StartAddon && role.GetAssignRoleType() == roleType);
            SuddenDeath.AdjustAssignList(AssignRoleList);
            AssignRoleList.Sort();
            Logger.Info($"{string.Join(", ", AssignCount)}", "AssignCount");
            Logger.Info($"{string.Join(", ", AssignRoleList)}", "AssignRoleList");
            if (LaterAddOnList.Count > 0) Logger.Info($"{string.Join(", ", LaterAddOnList)}", "LaterAddOnList");
        }
        ///<summary>
        ///役職の固定アサイン抽選
        ///chance が 100% の役職は必ず追加、1〜99% の役職は設定確率で抽選
        ///</summary>
        private static void SetFixedAssignRole()
        {
            int numImpostorsLeft = Math.Min(GameData.Instance.PlayerCount, Main.RealOptionsData.GetInt(Int32OptionNames.NumImpostors));
            int numOthersLeft = GameData.Instance.PlayerCount - numImpostorsLeft;
            var rand = IRandom.Instance;

            foreach (var role in GetCandidateRoleList(0).OrderBy(x => Guid.NewGuid()))
            {
                if (numImpostorsLeft <= 0 && numOthersLeft <= 0) break;

               
                var chance = role.GetChance();
                if (chance < 100)
                {
                    var r = rand.Next(100);
                    if (r >= chance) continue;
                }

                var targetRoles = role.GetAssignUnitRolesArray();
                var numImpostorAssign = targetRoles.Count(role => role.GetAssignRoleType() == CustomRoleTypes.Impostor);
                var numOthersAssign = targetRoles.Length - numImpostorAssign;
                if ((numImpostorAssign > numImpostorsLeft
                || numOthersAssign > numOthersLeft) && !SuddenDeath.Active) continue;

                AssignRoleList.AddRange(targetRoles);
                numImpostorsLeft -= numImpostorAssign;
                numOthersLeft -= numOthersAssign;
            }

            foreach (var roleType in CustomRolesHelper.AllRoleTypes)
            {
                var count = AssignRoleList.Count(role => role.GetAssignRoleType() == roleType);
                AssignCount.Add(roleType, count);
            }
        }
        ///<summary>
        ///設定と実際の人数から各役職のアサイン数を決定
        ///</summary>
        private static void SetRandomAssignCount(Func<CustomRoleTypes, (int min, int max)> range)
        {
            var rand = IRandom.Instance;
            var playerCount = GameData.Instance.PlayerCount;
            // インポスターの数: 最少〜最多のくじ引き。本編の「インポスター数」では頭打ちにしない (最低 1 人はクルー側に残す)
            var (impMin, impMax) = range(CustomRoleTypes.Impostor);
            int numImpostors = Math.Clamp(rand.Next(impMin, Math.Max(impMin, impMax) + 1), 0, Math.Max(0, playerCount - 1));
            //インポスター以外の人数
            //マッド、クルー、ニュートラル合計の限界値
            int numOthers = playerCount - numImpostors;
            var otherTypes = RandomAssignOptionsCollection.Keys.Where(x => x != CustomRoleTypes.Impostor).ToList();

            List<CustomRoleTypes> otherRoleTypesList = new();
            if (numOthers > 0) //マッド、クルー、ニュートラルの人数決定
            {
                //一旦最少人数を設定
                foreach (var roleType in otherTypes)
                    otherRoleTypesList.AddRange(Enumerable.Repeat(roleType, range(roleType).min).ToList());

                //超えている場合はランダムに削除
                while (otherRoleTypesList.Count > numOthers)
                    otherRoleTypesList.RemoveAt(rand.Next(otherRoleTypesList.Count));

                int numAdditional = numOthers - otherRoleTypesList.Count;
                if (numAdditional > 0) //最少人数で限界値に満たない場合
                {
                    List<CustomRoleTypes> additionalList = new();
                    foreach (var roleType in otherTypes)
                    {
                        var (min, max) = range(roleType);
                        //追加人数を取得
                        // 最多が最少より小さい設定 (最少を先に上げた時など) は最少と同じとみなす。負の上限は Next が例外を出す
                        int additionalCount = rand.Next(Math.Max(0, max - min) + 1);

                        additionalList.AddRange(Enumerable.Repeat(roleType, additionalCount).ToList());
                    }

                    //超えている場合はランダムに削除
                    while (additionalList.Count > numAdditional)
                        additionalList.RemoveAt(rand.Next(additionalList.Count));

                    otherRoleTypesList.AddRange(additionalList);
                }
            }

            //Dictionaryに変換
            foreach (var roleTypes in RandomAssignOptionsCollection.Keys)
            {
                if (roleTypes == CustomRoleTypes.Impostor) AssignCount.Add(roleTypes, numImpostors);
                else AssignCount.Add(roleTypes, otherRoleTypesList.Count(x => x == roleTypes));
            }
            ImpostorCountOverride = numImpostors;
        }
        ///<summary>
        ///役職のアサイン抽選
        ///既に決まったアサイン枠数に合わせて決定
        ///</summary>
        private static void SetRandomAssignRoleList()
        {
            List<(CustomRoles, int)> randomRoleTicketPool = new(); //ランダム抽選時のプール
            var rand = IRandom.Instance;
            var assignCount = new Dictionary<CustomRoleTypes, int>(AssignCount); //アサイン枠のDictionary
            foreach (var role in GetCandidateRoleList(100).OrderBy(x => Guid.NewGuid()))
            {
                var targetRoles = role.GetAssignUnitRolesArray();
                //アサイン枠が足りてない場合
                if (CustomRolesHelper.AllRoleTypes.Any(
                    type => assignCount.TryGetValue(type, out var count) &&
                    targetRoles.Count(role => role.GetAssignRoleType() == type) > count
                )) continue;

                foreach (var targetRole in targetRoles)
                {
                    AssignRoleList.Add(targetRole);
                    var targetRoleType = targetRole.GetAssignRoleType();
                    if (assignCount.ContainsKey(targetRoleType))
                        assignCount[targetRoleType]--;
                }
            }
            if (assignCount.All(kvp => kvp.Value <= 0)) return;

            foreach (var role in AllMainRoles.OrderBy(x => Guid.NewGuid())) //確定枠が偏らないようにシャッフル
            {
                if (!role.IsAssignable()) continue;
                if (Options.CustomRoleSpawnChances.TryGetValue(role, out var ticketOption) && !ModeCatalog.Current.ShowsOption(ticketOption)) continue;

                var chance = role.GetChance();
                var count = role.GetAssignCount();
                if (chance is 0 or 100) continue;
                if (count == 0) continue;
                for (var i = 0; i < count; i++)
                {
                    if (chance < 100 && rand.Next(100) >= chance) continue;
                    randomRoleTicketPool.Add((role, i));
                }
            }

            //確定分では足りない場合に抽選を行う
            while (assignCount.Any(kvp => kvp.Value > 0) && randomRoleTicketPool.Count > 0)
            {
                var selectedTicket = randomRoleTicketPool[rand.Next(randomRoleTicketPool.Count)];
                Logger.Info($"{selectedTicket.Item1}", "SetRandomAssignRoleList");
                var targetRoles = selectedTicket.Item1.GetAssignUnitRolesArray();
                Logger.Info($"{targetRoles}", "SetRandomAssignRoleList");
                //アサイン枠が足りていれば追加
                if (CustomRolesHelper.AllRoleTypes.Where(type => type != CustomRoleTypes.Unit).All(type => targetRoles.Count(role => role.GetAssignRoleType() == type) <= assignCount[type]))
                {
                    foreach (var targetRole in targetRoles)
                    {
                        AssignRoleList.Add(targetRole);
                        assignCount[targetRole.GetAssignRoleType()]--;
                    }
                }
                //1-9個ある同じチケットを削除
                randomRoleTicketPool.RemoveAll(x => x == selectedTicket);
            }
        }
        ///<summary>
        ///属性のアサイン抽選
        ///枠制限が無いので個別に抽選
        ///</summary>
        private static void SetAddOnsList(bool isFixedAssign = false)
        {
            foreach (var subRole in CustomRolesHelper.AllAddOnRoles)
            {
                var chance = subRole.GetChance();
                var count = subRole.GetAssignCount();
                if (chance == 0 || count == 0) continue;
                var target = subRole.IsFirstWriteAddOn() ? LaterAddOnList : AssignRoleList;
                var rnd = IRandom.Instance;
                for (var i = 0; i < count; i++) 
                    if (rnd.Next(100) < chance)
                        target.AddRange(subRole.GetAssignUnitRolesArray());
            }
        }
        private static List<CustomRoles> GetCandidateRoleList(int availableRate)
        {
            var candidateRoleList = new List<CustomRoles>();
            foreach (var role in AllMainRoles)
            {
                if (!role.IsAssignable()) continue;
                // 今のモードで設定を出していない役職 (他のモード専用の物) は、確率が残っていても配らない
                if (Options.CustomRoleSpawnChances.TryGetValue(role, out var spawnOption) && !ModeCatalog.Current.ShowsOption(spawnOption)) continue;

                var chance = role.GetChance();
                var count = role.GetAssignCount();
                if (chance < availableRate || count == 0) continue;
                candidateRoleList.AddRange(Enumerable.Repeat(role, count).ToList());
            }
            return candidateRoleList;
        }
        public static RoleAssignInfo GetRoleAssignInfo(this CustomRoles role) =>
            CustomRoleManager.GetRoleInfo(role)?.AssignInfo;
        private static CustomRoleTypes GetAssignRoleType(this CustomRoles role) =>
            role.GetRoleAssignInfo()?.AssignRoleType ?? role.GetCustomRoleTypes();
        private static bool IsAssignable(this CustomRoles role)
            => role.GetRoleAssignInfo()?.IsInitiallyAssignable ?? true;
        /// <summary>
        /// アサインの抽選回数
        /// </summary>
        private static int GetAssignCount(this CustomRoles role)
        {
            int maximumCount = role.GetCount();
            int assignUnitCount = role.GetRoleAssignInfo()?.AssignUnitCount ??
                role switch
                {
                    CustomRoles.Lovers => 2,
                    _ => 1,
                };
            return maximumCount / assignUnitCount;
        }
        ///<summary>
        ///RoleOptionのKey => 実際にアサインされる役職の配列
        ///両陣営役職、コンビ役職向け
        ///</summary>
        private static CustomRoles[] GetAssignUnitRolesArray(this CustomRoles role)
            => role.GetRoleAssignInfo()?.AssignUnitRolesGetter?.Invoke() ??
            role.GetRoleAssignInfo()?.AssignUnitRoles ??
            role switch
            {
                CustomRoles.Lovers => new CustomRoles[2] { CustomRoles.Lovers, CustomRoles.Lovers },
                _ => new CustomRoles[1] { role },
            };
        // ===== 役職の組み合わせ: 同時に出ない役職・一緒に必ず出る役職 =====
        private const int ComboSlots = 6;
        public static OptionItem OptionNotTogether, OptionTogether;
        private static readonly List<(FilterOptionItem a, FilterOptionItem b)> NotTogetherRules = new();
        private static readonly List<(FilterOptionItem a, FilterOptionItem b)> TogetherRules = new();

        private static FilterOptionItem ComboRole(OptionItem parent, string key, string labelKey, int n)
        {
            var o = new FilterOptionItem("RoleComboRole", 0, TabGroup.ModMainSettings, false, true, true, true, true, false);
            o.SetParent(parent);
            o.SetKeyName(key);
            o.SetOptionName(() => string.Format(GetString(labelKey), n));
            o.SetGameMode(CustomGameMode.Standard);
            return o;
        }

        private static void SetupComboOptions()
        {
            NotTogetherRules.Clear();
            TogetherRules.Clear();
            OptionNotTogether = BooleanOptionItem.Create("RoleComboNotTogether", false, TabGroup.ModMainSettings, false)
                .SetColor(Palette.LightBlue).SetGameMode(CustomGameMode.Standard);
            for (int i = 1; i <= ComboSlots; i++)
            {
                var a = ComboRole(OptionNotTogether, $"NotTogether{i}A", "RoleCombo.NotA", i);
                var b = ComboRole(OptionNotTogether, $"NotTogether{i}B", "RoleCombo.NotB", i);
                // 2 つは順番の無い組なので、1 つの行・1 つの一覧で 2 つまで選ぶ
                FilterOptionItem.LinkCollapsed(" × ", "RoleCombo.HintNot", a, b);
                NotTogetherRules.Add((a, b));
            }
            OptionTogether = BooleanOptionItem.Create("RoleComboTogether", false, TabGroup.ModMainSettings, false)
                .SetColor(Palette.LightBlue).SetGameMode(CustomGameMode.Standard);
            for (int i = 1; i <= ComboSlots; i++)
            {
                var a = ComboRole(OptionTogether, $"Together{i}A", "RoleCombo.IfA", i);
                var b = ComboRole(OptionTogether, $"Together{i}B", "RoleCombo.ThenB", i);
                // 1 つの行で選ぶ。1 つ目に選んだのが「出たら」、2 つ目が「一緒に必ず出る」
                FilterOptionItem.LinkCollapsed(" → ", "RoleCombo.HintTogether", a, b);
                TogetherRules.Add((a, b));
            }
        }

        private static List<(CustomRoles a, CustomRoles b)> ActiveRules(OptionItem parent, List<(FilterOptionItem a, FilterOptionItem b)> rules)
        {
            var list = new List<(CustomRoles a, CustomRoles b)>();
            if (parent == null || !parent.GetBool() || Options.CurrentGameMode != CustomGameMode.Standard) return list;
            foreach (var (a, b) in rules)
            {
                var ra = a.GetRole();
                var rb = b.GetRole();
                if (ra == CustomRoles.NotAssigned || rb == CustomRoles.NotAssigned || ra == rb) continue;
                list.Add((ra, rb));
            }
            return list;
        }

        private static bool Conflicts(CustomRoles role, IEnumerable<CustomRoles> present, List<(CustomRoles a, CustomRoles b)> notTogether)
            => notTogether.Any(r => (r.a == role && present.Contains(r.b)) || (r.b == role && present.Contains(r.a)));

        // 一人で配る役職か (組で配る役職は差し替えの候補にしない)
        private static bool IsSingle(CustomRoles role) => role < CustomRoles.StartAddon && role.GetAssignUnitRolesArray().Length == 1;

        // 抜いた役職の代わり: 同じ枠を使い、出現率が 0 でなく、まだ出ておらず、同時に出ない組にも当たらない役職をくじで
        private static CustomRoles PickReplacement(CustomRoleTypes type, List<(CustomRoles a, CustomRoles b)> notTogether, HashSet<CustomRoles> banned)
        {
            var pool = AllMainRoles.Where(r => r.IsAssignable() && IsSingle(r) && r.GetChance() > 0 && r.GetAssignCount() > 0
                    && r.GetAssignRoleType() == type && !AssignRoleList.Contains(r) && !banned.Contains(r)
                    && (!Options.CustomRoleSpawnChances.TryGetValue(r, out var o) || ModeCatalog.Current.ShowsOption(o))
                    && !Conflicts(r, AssignRoleList, notTogether))
                .ToList();
            return pool.Count == 0 ? CustomRoles.NotAssigned : pool[IRandom.Instance.Next(pool.Count)];
        }

        private static void ApplyComboRules()
        {
            var notTogether = ActiveRules(OptionNotTogether, NotTogetherRules);
            var together = ActiveRules(OptionTogether, TogetherRules);
            if (notTogether.Count == 0 && together.Count == 0) return;
            var rand = IRandom.Instance;
            var removed = new HashSet<CustomRoles>();

            // 同時に出ない組: 両方出ていたら、くじでどちらかを抜き、同じ枠の別の役職を入れる
            foreach (var (a, b) in notTogether)
            {
                if (!AssignRoleList.Contains(a) || !AssignRoleList.Contains(b)) continue;
                // 一緒に必ず出る組で守られている方は抜かない
                bool keepA = together.Any(t => t.b == a && AssignRoleList.Contains(t.a));
                bool keepB = together.Any(t => t.b == b && AssignRoleList.Contains(t.a));
                var drop = keepA && !keepB ? b : keepB && !keepA ? a : rand.Next(2) == 0 ? a : b;
                int slots = AssignRoleList.RemoveAll(r => r == drop);
                removed.Add(drop);
                Logger.Info($"同時に出ない組 {a} / {b}: {drop} を抜く", "RoleCombo");
                if (!IsSingle(drop)) continue;
                for (int i = 0; i < slots; i++)
                {
                    var rep = PickReplacement(drop.GetAssignRoleType(), notTogether, removed);
                    if (rep == CustomRoles.NotAssigned) break;
                    AssignRoleList.Add(rep);
                    Logger.Info($"代わりに {rep}", "RoleCombo");
                }
            }

            // 一緒に必ず出る組: A が出ていて B が出ていなければ、B を入れる
            int players = GameData.Instance.PlayerCount;
            int impCap = (AssignMode == AssignAlgorithm.Random || PlayerCountComposition.IsOn) && AssignCount.TryGetValue(CustomRoleTypes.Impostor, out var ic)
                ? ic : Math.Min(players, Main.RealOptionsData.GetInt(Int32OptionNames.NumImpostors));
            // B が別の組の A の時のため 2 周
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var (a, b) in together)
                {
                    if (!AssignRoleList.Contains(a) || AssignRoleList.Contains(b)) continue;
                    if (!IsSingle(b) || Conflicts(b, AssignRoleList.Where(r => r != a), notTogether))
                    {
                        Logger.Info($"一緒に出る組 {a} → {b}: {b} を入れられないので {a} を抜く", "RoleCombo");
                        AssignRoleList.RemoveAll(r => r == a);
                        continue;
                    }
                    var type = b.GetAssignRoleType();
                    // 守る役職 (出ている組の A と、その B)
                    var keep = new HashSet<CustomRoles>(together.Where(t => AssignRoleList.Contains(t.a)).SelectMany(t => new[] { t.a, t.b }));
                    var swap = Enumerable.Range(0, AssignRoleList.Count)
                        .Where(i => AssignRoleList[i] < CustomRoles.StartAddon && IsSingle(AssignRoleList[i])
                            && AssignRoleList[i].GetAssignRoleType() == type && !keep.Contains(AssignRoleList[i]))
                        .ToList();
                    if (swap.Count > 0)
                    {
                        int i = swap[rand.Next(swap.Count)];
                        Logger.Info($"一緒に出る組 {a} → {b}: {AssignRoleList[i]} を {b} に替える", "RoleCombo");
                        AssignRoleList[i] = b;
                        continue;
                    }
                    var mains = AssignRoleList.Where(r => r < CustomRoles.StartAddon).ToList();
                    int imps = mains.Count(r => r.GetAssignRoleType() == CustomRoleTypes.Impostor);
                    bool free = type == CustomRoleTypes.Impostor ? imps < impCap : mains.Count - imps < players - impCap;
                    if (free)
                    {
                        Logger.Info($"一緒に出る組 {a} → {b}: 空いた枠に {b} を足す", "RoleCombo");
                        AssignRoleList.Add(b);
                        if (AssignCount.ContainsKey(type)) AssignCount[type]++;
                    }
                    else
                    {
                        Logger.Info($"一緒に出る組 {a} → {b}: 枠が無いので {a} を抜く", "RoleCombo");
                        AssignRoleList.RemoveAll(r => r == a);
                    }
                }
            }
        }

        public static bool IsPresent(this CustomRoles role) => AssignRoleList.Any(x => x == role) || LaterAddOnList.Any(x => x == role);
        public static int GetRealCount(this CustomRoles role) => AssignRoleList.Count(x => x == role) + LaterAddOnList.Count(x => x == role);
        public static List<CustomRoles> GetAssignRoleList() => new(AssignRoleList);
    }
    public class RoleAssignInfo
    {
        public RoleAssignInfo(CustomRoles role, CustomRoleTypes roleType)
        {
            AssignRoleType = roleType;
            IsInitiallyAssignableCallBack = () => true;
            AssignCountRule =
                new(1, 15, 1);
            AssignUnitRoles =
                Enumerable.Repeat(role, AssignCountRule.Step).ToArray();
        }
        /// <summary>
        /// どのアサイン枠を消費するか
        /// </summary>
        public CustomRoleTypes AssignRoleType { get; init; }
        /// <summary>
        /// 試合開始時にアサインされるかどうかのデリゲート
        /// </summary>
        public Func<bool> IsInitiallyAssignableCallBack { get; init; }
        public bool IsInitiallyAssignable => IsInitiallyAssignableCallBack.Invoke();
        /// <summary>
        /// 人数設定の最小人数, 最大人数, 一単位数
        /// </summary>
        public IntegerValueRule AssignCountRule { get; init; }
        /// <summary>
        /// 人数設定に対し何人単位でアサインするか
        /// 役職の抽選回数 = 設定人数 / AssignUnitCount
        /// </summary>
        public int AssignUnitCount => AssignCountRule.Step;
        /// <summary>
        /// 実際にアサインされる役職の内訳
        /// </summary>
        public CustomRoles[] AssignUnitRoles { get; init; }
        /// <summary>
        /// 配る役職の内訳を、配る時に決める (設定で組の相手が変わる役職)。あれば AssignUnitRoles より先に使う
        /// </summary>
        public Func<CustomRoles[]> AssignUnitRolesGetter { get; init; }
    }
}