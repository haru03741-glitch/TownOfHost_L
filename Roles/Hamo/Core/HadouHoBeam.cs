using System.Linq;
using UnityEngine;
using Cno = TownOfHostL.Core.CustomNetObject;

namespace TownOfHostL.Modules;

/// <summary>
/// 波動砲系 (波動砲・シェリフ波動砲・ジャッカル波動砲) のためとビームを、CNO の名前の絵で描く。
/// 一発ごとに 2 体を出す: 撃ち手の前に置く★のゲートと、ゲートの前端から伸びる線 (発射の 1 秒前から ⚠ の予告、撃つとビーム)。
/// 撃ち終わり・会議・死亡で消す。
///
/// 撃つ向きは、ため始めまでにホストへ届いた位置の左右の動きで決め、ための間は変えない。客の体の向き (FlipX) は
/// ホストへ送られず、ホストの手元の向きは動けないため中には変わらないので、FlipX で決めると客の見ている向きと逆へ撃つことがある。
/// CNO は出してよい時 (イントロ明け 13 秒・会議明け 10 秒の後) まで待ってから出るので、呼ぶ側は <see cref="Ready"/> まで撃たない。
/// ただし <see cref="MaxWait"/> を過ぎても出ない時は、ためが終わらないまま固まらないよう撃たせる。
/// 作るのはホストだけ。客の手元では何もしない。
///
/// 見た目は End K not の波動砲に揃えた: ゲートは撃ち手の前 1.5・上 0.3 の大きな★、線は 20 文字の ━ (予告は ⚠) で、
/// 名前の文字は中央寄せなので、透明な列と見える列を同じ長さで並べ、境目 (= 線の CNO の位置) から伸びるようにする。
/// 大きさは % ではなく絶対値で書く (% は 600% 辺りで頭打ちになり、700% を超えると本編の描画が崩れる)。
/// CNO は客の画面で体も描かれる (体だけを消す手段が無い) ので、体はゲートと同じ色にして★の下に置く。
/// </summary>
public sealed class HadouHoBeam
{
    private const float GateForward = 1.5f, GateUp = 0.3f, GateRadius = 0.5f;
    private const string GateSprite = "<size=24><color=#ff7a00>★</color></size>";
    private const byte BodyColor = 4; // オレンジ (ゲートの色)
    /// <summary>ふつうのビームの太さ (太さ 2 × 30)。</summary>
    public const int NormalSize = 60;
    /// <summary>超波動砲の太さ (太さ 4 × 30)。</summary>
    public const int SuperSize = 120;
    /// <summary>波動砲 (インポスター) の予告の大きさ。シェリフとジャッカルはビームと同じ大きさ。</summary>
    public const int SmallWarningSize = 16;
    private const int Chars = 20;
    private static readonly string Row = new('━', Chars);
    private static readonly string WarningRow = string.Join("   ", Enumerable.Repeat("⚠", Chars));
    // 撃つ何秒前から予告を出すか
    private const float WarningLead = 1f;
    // 会議明けの 10 秒と、ほかの CNO の順番待ちを見込んだ上限
    private const float MaxWait = 15f;
    // 向きを変える逆向きの動き: 1 フレーム MinStep 以上の逆向きの動きが TurnFrames 回以上 (間に MinStep 以上前へ戻らずに) 続き、
    // 一番進んだ地点から合わせて TurnDistance 以上戻った時。ホストに届く位置の揺れ (2026-11-09 実機で測定):
    // 止まった後に 1 フレーム 0.014 ほど逆へ数フレーム戻る (合わせて 0.04 ほど)・歩き終わりや歩く途中に 1 フレームだけ 0.05〜0.10 逆へ跳ぶ。
    // いちばん短い入力は 1 フレーム 0.03〜0.04 を 3〜4 フレーム続けて動く (合わせて 0.09〜0.15)。
    private const float MinStep = 0.02f, TurnDistance = 0.06f;
    private const int TurnFrames = 2;
    // 1 フレームでこれより動いたら、歩きではなく飛び (ベント・テレポート) として向きに数えない
    private const float JumpDistance = 1.5f;

    private readonly PlayerControl owner;
    // MOD を入れている人とホストが自前の演出 (ClientEffects) でビームを描く役職か
    private readonly bool drawnByClientEffects;
    private Cno gate, line;
    private bool facingLeft;
    private bool warned;
    private int warningSize;
    private uint hiddenGate, hiddenLine;
    private float chargeStartedAt;
    // 今の向きで一番進んだ x と、前のフレームの位置
    private float extremeX;
    private Vector2 lastPos;
    private int reverseFrames;
    private bool tracking;

    /// <summary>撃つ向き。ため始めに固定される。</summary>
    public bool FacingLeft => facingLeft;

    public HadouHoBeam(PlayerControl owner, bool drawnByClientEffects)
    {
        this.owner = owner;
        this.drawnByClientEffects = drawnByClientEffects;
    }

