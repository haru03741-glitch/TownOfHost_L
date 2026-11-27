using AmongUs.GameOptions;

namespace TownOfHostL.Modules
{
    public class NormalGameOptionsSender : GameOptionsSender
    {
        public override IGameOptions BasedGameOptions
        {
            get
            {
                if (GameOptionsManager.Instance == null) return null;
                if (GameOptionsManager.Instance.CurrentGameOptions != null) return GameOptionsManager.Instance.CurrentGameOptions;
                var fallback = GameOptionsManager.Instance.currentNormalGameOptions;
                return fallback == null ? null : (IGameOptions)(object)fallback;
            }
        }

        public override bool IsDirty
        {
            get
            {
                if (GameManager.Instance == null)
                {
                    _logicOptions = null;
                    return false;
                }

                if (_logicOptions == null || GameManager.Instance.LogicComponents == null || !GameManager.Instance.LogicComponents.Contains(_logicOptions))
                {
                    _logicOptions = null;
                    foreach (var glc in GameManager.Instance.LogicComponents)
                    {
                        if (glc.TryCast<LogicOptions>(out var lo))
                        {
                            _logicOptions = lo;
                            break;
                        }
                    }
                }
                return _logicOptions?.IsDirty ?? false; // nullならfalse
            }
            protected set
            {
                _logicOptions?.ClearDirtyFlag(); // nullなら何もしない
            }
        }

        private LogicOptions _logicOptions;

        public override IGameOptions BuildGameOptions()
        {
            var opt = BasedGameOptions;
            // 試合の基へ戻すのは試合の間 (今の設定が試合用の写し) だけ。
            // 試合を出た後の今の設定は部屋の設定の器なので、書き戻すとロビーで変えた値が前の試合の値に戻り、保存もされる
            if (opt != null && Main.RealOptionsData != null && RoomOptions.Entered)
                Main.RealOptionsData.Restore(opt);
            return opt;
        }
    }
}