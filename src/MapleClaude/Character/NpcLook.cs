using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.Character;

/// <summary>
/// Renders a single NPC by playing its WZ animation frames.
/// WZ path: <c>Npc.wz/{npcId:D7}.img/{state}/{frame}</c>
/// Falls back to a coloured placeholder box when assets are unavailable.
/// </summary>
public sealed class NpcLook
{
    private readonly int _npcId;
    private readonly BuiltInFont? _font;

    // Animations indexed by state name → ordered frames
    private readonly Dictionary<string, List<(WzSprite sprite, int delayMs)>> _anims = new();

    private string _state = "stand";
    private int _frame;
    private float _frameTimer;

    public string Name { get; set; } = string.Empty;

    // World-space position (feet)
    public Vector2 Position { get; set; }

    private bool _facingLeft;
    private bool _loaded;

    private const int PlaceholderW = 40;
    private const int PlaceholderH = 70;

    public NpcLook(int npcId, Vector2 position, BuiltInFont? font = null)
    {
        _npcId = npcId;
        Position = position;
        _font = font;
    }

    /// <summary>
    /// Loads frames from Npc.wz.  Safe to call with null — falls back to placeholder.
    /// </summary>
    public void Load(WzTextureLoader loader, WzPackage? npcWz)
    {
        if (npcWz is null) return;

        var strid = $"{_npcId:D7}.img";

        // GetItem on a .img path returns WzImage; its Root gives the WzProperty tree
        WzProperty? npcRoot = null;
        if (npcWz.GetItem(strid) is WzImage img)
            npcRoot = img.Root;

        if (npcRoot is null) return;

        // Read name from info if present
        if (npcRoot.Get("info") is WzProperty info
            && info.Get("name") is string npcName)
        {
            Name = npcName;
        }

        // Iterate all top-level nodes as potential animation states
        foreach (var (key, value) in npcRoot.Items)
        {
            if (value is not WzProperty stateNode) continue;
            if (key == "info") continue;

            var frames = new List<(WzSprite, int)>();
            var fi = 0;
            while (true)
            {
                var raw = stateNode.Get($"{fi}");
                if (raw is null) break;

                int delay;
                WzSprite? sprite;
                if (raw is WzCanvas directCanvas)
                {
                    delay = 150;
                    sprite = loader.Load(directCanvas);
                }
                else if (raw is WzProperty frameNode)
                {
                    delay = ReadDelay(frameNode);
                    sprite = LoadFrame(loader, frameNode);
                }
                else break;

                if (sprite != null)
                    frames.Add((sprite, delay));
                fi++;
            }

            if (frames.Count > 0)
            {
                _anims[key] = frames;
                if (!_anims.ContainsKey(_state))
                    _state = key; // use first found state as default
            }
        }

        _loaded = _anims.Count > 0;
    }

    public void Update(float dt)
    {
        if (!_anims.TryGetValue(_state, out var frames) || frames.Count == 0) return;

        var delayMs = frames[_frame].delayMs;
        if (delayMs <= 0) delayMs = 150;
        _frameTimer += dt * 1000f;
        if (_frameTimer >= delayMs)
        {
            _frameTimer -= delayMs;
            _frame = (_frame + 1) % frames.Count;
        }
    }

    public void Draw(SpriteBatch sb, Texture2D white, Vector2 screenPos)
    {
        if (!_loaded)
        {
            DrawPlaceholder(sb, white, screenPos);
        }
        else if (_anims.TryGetValue(_state, out var frames) && frames.Count > 0)
        {
            var (sprite, _) = frames[Math.Min(_frame, frames.Count - 1)];
            var flip = _facingLeft ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            sprite.Draw(sb, screenPos, flip);
        }
        else
        {
            DrawPlaceholder(sb, white, screenPos);
        }

        DrawNameTag(sb, white, screenPos);
    }

    private void DrawPlaceholder(SpriteBatch sb, Texture2D white, Vector2 screenPos)
    {
        var rect = new Rectangle(
            (int)(screenPos.X - PlaceholderW / 2f),
            (int)(screenPos.Y - PlaceholderH),
            PlaceholderW, PlaceholderH);
        sb.Draw(white, rect, new Color(80, 60, 100, 200));
        var head = new Rectangle(rect.X + 5, rect.Y - 16, 30, 16);
        sb.Draw(white, head, new Color(220, 180, 140, 200));
    }

    private void DrawNameTag(SpriteBatch sb, Texture2D white, Vector2 screenPos)
    {
        if (_font is null || string.IsNullOrEmpty(Name)) return;
        var sz = _font.Measure(Name);
        var tagPos = new Vector2(screenPos.X - sz.X / 2f, screenPos.Y - PlaceholderH - 18);
        var tagRect = new Rectangle((int)(tagPos.X - 3), (int)(tagPos.Y - 1), (int)sz.X + 6, (int)sz.Y + 2);
        sb.Draw(white, tagRect, new Color(0, 0, 0, 160));
        _font.Draw(sb, Name, tagPos, Color.White);
    }

    public void SetState(string state)
    {
        if (_anims.ContainsKey(state) && state != _state)
        {
            _state = state;
            _frame = 0;
            _frameTimer = 0;
        }
    }

    public void FaceLeft(bool left) => _facingLeft = left;

    private static WzSprite? LoadFrame(WzTextureLoader loader, WzProperty frameNode)
    {
        // NPC frames are either direct canvas or first child canvas
        foreach (var (_, v) in frameNode.Items)
        {
            if (v is WzCanvas c) return loader.Load(c);
        }
        return null;
    }

    private static int ReadDelay(WzProperty node)
    {
        return node.Get("delay") switch
        {
            int i => i,
            short s => s,
            long l => (int)l,
            _ => 150,
        };
    }
}
