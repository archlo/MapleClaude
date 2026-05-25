using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// System / Game Options dialog — the authentic v95 <c>CUISysOpt</c>, drawn from
/// <c>UIWindow.img/SysOpt/backgrnd</c> (the real "SYSTEM OPTION" panel; the old code loaded a
/// non-existent <c>UIWindow.img/OptionMenu</c> node and fell back to hand-drawn chrome — that was
/// the "old UI" the user saw). The 299×366 background bakes every row label (Picture Quality, BGM,
/// Sound, Screen Shot, Mouse Cursor, HP/MP Alert, Shake, Monster Info, Viewing Mode); the
/// interactive widgets are overlaid at the exact <c>CUISysOpt::OnCreate</c> coordinates.
///
/// Only the settings this client actually supports are wired (BGM/SFX volume + mute). The volume
/// tracks are <c>SOFT … LOUD</c> baked into the panel; a knob slides x∈[95,191] on each. Changes
/// apply live and persist on close. (The legacy hand-drawn resolution selector is gone — v95's
/// panel only offers windowed/fullscreen, which this single-window client renders as windowed; the
/// saved resolution is preserved untouched.)
/// </summary>
public sealed class OptionMenu : GamePanel
{
    // ── CUISysOpt::OnCreate coordinates (window-relative) ─────────────────────────
    private const int BgmSliderX = 95, BgmSliderY = 91, SliderLen = 96;
    private const int SfxSliderX = 95, SfxSliderY = 121;
    private const int BgmMuteX = 223, BgmMuteY = 90;
    private const int SfxMuteX = 223, SfxMuteY = 120;
    private const int KnobW = 6, KnobH = 14;

    private readonly WzSprite? _background;
    private readonly WzSprite? _checkOff;
    private readonly WzSprite? _checkOn;
    private readonly Button?   _btClose;
    private readonly BuiltInFont? _font;

    // ── Settings ──────────────────────────────────────────────────────────────
    private int  _bgmVolume = 80;
    private int  _sfxVolume = 100;
    private bool _bgmMute;
    private bool _sfxMute;
    private int  _resW = 1024, _resH = 768;   // preserved across the dialog (no authentic control)

    private bool _dragBgm, _dragSfx;

    private int PanelW => _background?.Width ?? 299;
    private int PanelH => _background?.Height ?? 366;

    public OptionMenu(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = false;
        Position = new Vector2(220, 120);

        var opt = ui?.GetItem("UIWindow.img/SysOpt") as WzProperty;
        _background = opt?.Get("backgrnd") is WzCanvas bc ? loader.Load(bc) : null;

        var cb = ui?.GetItem("Basic.img/CheckBox") as WzProperty;
        _checkOff = cb?.Get("0") is WzCanvas c0 ? loader.Load(c0) : null;
        _checkOn  = cb?.Get("1") is WzCanvas c1 ? loader.Load(c1) : null;

        // Standard CUIWnd close button, top-right of the panel.
        if (ui?.GetItem("Basic.img/BtClose3") is WzProperty close)
            _btClose = new Button(loader, close) { OnClick = Close };
    }

    // ── Public API (consumed by GameStage) ───────────────────────────────────────
    public int BgmVolume => _bgmMute ? 0 : _bgmVolume;
    public int SfxVolume => _sfxMute ? 0 : _sfxVolume;
    public int ResW => _resW;
    public int ResH => _resH;

    /// <summary>Fired whenever a setting changes (volume drag / mute / close), so the host applies
    /// volumes live and persists them.</summary>
    public event Action? OnSettingsChanged;

    public void LoadVolumes(int bgm, int sfx)
    {
        _bgmVolume = Math.Clamp(bgm, 0, 100);
        _sfxVolume = Math.Clamp(sfx, 0, 100);
    }

    public void LoadResolution(int w, int h)
    {
        if (w > 0) _resW = w;
        if (h > 0) _resH = h;
    }

    private void Close()
    {
        IsVisible = false;
        OnSettingsChanged?.Invoke();
    }

    // ── Update / input ────────────────────────────────────────────────────────
    public override void Update(GameTime gameTime)
    {
        if (!IsVisible) return;
        if (_btClose != null) _btClose.Position = Position + new Vector2(PanelW - 18, 6);

        var m = Mouse.GetState();
        var held = m.LeftButton == ButtonState.Pressed;
        _btClose?.Update(m.X, m.Y, held);

        if (held && _dragBgm) { SetVolume(ref _bgmVolume, m.X); OnSettingsChanged?.Invoke(); }
        if (held && _dragSfx) { SetVolume(ref _sfxVolume, m.X); OnSettingsChanged?.Invoke(); }
        if (!held) { _dragBgm = _dragSfx = false; }
    }

