using MapleClaude.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.Character;

/// <summary>
/// Manages a pool of floating damage / heal numbers above the game world.
/// Each entry rises for 0.7 s then fades for 0.3 s.
/// Caller adds entries via <see cref="Add"/>; <see cref="Update"/> and
/// <see cref="Draw"/> are called each frame from <see cref="Stages.GameStage"/>.
/// </summary>
public sealed class DamageNumber
{
    public enum Kind
    {
        DamageNormal,   // white
        DamageCrit,     // yellow
        DamageMiss,     // grey   "MISS"
        HealHp,         // green
        HealMp,         // cyan
        MobDamage,      // red (mob hit by player — shown above mob)
        Exp,            // yellow-green  "+N EXP"
    }

    private static readonly Color[] KindColors =
    [
        Color.White,
        new Color(255, 220, 40),
        new Color(160, 160, 160),
        new Color(80,  220, 80),
        new Color(80,  200, 255),
        new Color(255, 60,  60),
        new Color(120, 255, 80),
    ];

    private sealed class Entry
    {
        public string  Text;
        public Color   Color;
        public Vector2 WorldPos;
        public float   Age;
        public float   Vy;          // upward velocity (world units/s)
        public Kind    Kind;        // needed by Draw to pick the right damage-skin sprite set
        public Entry(string t, Color c, Vector2 p, Kind kind)
        { Text = t; Color = c; WorldPos = p; Vy = -80f; Kind = kind; }
    }

    private const float RiseDuration = 0.7f;
    private const float FadeDuration = 0.3f;
    private const float TotalLife    = RiseDuration + FadeDuration;

    private readonly List<Entry> _entries = new();
    private readonly BuiltInFont? _font;
    /// <summary>WZ damage-skin sprite digits (Effect.wz/BasicEff.img/NoRed1/NoCri1/NoMiss).
    /// When loaded, damage / crit / miss numbers render with proper v95 sprite digits;
    /// otherwise they fall back to the BuiltInFont text rendering.</summary>
    private readonly DamageDigits? _digits;

    public DamageNumber(BuiltInFont? font, DamageDigits? digits = null)
    {
        _font   = font;
        _digits = digits;
    }

    // ── API ───────────────────────────────────────────────────────────────────

    public void Add(int value, Vector2 worldPos, Kind kind = Kind.MobDamage)
    {
        var text = kind switch
        {
            Kind.DamageMiss  => "MISS",
            Kind.HealHp      => $"+{value:N0}",
            Kind.HealMp      => $"+{value:N0}",
            Kind.Exp         => $"+{value:N0} EXP",
            _                => value.ToString("N0"),
        };
        // Slight random horizontal spread so stacked numbers don't overlap
        var spread = (float)(System.Random.Shared.NextDouble() * 20 - 10);
        _entries.Add(new Entry(text, KindColors[(int)kind], worldPos + new Vector2(spread, 0), kind));
    }

    public void AddMiss(Vector2 worldPos) => Add(0, worldPos, Kind.DamageMiss);

    // ── Update ────────────────────────────────────────────────────────────────

    public void Update(float dt)
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            var e = _entries[i];
            e.Age           += dt;
            e.WorldPos      += new Vector2(0, e.Vy * dt);
            e.Vy            = Math.Min(0, e.Vy + 40f * dt);  // decelerate
            if (e.Age >= TotalLife) _entries.RemoveAt(i);
        }
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public void Draw(SpriteBatch sb, Texture2D white,
                     Func<Vector2, Vector2> worldToScreen)
    {
        foreach (var e in _entries)
        {
            var fade  = e.Age >= RiseDuration
                ? 1f - (e.Age - RiseDuration) / FadeDuration
                : 1f;
            var alpha = (byte)(255 * Math.Clamp(fade, 0f, 1f));
            var color = e.Color with { A = alpha };
            var screenPos = worldToScreen(e.WorldPos);

            // For damage / crit / miss / mob-damage, prefer the v95 WZ damage-skin
            // sprites — the floating numbers above mobs should look like real Maple
            // digits, not text. Other kinds (heal / EXP) have no native sprite set
            // in v95, so they always render as text.
            var rendered = false;
            if (_digits is not null)
            {
                rendered = e.Kind switch
                {
                    Kind.DamageMiss                                        => _digits.DrawMiss(sb, screenPos, alpha),
                    Kind.DamageCrit                                        => _digits.DrawNumber(sb, e.Text, screenPos, alpha, crit: true),
                    Kind.DamageNormal or Kind.MobDamage                    => _digits.DrawNumber(sb, e.Text, screenPos, alpha, crit: false),
                    _                                                       => false,
                };
            }

            if (!rendered && _font is not null)
            {
                var sz = _font.Measure(e.Text);
                _font.Draw(sb, e.Text,
                    new Vector2(screenPos.X - sz.X / 2f, screenPos.Y - sz.Y),
                    color);
            }
        }
    }
}
