using MapleClaude.UI;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.UI.Game;

/// <summary>
/// Floating status-message queue. Up to 6 messages visible simultaneously.
/// Each fades over 8 seconds then is removed (FIFO).
/// Anchored at (10, 350) by default — above the chat bar.
///
/// Message colours:
///   White  = general / loot pick-up
///   Yellow = experience gain
///   Red    = damage taken / death
///   Green  = HP/MP recovery
///   Cyan   = quest progress
///   Orange = level-up / skill unlock
/// WZ: none — rendered entirely from font + colored rectangles.
/// </summary>
public sealed class StatusMessenger : GamePanel
{
    public enum MsgColor { White, Yellow, Red, Green, Cyan, Orange, Purple }

    private static readonly Color[] MsgColors =
    [
        Color.White,                    // White
        new Color(255, 230, 60),        // Yellow  (EXP)
        new Color(255, 80,  80),        // Red     (damage)
        new Color(80,  220, 100),       // Green   (heal)
        new Color(80,  210, 255),       // Cyan    (quest)
        new Color(255, 160, 40),        // Orange  (level)
        new Color(200, 80,  255),       // Purple  (buff)
    ];

    private const int MaxMessages   = 6;
    private const float FadeDuration = 8f;
    private const float FadeStart    = 6f;   // start fading at 6s, gone at 8s
    private const float LineHeight   = 16f;
    private const float BgAlpha      = 0.55f;

    private sealed class MsgEntry
    {
        public string  Text;
        public Color   Color;
        public float   Age;           // seconds since shown
        public MsgEntry(string t, Color c) { Text = t; Color = c; }
    }

    private readonly System.Collections.Generic.Queue<MsgEntry> _msgs = new();
    private readonly BuiltInFont? _font;

    public StatusMessenger(BuiltInFont? font)
    {
        _font = font;
        IsVisible = true;
        Position = new Vector2(10, 340);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void Show(string text, MsgColor color = MsgColor.White)
    {
        if (_msgs.Count >= MaxMessages) _msgs.Dequeue();
        _msgs.Enqueue(new MsgEntry(text, MsgColors[(int)color]));
    }

    public void ShowEXP(int amount)       => Show($"+{amount:N0} EXP",      MsgColor.Yellow);
    public void ShowLoot(string itemName) => Show($"[{itemName}]",           MsgColor.White);
    public void ShowHeal(int hp, int mp)
    {
        if (hp > 0) Show($"+{hp:N0} HP", MsgColor.Green);
        if (mp > 0) Show($"+{mp:N0} MP", MsgColor.Cyan);
    }
    public void ShowDamage(int damage)    => Show($"-{damage:N0} HP",        MsgColor.Red);
    public void ShowLevelUp(int level)    => Show($"LEVEL UP! → Lv.{level}", MsgColor.Orange);
    public void ShowQuest(string name)    => Show($"[Quest] {name}",          MsgColor.Cyan);
    public void ShowBuff(string name)     => Show($"[Buff] {name}",           MsgColor.Purple);

    // ── Update ────────────────────────────────────────────────────────────────

    public override void Update(GameTime gt)
    {
        if (!IsVisible) return;
        var dt = (float)gt.ElapsedGameTime.TotalSeconds;
        foreach (var m in _msgs) m.Age += dt;
        // Dequeue expired messages
        while (_msgs.Count > 0 && _msgs.Peek().Age >= FadeDuration)
            _msgs.Dequeue();
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible || _font is null || _msgs.Count == 0) return;

        var py = Position.Y + (_msgs.Count - 1) * LineHeight;
        foreach (var m in _msgs)
        {
            var fade = m.Age >= FadeStart
                ? 1f - (m.Age - FadeStart) / (FadeDuration - FadeStart)
                : 1f;
            fade = Math.Clamp(fade, 0f, 1f);

            var alpha = (byte)(255 * fade);
            var textColor = m.Color with { A = alpha };
            var bgAlpha   = (byte)(255 * fade * BgAlpha);

            var sz  = _font.Measure(m.Text);
            var bgR = new Rectangle((int)Position.X - 2, (int)py - 1, (int)sz.X + 4, (int)sz.Y + 2);
            sb.Draw(white, bgR, new Color(0, 0, 0, bgAlpha));
            _font.Draw(sb, m.Text, new Vector2(Position.X, py), textColor);

            py -= LineHeight;
        }
    }
}
