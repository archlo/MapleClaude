using MapleClaude.App;
using MapleClaude.Character;
using MapleClaude.Domain;
using MapleClaude.Map;
using MapleClaude.Net.Handlers;
using MapleClaude.Net.Packet;
using MapleClaude.Net.Session;
using MapleClaude.Render;
using MapleClaude.UI.Game;
using MapleClaude.Wz;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.Stages;

/// <summary>
/// In-game field stage. Pushed by <see cref="CharSelectStage"/> just before
/// <see cref="MigrationCoordinator.BeginMigrateAsync"/> runs, so it's
/// already mounted when the channel server replies with <c>SetField(141)</c>.
///
/// On SetField: log the Phase 1 → Phase 2 boundary line, load the spawn
/// map by id from <c>Map.wz/Map/Map&lt;prefix&gt;/&lt;mapIdPadded&gt;.img</c>,
/// place the player avatar at the spawn portal, render the map + avatar.
/// Phase 3 adds input + physics + camera + <c>UserMove(44)</c>.
/// </summary>
public sealed class FieldStage : Stage
{
    private readonly ILogger<FieldStage> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly WzPackage? _ui;
    private readonly WzPackage? _map;
    private readonly WzPackage? _sound;
    private readonly int _characterId;

    private WzTextureLoader? _loader;
    private FieldScene? _field;
    private KeyboardState _prevKeyboard;
    private CharacterRenderer? _avatar;
    private AvatarLook? _look;
    private CharacterStat? _stat;
    private string _statusLabel = "Waiting for SetField from channel...";
    private byte _fieldKey;
    private PlayerController? _player;

    // KeyConfig owns the live bindings — created in OnEnter.
    // F12 always opens it regardless of what Jump/MoveLeft etc. are bound to.
    private KeyConfig? _keyConfig;

    public FieldStage(
        ILogger<FieldStage> logger,
        ILoggerFactory loggerFactory,
        WzPackage? ui,
        WzPackage? map,
        WzPackage? sound,
        int characterId)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _ui = ui;
        _map = map;
        _sound = sound;
        _characterId = characterId;
    }

    public override void OnEnter(MapleClaudeGame game)
    {
        base.OnEnter(game);
        _loader = new WzTextureLoader(GraphicsDevice);
        _avatar = new CharacterRenderer(
            _loggerFactory.CreateLogger<CharacterRenderer>(),
            game.CharacterWz, game.ItemWz, game.BaseWz, _loader);

        // KeyConfig — UI panel that also owns the binding map.
        // Loaded without WZ buttons (null ui pkg) so it falls back to drawn keys.
        _keyConfig = new KeyConfig(_loader, game.UiWz, game.Font);

        Game.FieldHandlers.OnSetField += OnSetField;
        _logger.LogInformation("FieldStage mounted for charId={Cid} — awaiting SetField", _characterId);
    }

    public override void OnExit()
    {
        Game.FieldHandlers.OnSetField -= OnSetField;
        _loader?.Dispose();
        _loader = null;
        base.OnExit();
    }

    private void OnSetField(SetFieldArgs args)
    {
        if (!args.IsMigrate)
        {
            return;
        }
        _stat = args.Stat;
        _look = args.Look;
        _fieldKey = args.FieldKey;
        _statusLabel = $"In field {args.Stat?.PosMap}";
        _logger.LogInformation("SetField processed (mapId={Map}, portal={Portal})",
            args.Stat?.PosMap, args.Stat?.Portal);

        if (args.Stat is null || _map is null)
        {
            return;
        }
        try
        {
            _field = new FieldScene(_loggerFactory.CreateLogger<FieldScene>(), _map, _loader!);
            _field.Load(args.Stat.PosMap);
            _player = new PlayerController(_field);
            _field.PlacePlayerAtPortal(_player, args.Stat.Portal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load field {Id}", args.Stat.PosMap);
            _statusLabel = "Failed to load map: " + ex.Message;
        }
    }

    public override void Update(GameTime gameTime)
    {
        if (_player is null || _field is null)
        {
            return;
        }
        var kb    = Keyboard.GetState();
        var kc    = _keyConfig!;

        // F12 = meta key to open KeyConfig regardless of bindings
        if (kb.IsKeyDown(Keys.F12) && !_prevKeyboard.IsKeyDown(Keys.F12))
            kc.IsVisible = !kc.IsVisible;

        // All movement driven by KeyConfig bindings — respects user rebinds
        var input = new PlayerInput
        {
            Left        = kc.IsActionDown(kb, KeyConfig.KeyAction.MoveLeft),
            Right       = kc.IsActionDown(kb, KeyConfig.KeyAction.MoveRight),
            JumpPressed = kc.IsActionDown(kb, KeyConfig.KeyAction.Jump),
        };

        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _player.Update(input, dt);
        _field.Camera.Follow(_player.Position, _field, GraphicsDevice.PresentationParameters);

        _prevKeyboard = kb;
        kc.Update(gameTime);

        // Periodic move-path send (every 100 ms or when state changed).
        if (_player.TryFlushMovePath(out var moveBlob))
        {
            SendUserMove(moveBlob);
        }
    }

    public override void Draw(GameTime gameTime, SpriteBatch sb)
    {
        var pp = GraphicsDevice.PresentationParameters;
        sb.Draw(Game.WhitePixel, pp.Bounds, new Color(8, 8, 20));
        if (_field != null)
        {
            _field.Draw(sb, Game.WhitePixel, pp.BackBufferWidth, pp.BackBufferHeight);
            if (_look != null && _player != null)
            {
                var screenPos = _player.Position - _field.Camera.Position +
                                new Vector2(pp.BackBufferWidth / 2f, pp.BackBufferHeight / 2f);
                _avatar?.Draw(sb, _look, _stat, _player.Stance, _player.Frame, screenPos, _player.FacingLeft);
            }
        }
        // KeyConfig overlay (F12 to toggle)
        _keyConfig?.Draw(sb, Game.WhitePixel);

        if (!string.IsNullOrEmpty(_statusLabel) && Game.Font is not null)
        {
            Game.Font.Draw(sb, _statusLabel, new Vector2(10, 10), Color.White);
        }
    }

    public override void OnMouseButton(int x, int y, bool down, MouseButton button)
    {
        if (button == MouseButton.Left)
            _keyConfig?.HandleMouseButton(x, y, down);
    }

    public override void OnKeyPress(Keys key)
    {
        // Let KeyConfig intercept when it's open
        if (_keyConfig?.IsVisible == true && _keyConfig.OnKeyPress(key)) return;
    }

    private void SendUserMove(byte[] movePathBlob)
    {
        if (_player is null)
        {
            return;
        }
        // UserMove(44): int 0, int 0, byte fieldKey, int 0, int 0, int crc (0),
        //   int crc32 (0), then MovePath blob.
        var p = OutPacket.Of(InHeader.UserMove);
        p.WriteInt(0);
        p.WriteInt(0);
        p.WriteByte(_fieldKey);
        p.WriteInt(0);
        p.WriteInt(0);
        p.WriteInt(0);
        p.WriteInt(0);
        p.WriteBytes(movePathBlob);
        Game.Session.Send(p);
    }
}
