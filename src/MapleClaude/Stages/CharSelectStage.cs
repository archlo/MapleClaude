using MapleClaude.App;
using MapleClaude.Character;
using MapleClaude.Debug;
using MapleClaude.Domain;
using MapleClaude.Map;
using MapleClaude.Net.Handlers;
using MapleClaude.Net.Packet;
using MapleClaude.Net.Senders;
using MapleClaude.Net.Session;
using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.Stages;

/// <summary>
/// Character-select screen (step 2 of login). Mirrors the v95 client's
/// <c>CUIAvatar</c> + <c>CUICharSelect</c>: three character slots per page laid
/// out in the scrolling login-map space, the BtSelect/BtNew/BtDelete buttons in
/// a vertical stack, and left/right page arrows. Each character stands on a job
/// signpost (<c>CharSelect/&lt;jobGroup&gt;</c>); selecting one rolls a stat
/// scroll (<c>CharSelect/scroll</c> + <c>RollDown</c>) over the avatar showing
/// the <c>charInfo1</c> card. The "CH." channel header sits top-right.
///
/// All slot/button/arrow coordinates are authentic login-map values placed on
/// screen via <see cref="MapScene.WorldToScreen"/>, so they ride the camera.
/// </summary>
public sealed class CharSelectStage : Stage
{
    private const int SlotCount = 3;   // 3 avatars per page (CUIAvatar), paged
    private const string DebugCat = "CharSelect";

    // ---- Authentic char-select map coordinates (login-map space) ----
    private static readonly Vector2 SlotBaseMap = new(-120, -1138);  // slot 0 avatar anchor (feet)
    private static readonly Vector2 SlotStepMap = new(125, 0);
    private static readonly Vector2 PageLMap = new(-260, -1215);
    private static readonly Vector2 PageRMap = new(188, -1213);
    private static readonly Vector2 BtSelectMap = new(259, -1361);
    private static readonly Vector2 BtNewMap = new(259, -1323);
    private static readonly Vector2 BtDeleteMap = new(259, -1274);

    private readonly ILogger<CharSelectStage> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly WzPackage? _ui;
    private readonly WzPackage? _map;
    private readonly WzPackage? _sound;
    private readonly int _worldId;
    private readonly int _channelId;
    private readonly Vector2 _cameraStart;
    private readonly Vector2 _loginCameraOffset;

    private WzTextureLoader? _loader;
    private MapScene? _scene;
    private CharacterRenderer? _charRenderer;

    // Shared chrome (overlay)
    private WzSprite? _commonFrame;
    private WzSprite? _stepIndicator;   // step/2 "Select a character"
    private WzSprite? _chLabel;         // Common/selectWorld ("CH.") — top-right

    // CharSelect sprites
    private WzSprite? _charEmpty;       // character/1 — empty-slot figure
    private WzSprite? _charInfo;        // charInfo1 stat card (selected char)
    private WzSprite? _effectSelected;  // effect/0 — selection glow
    private WzSprite? _pageL;           // pageL/0 arrow
    private WzSprite? _pageR;           // pageR/0 arrow
    private readonly Dictionary<string, WzSprite?> _platforms = new(); // job signposts
    private readonly WzSprite?[] _scrollFrames = new WzSprite?[4];     // stat scroll unroll
    private WzSprite? _charInfoNoRank;   // charInfo (115h) — stat board when the char has no rank
    private WzSprite? _rankUp, _rankDown, _rankSame;   // ranking move arrows (icon/{up,down,same})

    // Buttons (vertical stack, right side)
    private Button? _btSelect;
    private Button? _btNew;
    private Button? _btDelete;
    private Button? _btBack;   // Common/BtStart "Back" -> world select (screen-space)
    private readonly List<Button> _allButtons = new();

    // Overlays
    private LoginNoticeOverlay? _notice;
    private SoftKeyOverlay? _softKey;   // PIC/secondary-password number pad (delete + select)
    private SystemNoticeOverlay? _sysNotice;   // baked "must register a PIC" popup (Notice/text/95)

    // Sounds
    private WzSound? _rollDown;

    // State
    private int _selectedSlot = -1;     // absolute index into Session.Characters
    private int _page;
    private float _scrollAnimT;         // 0..1 stat-scroll unroll progress
    private float _walkAnimT;           // selected-char walk-animation clock
    private int _lastClickSlot = -1;    // double-click (= Start) tracking
    private DateTime _lastClickTime;