    private void SetVolume(ref int vol, int mouseX)
    {
        var rel = mouseX - (Position.X + BgmSliderX);
        vol = (int)Math.Clamp(rel / SliderLen * 100f, 0, 100);
    }

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        if (_background != null)
            _background.Draw(sb, Position);
        else
        {
            var r = new Rectangle((int)Position.X, (int)Position.Y, PanelW, PanelH);
            sb.Draw(white, r, new Color(15, 15, 25, 235));
            _font?.Draw(sb, "SYSTEM OPTION", Position + new Vector2(12, 6), new Color(220, 200, 150));
        }

        // Volume knobs on the baked SOFT…LOUD tracks.
        DrawKnob(sb, white, BgmSliderX, BgmSliderY, _bgmMute ? 0 : _bgmVolume, _bgmMute);
        DrawKnob(sb, white, SfxSliderX, SfxSliderY, _sfxMute ? 0 : _sfxVolume, _sfxMute);

        // Mute checkboxes.
        DrawCheck(sb, white, BgmMuteX, BgmMuteY, _bgmMute);
        DrawCheck(sb, white, SfxMuteX, SfxMuteY, _sfxMute);

        _btClose?.Draw(sb);
    }

    private void DrawKnob(SpriteBatch sb, Texture2D white, int x, int y, int vol, bool muted)
    {
        var trackX = (int)Position.X + x;
        var trackY = (int)Position.Y + y;
        // Filled portion of the track (subtle), then the knob.
        var fill = (int)(SliderLen * Math.Clamp(vol, 0, 100) / 100f);
        sb.Draw(white, new Rectangle(trackX, trackY + KnobH / 2 - 1, fill, 2),
            muted ? new Color(120, 120, 120, 160) : new Color(90, 150, 220, 180));
        var knobX = trackX + fill - KnobW / 2;
        var knob = new Rectangle(knobX, trackY - 1, KnobW, KnobH);
        sb.Draw(white, knob, muted ? new Color(150, 150, 150) : new Color(235, 235, 245));
        DrawBorder(sb, white, knob, new Color(70, 70, 90));
    }

    private void DrawCheck(SpriteBatch sb, Texture2D white, int x, int y, bool on)
    {
        var spr = on ? _checkOn : _checkOff;
        if (spr != null) { spr.Draw(sb, Position + new Vector2(x, y)); return; }
        var box = new Rectangle((int)Position.X + x, (int)Position.Y + y, 11, 11);
        sb.Draw(white, box, new Color(25, 25, 50));
        DrawBorder(sb, white, box, new Color(80, 70, 50));
        if (on) sb.Draw(white, new Rectangle(box.X + 2, box.Y + 2, 7, 7), new Color(100, 220, 100));
    }

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        if (_btClose?.HandleMouseButton(x, y, down) == true) return true;
        if (!down) return new Rectangle((int)Position.X, (int)Position.Y, PanelW, PanelH).Contains(x, y);

        // Mute checkboxes (11×11 hit boxes).
        if (Hit(x, y, BgmMuteX, BgmMuteY, 13, 13)) { _bgmMute = !_bgmMute; OnSettingsChanged?.Invoke(); return true; }
        if (Hit(x, y, SfxMuteX, SfxMuteY, 13, 13)) { _sfxMute = !_sfxMute; OnSettingsChanged?.Invoke(); return true; }

        // Volume tracks (grab to drag; a single click also jumps the knob).
        if (Hit(x, y, BgmSliderX, BgmSliderY - 3, SliderLen + KnobW, KnobH + 6))
        { _dragBgm = true; _bgmMute = false; SetVolume(ref _bgmVolume, x); OnSettingsChanged?.Invoke(); return true; }
        if (Hit(x, y, SfxSliderX, SfxSliderY - 3, SliderLen + KnobW, KnobH + 6))
        { _dragSfx = true; _sfxMute = false; SetVolume(ref _sfxVolume, x); OnSettingsChanged?.Invoke(); return true; }

        return new Rectangle((int)Position.X, (int)Position.Y, PanelW, PanelH).Contains(x, y);
    }

    private bool Hit(int mx, int my, int x, int y, int w, int h) =>
        new Rectangle((int)Position.X + x, (int)Position.Y + y, w, h).Contains(mx, my);

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (key is Keys.Escape or Keys.Enter) { Close(); return true; }
        return true;
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r, Color c)
    {
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }
}