    /// <summary>撃ってよいか: ゲートと線が客の画面に出た、CNO を使っていない、または待ちの上限を過ぎた。</summary>
    public bool Ready => gate == null
        || (gate.IsLive && (line == null || line.IsLive))
        || Time.realtimeSinceStartup - chargeStartedAt >= MaxWait;

    /// <summary>
    /// ためていない間、ホストの毎 fixed update で呼ぶ。届いた位置の左右の動きから撃つ向きを決める。
    /// 今の向きで一番進んだ地点から、逆へ <see cref="TurnDistance"/> 以上戻ったら向きを変える。
    /// </summary>
    public void TrackDirection()
    {
        if (owner == null) return;
        var pos = owner.GetTruePosition();
        if (!tracking || (pos - lastPos).sqrMagnitude > JumpDistance * JumpDistance)
        {
            if (!tracking) facingLeft = owner.cosmetics != null && owner.cosmetics.FlipX;
            tracking = true;
            extremeX = pos.x;
            lastPos = pos;
            reverseFrames = 0;
            return;
        }
        float step = pos.x - lastPos.x;
        lastPos = pos;
        bool forward = facingLeft ? step < 0f : step > 0f;
        if (forward)
        {
            if (Mathf.Abs(step) >= MinStep) reverseFrames = 0;
            if (facingLeft ? pos.x < extremeX : pos.x > extremeX) extremeX = pos.x;
            return;
        }
        if (Mathf.Abs(step) < MinStep) return;
        reverseFrames++;
        if (reverseFrames < TurnFrames || Mathf.Abs(pos.x - extremeX) < TurnDistance) return;
        facingLeft = !facingLeft;
        extremeX = pos.x;
        reverseFrames = 0;
    }

    /// <summary>ためを始める。<see cref="FacingLeft"/> の側にゲートを出し、撃ち終わるまで向きを変えない。</summary>
    public void BeginCharge(int warningSize)
    {
        End();
        // ため中は動けないが、撃ち終わった後の最初の動きを古い位置から測らないよう測り直す
        tracking = false;
        if (owner == null) return;
        // 全員が自前の演出で描くなら、CNO は要らない
        if (drawnByClientEffects && ClientEffects.SkipNovaDummies) return;
        this.warningSize = warningSize;
        warned = false;
        chargeStartedAt = Time.realtimeSinceStartup;
        gate = Cno.Create(GateSprite, GatePosition(), BodyColor);
        line = Cno.Create("", LinePosition(), BodyColor);
    }

    /// <summary>ための間に毎 fixed update で呼ぶ。撃つ 1 秒前に予告を出す。途中で消えた CNO は出し直す。</summary>
    public void Charging(float remaining)
    {
        if (gate == null || owner == null) return;
        if (gate.IsGone) gate = Cno.Create(GateSprite, GatePosition(), BodyColor);
        if (line == null || line.IsGone) line = Cno.Create(warned ? WarningSprite() : "", LinePosition(), BodyColor);
        if (gate == null || line == null) return;
        if (!warned && remaining <= WarningLead)
        {
            warned = true;
            line.ChangeSprite(WarningSprite());
        }
    }

    /// <summary>ゲートの前端からビームを伸ばす。<paramref name="colorHex"/> は "#RRGGBB"、<paramref name="size"/> は太さ。</summary>
    public void Fire(string colorHex, int size)
    {
        if (gate == null || line == null || owner == null) return;
        line.ChangeSprite(Padded($"<color={colorHex}FF>{Row}</color>", Row, size));
    }

    public void End()
    {
        gate?.Despawn();
        line?.Despawn();
        gate = line = null;
        hiddenGate = hiddenLine = 0;
    }

    /// <summary>ホストの毎 fixed update で呼ぶ。自前の演出で描く人 (ホストを含む) の画面からは CNO を隠す。</summary>
    public void Tick()
    {
        if (!drawnByClientEffects || !ClientEffects.NovaOn) return;
        Hide(gate, ref hiddenGate);
        Hide(line, ref hiddenLine);
    }

    private static void Hide(Cno cno, ref uint hidden)
    {
        if (cno == null) return;
        uint netId = cno.NetId;
        if (netId == 0 || netId == hidden) return;
        if (ClientEffects.HideDummy(netId)) hidden = netId;
    }

    private string WarningSprite() => Padded($"<color=#FFFF00FF>{WarningRow}</color>", WarningRow, warningSize);

    // 見える列と同じ長さの透明な列を後ろ (左向きなら右) に並べ、境目が CNO の位置に来るようにする
    private string Padded(string visible, string row, int size)
        => facingLeft ? $"<size={size}>{visible}<alpha=#00>{row}</size>" : $"<size={size}><alpha=#00>{row}{visible}</size>";

    private Vector2 Direction => facingLeft ? Vector2.left : Vector2.right;

    private Vector2 GatePosition()
        => (Vector2)owner.transform.position + new Vector2(Direction.x * GateForward, GateUp);

    private Vector2 LinePosition() => GatePosition() + Direction * GateRadius;
}