    // Camera scroll
    private float _scrollT;
    private const float ScrollDuration = 0.55f;
    private const float ScrollUnrollDuration = 0.28f;
    private const float WalkFrameDur = 0.18f;          // selected-char walk frame duration

    // Tunables (debug-window editable)
    private Vector2 _cameraOffset = new(28, -1208);   // authentic step-2 camera (GetStepHeight)
    private Vector2 _stepIndicatorPos = new(0, 0);    // step/2 top-left, flush to top
    private Vector2 _chLabelPos = new(688, 18);       // "CH." top-right against the frame
    private Vector2 _slotNudge = Vector2.Zero;        // map-space fine nudge for the avatar row
    private Vector2 _scrollOffset = new(-108, -320);  // stat scroll TL relative to char foot (above the avatar)
    private Vector2 _cardOffset = new(17, 30);        // charInfo1 card TL within the scroll
    private Vector2 _btBackPos = new(0, 546);         // "Back" button, screen-space (CUILoginStart)

    private List<CharacterEntry> Characters => Game.Session.Characters;
    private int MaxPage => Characters.Count == 0 ? 0 : (Characters.Count - 1) / SlotCount;

    public CharSelectStage(
        ILogger<CharSelectStage> logger,
        ILoggerFactory loggerFactory,
        WzPackage? ui,
        WzPackage? map,
        WzPackage? sound,
        int worldId,
        int channelId,
        Vector2 cameraStart,
        Vector2 loginCameraOffset)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _ui = ui;
        _map = map;
        _sound = sound;
        _worldId = worldId;
        _channelId = channelId;
        _cameraStart = cameraStart;
        _loginCameraOffset = loginCameraOffset;
    }

    /// <summary>
    /// Re-entry constructor used after a successful <c>CreateNewCharacterResult</c>.
    /// World / channel / camera state are restored from the previous selection
    /// (held on the session). The character list lives on
    /// <c>Game.Session.Characters</c>; <paramref name="existingCharacters"/> is
    /// retained for call-site API stability.
    /// </summary>
    public CharSelectStage(
        ILogger<CharSelectStage> logger,
        ILoggerFactory loggerFactory,
        WzPackage? ui,
        WzPackage? map,
        WzPackage? sound,
        IReadOnlyList<CharacterEntry> existingCharacters)
        : this(logger, loggerFactory, ui, map, sound,
               worldId: 0, channelId: 0,
               cameraStart: Vector2.Zero, loginCameraOffset: new Vector2(28, -1208))
    {
        _ = existingCharacters;
    }

    public override void OnEnter(MapleClaudeGame game)
    {
        base.OnEnter(game);
        _loader = new WzTextureLoader(GraphicsDevice);

        if (_ui is null || _map is null)
        {
            _logger.LogError("CharSelectStage: WZ packages unavailable");
            return;
        }

        if (_ui.GetItem("MapLogin1.img") is WzImage loginMap)
        {
            _scene = new MapScene(_loggerFactory.CreateLogger<MapScene>(), _map, _loader);
            _scene.Load(loginMap.Root);
            _scene.Camera = _cameraStart;
        }

        _charRenderer = new CharacterRenderer(
            _loggerFactory.CreateLogger<CharacterRenderer>(), Game.CharWz, Game.ItemWz, Game.BaseWz, _loader);

        _commonFrame = LoadCanvas("Login.img/Common/frame");
        _stepIndicator = LoadCanvas("Login.img/Common/step/2");
        _chLabel = LoadCanvas("Login.img/Common/selectWorld");

        _charEmpty = LoadCanvas("Login.img/CharSelect/character/1/0");
        _charInfo = LoadCanvas("Login.img/CharSelect/charInfo1");
        _charInfoNoRank = LoadCanvas("Login.img/CharSelect/charInfo");
        _rankUp = LoadCanvas("Login.img/CharSelect/icon/up/0") ?? LoadCanvas("Login.img/CharSelect/icon/up");
        _rankDown = LoadCanvas("Login.img/CharSelect/icon/down/0") ?? LoadCanvas("Login.img/CharSelect/icon/down");
        _rankSame = LoadCanvas("Login.img/CharSelect/icon/same/0") ?? LoadCanvas("Login.img/CharSelect/icon/same");
        _effectSelected = LoadCanvas("Login.img/CharSelect/effect/0/0");
        _pageL = LoadCanvas("Login.img/CharSelect/pageL/0/0");
        _pageR = LoadCanvas("Login.img/CharSelect/pageR/0/0");

        // Job signposts that stand behind each character (by class).
        foreach (var g in new[] { "adventure", "knight", "aran", "evan", "resistance" })
        {
            _platforms[g] = LoadCanvas($"Login.img/CharSelect/{g}/0");
        }

        // Stat-scroll unroll frames (rolled 217x30 -> unrolled parchment 217x193).
        for (var i = 0; i < _scrollFrames.Length; i++)
        {
            _scrollFrames[i] = LoadCanvas($"Login.img/CharSelect/scroll/{i}/0");
        }

        _btSelect = MakeButton("Login.img/CharSelect/BtSelect", OnSelectClicked);
        _btNew = MakeButton("Login.img/CharSelect/BtNew", OnNewClicked);
        _btDelete = MakeButton("Login.img/CharSelect/BtDelete", OnDeleteClicked);
        _btBack = MakeButton("Login.img/Common/BtStart", GoBack);   // "Back" -> world select

        if (_btSelect != null) _btSelect.Enabled = false;
        if (_btDelete != null) _btDelete.Enabled = false;

        _notice = new LoginNoticeOverlay(_loader, _ui, Game.Font, new Vector2(400, 300));
        _softKey = new SoftKeyOverlay(_loader, _ui, Game.BasicFont ?? Game.Font, new Vector2(400, 300));
        _sysNotice = new SystemNoticeOverlay(_loader, _ui, new Vector2(400, 300));

        _rollDown = _sound?.GetItem("UI.img/RollDown") as WzSound;
        if (_sound?.GetItem("UI.img/CharSelect") is WzSound enter)
        {
            Game.AudioPlayer.PlayEffect(enter);
        }

        ApplyLayout();
        RegisterDebugItems();

        Game.LoginHandlers.OnSelectCharacterResult += OnSelectCharacterResult;
        Game.LoginHandlers.OnDeleteCharacterResult += OnDeleteCharacterResult;
        Game.LoginHandlers.OnCheckSpwFailed += OnCheckSpwFailed;

        _logger.LogInformation(
            "CharSelectStage: world={World} channel={Channel} chars={N}",
            _worldId, _channelId, Characters.Count);
    }

    public override void OnExit()
    {
        Game.LoginHandlers.OnSelectCharacterResult -= OnSelectCharacterResult;
        Game.LoginHandlers.OnDeleteCharacterResult -= OnDeleteCharacterResult;
        Game.LoginHandlers.OnCheckSpwFailed -= OnCheckSpwFailed;
        UnregisterDebugItems();
        _loader?.Dispose();
        _loader = null;
        base.OnExit();
    }

    private void OnSelectCharacterResult(SelectCharacterResultArgs args)
    {
        if (!args.Success || args.ChannelHost is null)
        {
            _notice?.Show($"Could not enter character (code {args.ResultCode}).", LoginNoticeOverlay.NoticeType.Ok);
            return;
        }
        _logger.LogInformation("SelectCharacterResult ok charId={Cid} — starting migration", args.CharacterId);
        Game.CharacterId = args.CharacterId;
        var fieldStage = new GameStage(
            _loggerFactory.CreateLogger<GameStage>(),
            _loggerFactory, _ui, _map, _sound,
            Game.CharWz, Game.NpcWz, Game.MobWz);
        Game.StageDirector.Replace(fieldStage);
        _ = Task.Run(async () =>
        {
            try
            {
                await Game.Migration.BeginMigrateAsync(args.ChannelHost, args.ChannelPort, args.CharacterId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Migration failed");
            }
        });
    }

    private void OnDeleteCharacterResult(DeleteCharacterArgs args)
    {
        if (args.Success)
        {
            _selectedSlot = -1;
            if (_btSelect != null) _btSelect.Enabled = false;
            if (_btDelete != null) _btDelete.Enabled = false;
            _page = Math.Min(_page, MaxPage);
            _notice?.Show("Character deleted.", LoginNoticeOverlay.NoticeType.Ok);
        }
        else
        {
            _notice?.Show(DeleteFailMessage(args.ResultCode), LoginNoticeOverlay.NoticeType.Ok);
        }
    }

    // LoginResultType codes the server returns for a failed delete (mirrors
    // upstream Kinoko handleDeleteCharacter / LoginResultType).
    private static string DeleteFailMessage(byte code) => code switch
    {
        20 => "Incorrect PIC / 2nd password.",                   // IncorrectSPW
        6  => "Could not delete the character (server error).",  // DBFail
        22 => "A guild master's character cannot be deleted.",
        24 => "An engaged character cannot be deleted.",
        29 => "A character in a family cannot be deleted.",
        _  => $"Could not delete the character (code {code}).",
    };

    public override void Update(GameTime gameTime)
    {
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_scene != null)
        {
            _scrollT = Math.Min(1f, _scrollT + dt / ScrollDuration);
            var sp = _scene.StartPoint ?? Vector2.Zero;
            var target = sp + _cameraOffset;
            _scene.Camera = Vector2.Lerp(_cameraStart, target, SmoothStep(_scrollT));
        }
        if (_selectedSlot >= 0)
        {
            _scrollAnimT = Math.Min(1f, _scrollAnimT + dt / ScrollUnrollDuration);
        }
        _walkAnimT += dt;

        ApplyLayout();
        _notice?.Update(gameTime);
        _softKey?.Update(gameTime);
        _sysNotice?.Update(gameTime);
    }

    public override void Draw(GameTime gameTime, SpriteBatch sb)
    {
        var pp = GraphicsDevice.PresentationParameters;
        var w = pp.BackBufferWidth;
        var h = pp.BackBufferHeight;

        if (_scene != null)
            _scene.Draw(sb, Game.WhitePixel, w, h);
        else
            sb.Draw(Game.WhitePixel, pp.Bounds, new Color(10, 16, 36));

        // The wooden stage signboard is drawn by MapScene from the login map.
        _commonFrame?.Draw(sb, new Vector2(w / 2f, h / 2f));
        DrawFrameMuteButton(sb);
        _stepIndicator?.Draw(sb, _stepIndicatorPos);
        _chLabel?.Draw(sb, _chLabelPos);   // "CH." channel header, top-right

        DrawSlots(sb);
        DrawStatScroll(sb);

        if (MaxPage > 0)
        {
            if (_page > 0) _pageL?.Draw(sb, MapToScreen(PageLMap));
            if (_page < MaxPage) _pageR?.Draw(sb, MapToScreen(PageRMap));
        }

        foreach (var b in _allButtons)
            b.Draw(sb);

        _notice?.Draw(sb, Game.WhitePixel);
        _softKey?.Draw(sb, Game.WhitePixel);
        _sysNotice?.Draw(sb, Game.WhitePixel);
    }

    private void DrawSlots(SpriteBatch sb)
    {
        for (var i = 0; i < SlotCount; i++)
        {
            var absIdx = _page * SlotCount + i;
            var pos = SlotScreen(i);
            var hasChar = absIdx < Characters.Count;

            if (absIdx == _selectedSlot)
                _effectSelected?.Draw(sb, pos);

            if (!hasChar)
            {
                _charEmpty?.Draw(sb, pos);
                continue;
            }

            var entry = Characters[absIdx];

            // Job signpost stands behind the avatar.
            if (_platforms.TryGetValue(JobGroup(entry.Stat.Job), out var platform))
                platform?.Draw(sb, pos);

            // Avatar standing on the slot. The SELECTED character walks in place (walk1, or
            // walk2 for a two-handed weapon e.g. a polearm); the rest stand still.
            if (absIdx == _selectedSlot && _charRenderer != null)
            {
                var stance = _charRenderer.IsTwoHanded(entry.Look) ? Stance.Walk2 : Stance.Walk1;
                var fr = (int)(_walkAnimT / WalkFrameDur) % _charRenderer.FrameCount(entry.Look, stance);
                _charRenderer.Draw(sb, entry.Look, entry.Stat, stance, fr, pos, facingLeft: false);
            }
            else
            {
                _charRenderer?.Draw(sb, entry.Look, entry.Stat, Stance.Stand1, frame: 0,
                    position: pos, facingLeft: false);
            }
            DrawNameTag(sb, entry.Stat.Name, pos, absIdx == _selectedSlot);
        }
    }

    // The selected character's stat scroll: unrolls (scroll/0..3 + RollDown) over the
    // avatar, then shows the charInfo1 card filled from the character's stats.
    private void DrawStatScroll(SpriteBatch sb)
    {
        if (_selectedSlot < 0 || _selectedSlot >= Characters.Count) return;
        var slotInPage = _selectedSlot - _page * SlotCount;
        if (slotInPage < 0 || slotInPage >= SlotCount) return;

        var scrollTL = SlotScreen(slotInPage) + _scrollOffset;
        var frame = Math.Clamp((int)(_scrollAnimT * _scrollFrames.Length), 0, _scrollFrames.Length - 1);
        _scrollFrames[frame]?.Draw(sb, scrollTL);

        if (frame < _scrollFrames.Length - 1) return; // still unrolling

        var entry = Characters[_selectedSlot];
        var hasRank = entry.Rank is { WorldRank: not 0 };
        var board = (hasRank ? _charInfo : _charInfoNoRank) ?? _charInfo;
        var cardTL = scrollTL + _cardOffset;
        board?.Draw(sb, cardTL + (board?.Origin ?? Vector2.Zero));
        DrawCardStats(sb, cardTL, entry, hasRank);
    }

    // Values on the charInfo board (labels are baked into the art). Offsets are the exact canvas
    // coords from CUICharDetail::Draw; font = the basic Arial-12 black UI font.
    private void DrawCardStats(SpriteBatch sb, Vector2 board, CharacterEntry entry, bool hasRank)
    {
        var font = Game.BasicFont ?? Game.Font;
        if (font is null) return;
        var s = entry.Stat;
        var ink = Color.Black;
        font.Draw(sb, JobName(s.Job), board + new Vector2(46, 1), ink);
        font.Draw(sb, s.Level.ToString(), board + new Vector2(46, 19), ink);
        font.Draw(sb, s.Pop.ToString(), board + new Vector2(136, 19), ink);
        font.Draw(sb, s.Str.ToString(), board + new Vector2(46, 37), ink);
        font.Draw(sb, s.Int.ToString(), board + new Vector2(136, 37), ink);
        font.Draw(sb, s.Dex.ToString(), board + new Vector2(46, 55), ink);
        font.Draw(sb, s.Luk.ToString(), board + new Vector2(136, 55), ink);

        if (hasRank && entry.Rank is { } r)
        {
            DrawRank(sb, board, 99, r.WorldRank, r.WorldRankMove);   // WORLD RANKING band
            DrawRank(sb, board, 135, r.JobRank, r.JobRankMove);      // overall/job RANKING band
        }
    }

    // One ranking row (IDB CUICharDetail::Draw): rank number + move-gap, both right-aligned, then
    // the up/down/same arrow. move > 0 = climbed.
    private void DrawRank(SpriteBatch sb, Vector2 board, int y, int rank, int move)
    {
        var font = Game.BasicFont ?? Game.Font;
        if (font is null) return;
        var ink = Color.Black;
        var rankStr = rank.ToString();
        var gapStr = Math.Abs(move).ToString();
        font.Draw(sb, rankStr, board + new Vector2(115 - font.Measure(rankStr).X, y), ink);
        font.Draw(sb, gapStr, board + new Vector2(165 - font.Measure(gapStr).X, y), ink);
        var arrow = move > 0 ? _rankUp : move < 0 ? _rankDown : _rankSame;
        arrow?.Draw(sb, board + new Vector2(170, y + 6));
    }

    // Name plate under the avatar feet (a fitted dark plate for legibility over the map), name
    // centered in the basic font. Level is NOT shown here — it's on the stat scroll.
    private void DrawNameTag(SpriteBatch sb, string name, Vector2 foot, bool selected)
    {
        var font = Game.BasicFont ?? Game.Font;
        if (font is null) return;
        var textW = (int)font.Measure(name).X;
        var tagW = Math.Max(58, textW + 16);
        var x = (int)(foot.X - tagW / 2f);
        var y = (int)(foot.Y + 4);
        sb.Draw(Game.WhitePixel, new Rectangle(x, y, tagW, font.LineHeight + 4),
            new Color(0, 0, 0, selected ? 205 : 150));
        font.Draw(sb, name, new Vector2(foot.X - textW / 2f, y + 2), Color.White);
    }

    private Vector2 MapToScreen(Vector2 map)
    {
        var pp = GraphicsDevice.PresentationParameters;
        return _scene?.WorldToScreen(map, pp.BackBufferWidth, pp.BackBufferHeight)
            ?? new Vector2(pp.BackBufferWidth / 2f, pp.BackBufferHeight / 2f);
    }

    private Vector2 SlotScreen(int i) => MapToScreen(SlotBaseMap + SlotStepMap * i + _slotNudge);

    private void ApplyLayout()
    {
        if (_btSelect != null) _btSelect.Position = MapToScreen(BtSelectMap);
        if (_btNew != null) _btNew.Position = MapToScreen(BtNewMap);
        if (_btDelete != null) _btDelete.Position = MapToScreen(BtDeleteMap);
        if (_btBack != null) _btBack.Position = _btBackPos;   // screen-space, on top of frame
    }

    public override void OnMouseButton(int x, int y, bool down, MouseButton button)
    {
        if (button != MouseButton.Left) return;

        if (_softKey?.IsVisible == true) { _softKey.HandleMouseButton(x, y, down); return; }
        if (_sysNotice?.IsVisible == true) { _sysNotice.HandleMouseButton(x, y, down); return; }
        if (_notice?.IsVisible == true) { _notice.HandleMouseButton(x, y, down); return; }

        if (down)
        {
            // Page arrows.
            if (MaxPage > 0)
            {
                if (_page > 0 && HitSprite(_pageL, PageLMap, x, y)) { OnPageL(); return; }
                if (_page < MaxPage && HitSprite(_pageR, PageRMap, x, y)) { OnPageR(); return; }
            }

            // Character slots — hit a box around each avatar standing on the stage.
            for (var i = 0; i < SlotCount; i++)
            {
                var pos = SlotScreen(i);
                var rect = new Rectangle((int)pos.X - 45, (int)pos.Y - 110, 90, 130);
                if (rect.Contains(x, y))
                {
                    var abs = _page * SlotCount + i;
                    var now = DateTime.UtcNow;
                    var isDouble = abs == _lastClickSlot && (now - _lastClickTime).TotalMilliseconds < 400;
                    _lastClickSlot = abs;
                    _lastClickTime = now;
                    SelectSlot(abs);
                    if (isDouble) OnSelectClicked();   // double-click = Start
                    return;
                }
            }
        }

        foreach (var b in _allButtons)
        {
            if (b.HandleMouseButton(x, y, down)) return;
        }
    }

    private bool HitSprite(WzSprite? sprite, Vector2 map, int x, int y)
    {
        if (sprite is null) return false;
        var pos = MapToScreen(map);
        var rect = new Rectangle(
            (int)(pos.X - sprite.Origin.X), (int)(pos.Y - sprite.Origin.Y),
            sprite.Width, sprite.Height);
        return rect.Contains(x, y);
    }

    public override void OnKeyPress(Keys key)
    {
        if (_softKey?.IsVisible == true) { _softKey.OnKeyPress(key); return; }
        if (_sysNotice?.IsVisible == true) { _sysNotice.OnKeyPress(key); return; }
        if (_notice?.OnKeyPress(key) == true) return;

        switch (key)
        {
            case Keys.Back:
            case Keys.Escape:   // both go back to world select (authentic ESC → GotoTitle/back)
                GoBack();
                break;
            case Keys.Enter:
                if (_selectedSlot >= 0) OnSelectClicked();
                break;
            case Keys.Left:
                if (_selectedSlot > 0) SelectSlot(_selectedSlot - 1);
                break;
            case Keys.Right:
                if (_selectedSlot < Characters.Count - 1) SelectSlot(_selectedSlot + 1);
                break;
        }
    }

    public override void OnTextInput(char character)
    {
        if (_softKey?.IsVisible == true) _softKey.OnTextInput(character);
    }

    private void SelectSlot(int absIdx)
    {
        if (absIdx < 0 || absIdx >= Characters.Count)
        {
            return;
        }
        _selectedSlot = absIdx;
        _page = absIdx / SlotCount;
        _scrollAnimT = 0f;                 // re-roll the stat scroll
        Game.AudioPlayer.PlayEffect(_rollDown);
        if (_btSelect != null) _btSelect.Enabled = true;
        if (_btDelete != null) _btDelete.Enabled = true;
        PlayClick();
        _logger.LogInformation("CharSelect: slot {Slot} selected", absIdx);
    }

    private void OnSelectClicked()
    {
        if (_selectedSlot < 0 || _selectedSlot >= Characters.Count)
        {
            _notice?.Show("That slot has no character yet.", LoginNoticeOverlay.NoticeType.Ok);
            return;
        }
        var entry = Characters[_selectedSlot];
        // The login server's bLoginOpt (decoded onto the Account at CheckPassword / SelectWorld)
        // selects the secondary-password (PIC) path, exactly as the v95 client branches on it:
        //   2 NO_SECONDARY — secondary passwords disabled; SelectCharacter(19) enters directly (no pad).
        //   1 CHECK        — a PIC exists; show the pad once and verify it via CheckSPWRequest(29).
        //   0 INITIALIZE   — no PIC yet; show the pad twice (set + confirm), register via EnableSPWRequest(28).
        switch (Game.Session.Account.LoginOpt)
        {
            case 2:
                SendSelectCharacter(entry);
                break;
            case 1:
                _softKey?.Show("", pic => SendCheckSpw(entry, pic));
                break;
            default:
                BeginRegisterPic(entry);
                break;
        }
    }

    private void SendSelectCharacter(CharacterEntry entry)
    {
        _logger.LogInformation("CharSelect: entering slot {Slot} charId={Cid} name={Name}",
            _selectedSlot, entry.Stat.CharacterId, entry.Stat.Name);
        // SelectCharacter(19): int charId, string macAddress, string macAddressWithHddSerial.
        var p = OutPacket.Of(InHeader.SelectCharacter);
        p.WriteInt(entry.Stat.CharacterId);
        p.WriteString(MachineIdProvider.GetFakeMacAddress());
        p.WriteString(MachineIdProvider.GetFakeMacAddressWithHddSerial());
        Game.Session.Send(p);
    }

    /// <summary>LoginOpt CHECK: verify an existing PIC. CheckSPWRequest(29) wire:
    /// <c>string sSPW, int charId, string mac, string macHdd</c>. The server migrates on success
    /// (SelectCharacterResult) or returns CheckSPWResult on a wrong PIC (-> <see cref="OnCheckSpwFailed"/>).</summary>
    private void SendCheckSpw(CharacterEntry entry, string pic)
    {
        _logger.LogInformation("CharSelect: CheckSPW verify charId={Cid}", entry.Stat.CharacterId);
        var p = OutPacket.Of(InHeader.CheckSPWRequest);
        p.WriteString(pic);
        p.WriteInt(entry.Stat.CharacterId);
        p.WriteString(MachineIdProvider.GetFakeMacAddress());
        p.WriteString(MachineIdProvider.GetFakeMacAddressWithHddSerial());
        Game.Session.Send(p);
    }

    /// <summary>LoginOpt INITIALIZE: register a brand-new PIC. EnableSPWRequest(28) wire:
    /// <c>byte(1), int charId, string mac, string macHdd, string sSPW</c>. The server saves the PIC
    /// then migrates exactly like SelectCharacter.</summary>
    private void SendEnableSpw(CharacterEntry entry, string pic)
    {
        _logger.LogInformation("CharSelect: EnableSPW register charId={Cid}", entry.Stat.CharacterId);
        var p = OutPacket.Of(InHeader.EnableSPWRequest);
        p.WriteByte(1);
        p.WriteInt(entry.Stat.CharacterId);
        p.WriteString(MachineIdProvider.GetFakeMacAddress());
        p.WriteString(MachineIdProvider.GetFakeMacAddressWithHddSerial());
        p.WriteString(pic);
        Game.Session.Send(p);
    }

    /// <summary>LoginOpt INITIALIZE flow: the account has no PIC yet. Mirrors the v95 client's
    /// CSoftKeyboardDlg::InitializeSecondaryPassword — show the "must register a PIC" notice
    /// (Login.img/Notice/text/95) once, then enter + re-enter the PIC and register it on a match.</summary>
    private void BeginRegisterPic(CharacterEntry entry)
    {
        _sysNotice?.Show(95, () => AskNewPic(entry));
    }

    /// <summary>The enter → re-enter → compare loop. On a match, register via EnableSPWRequest (which
    /// migrates into the game); on a mismatch, show the baked "do not match" notice (Notice/text/41)
    /// and start over — matching CLoginUtilDlg::Notice(41) in the original flow.</summary>
    private void AskNewPic(CharacterEntry entry)
    {
        _softKey?.Show("", first =>
            _softKey?.Show("Please re-enter your PIC", second =>
            {
                if (first == second)
                {
                    SendEnableSpw(entry, first);
                }
                else
                {
                    _sysNotice?.Show(41, () => AskNewPic(entry));
                }
            }));
    }

    /// <summary>Server rejected the PIC on a CheckSPWRequest. Inform the player; they can press the
    /// select button again to retry (which re-opens the pad via <see cref="OnSelectClicked"/>).</summary>
    private void OnCheckSpwFailed()
    {
        _notice?.Show("Incorrect PIC. Please try again.", LoginNoticeOverlay.NoticeType.Ok);
    }

    private void OnNewClicked()
    {
        _logger.LogInformation("CharSelect: BtNew — entering race/job select");
        // Authentic v95: Create -> race select (6 jobs); gender is chosen during customise.
        Game.StageDirector.Replace(new RaceSelectStage(
            _loggerFactory.CreateLogger<RaceSelectStage>(),
            _loggerFactory, _ui, _map, _sound,
            _worldId, _channelId, _scene?.Camera ?? Vector2.Zero, _loginCameraOffset));
    }

    private void OnDeleteClicked()
    {
        if (_selectedSlot < 0 || _selectedSlot >= Characters.Count)
        {
            _notice?.Show("That slot has no character yet.", LoginNoticeOverlay.NoticeType.Ok);
            return;
        }
        var charId = Characters[_selectedSlot].Stat.CharacterId;
        // DeleteCharacter(24) always validates the secondary password server-side. Only prompt for it
        // when the account actually has a PIC set (LoginOpt CHECK); otherwise send an empty value.
        if (Game.Session.Account.LoginOpt == 1)
        {
            _softKey?.Show("", spw => Game.Session.Send(LoginSender.DeleteCharacter(charId, spw)));
        }
        else
        {
            Game.Session.Send(LoginSender.DeleteCharacter(charId, string.Empty));
        }
    }

    private void OnPageL()
    {
        if (_page <= 0) return;
        _page--;
        ClearSelection();
    }

    private void OnPageR()
    {
        if (_page >= MaxPage) return;
        _page++;
        ClearSelection();
    }

    private void ClearSelection()
    {
        _selectedSlot = -1;
        if (_btSelect != null) _btSelect.Enabled = false;
        if (_btDelete != null) _btDelete.Enabled = false;
        PlayClick();
    }

    private void GoBack()
    {
        // Leaving this world's character list: tell the login server we're logging out of the world
        // (LogoutWorld(12), no reply expected); WorldSelectStage.OnEnter then re-requests the world list.
        Game.Session.Send(LoginSender.LogoutWorld());
        Game.StageDirector.Replace(new WorldSelectStage(
            _loggerFactory.CreateLogger<WorldSelectStage>(),
            _loggerFactory, _ui, _map, _sound,
            _scene?.Camera ?? Vector2.Zero, _loginCameraOffset));
    }

    private void PlayClick()
    {
        if (_sound?.GetItem("UI.img/BtMouseClick") is WzSound click)
        {
            Game.AudioPlayer.PlayEffect(click);
        }
    }

    // v95 job-id → char-select signpost group. Aran/Evan share the 20xx range; the
    // Resistance is 30xx, Cygnus 10xx, everything else (incl. beginner) is Explorer.
    private static string JobGroup(short job) => job switch
    {
        >= 3000 and < 4000 => "resistance",
        2001 or (>= 2200 and < 2300) => "evan",
        2000 or (>= 2100 and < 2200) => "aran",
        >= 1000 and < 2000 => "knight",
        _ => "adventure",
    };

    private static string JobName(short job) => job switch
    {
        0 or 1000 or 2000 or 2001 or 3000 => "Beginner",
        >= 3000 and < 4000 => "Resistance",
        2001 or (>= 2200 and < 2300) => "Evan",
        2000 or (>= 2100 and < 2200) => "Aran",
        >= 1000 and < 2000 => "Cygnus Knight",
        _ => "Explorer",
    };

    private WzSprite? LoadCanvas(string path)
    {
        try
        {
            return _ui!.GetItem(path) is WzCanvas c ? _loader!.Load(c) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CharSelectStage: missing canvas {Path}", path);
            return null;
        }
    }

    private Button? MakeButton(string path, Action onClick)
    {
        try
        {
            if (_ui!.GetItem(path) is not WzProperty root) return null;
            var b = new Button(_loader!, root) { OnClick = onClick };
            _allButtons.Add(b);
            return b;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CharSelectStage: missing button {Path}", path);
            return null;
        }
    }

    private void RegisterDebugItems()
    {
        var reg = Game.DebugRegistry;
        reg.Register(new DebugItem(DebugCat, "Camera offset (SP +)", () => _cameraOffset, v => _cameraOffset = v) { Draggable = false });
        reg.Register(new DebugItem(DebugCat, "Slot nudge (map)", () => _slotNudge, v => _slotNudge = v) { Draggable = false });
        reg.Register(new DebugItem(DebugCat, "Step label pos", () => _stepIndicatorPos, v => _stepIndicatorPos = v));
        reg.Register(new DebugItem(DebugCat, "CH label pos", () => _chLabelPos, v => _chLabelPos = v));
        reg.Register(new DebugItem(DebugCat, "Stat scroll offset", () => _scrollOffset, v => _scrollOffset = v));
        reg.Register(new DebugItem(DebugCat, "Stat card offset", () => _cardOffset, v => _cardOffset = v));
    }

    private void UnregisterDebugItems()
    {
        var reg = Game.DebugRegistry;
        foreach (var name in new[] { "Camera offset (SP +)", "Slot nudge (map)", "Step label pos", "CH label pos", "Stat scroll offset", "Stat card offset" })
            reg.Unregister(DebugCat, name);
    }

    private static float SmoothStep(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
