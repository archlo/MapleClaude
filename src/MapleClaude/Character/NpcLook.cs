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
    public int NpcId => _npcId;
    public int ObjId { get; set; }

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

    // Ambient chatter: keys come from Npc.wz/{id}.img/info/speak ("n0"/"s0"/…); the caller resolves
    // them to text via String.wz/Npc.img and hands the lines back through SetAmbientSpeak.
    private readonly List<string> _speakKeys = new();
    private readonly List<string> _speakLines = new();
    private float _speakTimer = -1f;      // <0 = no ambient speak scheduled
    private string? _pendingSpeak;
    private static readonly Random _speakRng = new();

    /// <summary>True when Npc.wz <c>info/script</c> names a non-empty script — the server runs it on
    /// UserSelectNpc. Mirrors Kinoko <c>NpcTemplate.hasScript()</c>; used to route clicks (a scripted
    /// NPC goes through UserSelectNpc, a pure-quest NPC through the quest packet).</summary>
    public bool HasScript { get; private set; }

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

        // Read name + ambient-speak keys from info if present
        if (npcRoot.Get("info") is WzProperty info)
        {
            if (info.Get("name") is string npcName) Name = npcName;
            // info/speak/{i} = a String.wz/Npc.img key ("n0"/"s0"/…). Only NPCs with this node chatter.
            if (info.Get("speak") is WzProperty speak)
            {
                foreach (var (_, v) in speak.Items)
                    if (v is string key && key.Length > 0) _speakKeys.Add(key);
            }
            // info/script → a general NPC script (string at script/script or script/0/script). Mirrors
            // Kinoko NpcTemplate.from: such an NPC responds to UserSelectNpc.
            if (info.Get("script") is WzProperty scr)
                HasScript = scr.Get("script") is string s1 && s1.Length > 0
                         || (scr.Get("0") as WzProperty)?.Get("script") is string s2 && s2.Length > 0;
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
        UpdateSpeak(dt);

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

    // ── Ambient speech bubbles ───────────────────────────────────────────────────
    // The v95 client picks a random info/speak line on an idle timer and floats it above the head
    // (CNpc::DoActionOrChat → CNpc::OnChat → CChatBalloon::MakeBalloon). Bubble text is client-side.

    /// <summary>The info/speak keys (e.g. "n0", "s0") this NPC can say, for the caller to resolve.</summary>
    public IReadOnlyList<string> SpeakKeys => _speakKeys;

    /// <summary>Attach the resolved ambient lines. Schedules the first after a short randomised delay
    /// so a town's NPCs don't speak in unison. Empty list ⇒ the NPC stays silent.</summary>
    public void SetAmbientSpeak(IReadOnlyList<string> lines)
    {
        _speakLines.Clear();
        _speakLines.AddRange(lines);
        _speakTimer = _speakLines.Count > 0 ? 2f + (float)_speakRng.NextDouble() * 6f : -1f;
    }

    private void UpdateSpeak(float dt)
    {
        if (_speakTimer < 0f || _speakLines.Count == 0) return;
        _speakTimer -= dt;
        if (_speakTimer > 0f) return;
        _pendingSpeak = _speakLines[_speakRng.Next(_speakLines.Count)];
        _speakTimer = 5f + (float)_speakRng.NextDouble() * 4f;   // 5–9 s between lines
    }

    /// <summary>Returns a just-due ambient line once (then clears it), or null. Caller shows the bubble.</summary>
    public string? TakePendingSpeak()
    {
        var s = _pendingSpeak;
        _pendingSpeak = null;
        return s;
    }

    /// <summary>Y distance (px) from the foot anchor up to the top of the current sprite (the head),
    /// for placing a speech bubble / quest marker. Falls back to the placeholder height.</summary>
    public float HeadOffset => CurrentFrame?.Origin.Y ?? PlaceholderH;

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
        // Name plate sits just below the NPC's feet (screenPos is the foot anchor).
        var tagPos = new Vector2(screenPos.X - sz.X / 2f, screenPos.Y + 4);
        var tagRect = new Rectangle((int)(tagPos.X - 3), (int)(tagPos.Y - 1), (int)sz.X + 6, (int)sz.Y + 2);
        sb.Draw(white, tagRect, new Color(64, 64, 64, 150));     // grey, semi-transparent
        _font.Draw(sb, Name, tagPos, new Color(255, 230, 0));    // yellow
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

    /// <summary>True once at least one animation state has loaded real frames.</summary>
    public bool HasFrames => _loaded;

    /// <summary>The current frame's sprite for the active state, or null if none loaded.</summary>
    public WzSprite? CurrentFrame =>
        _anims.TryGetValue(_state, out var frames) && frames.Count > 0
            ? frames[Math.Min(_frame, frames.Count - 1)].sprite
            : null;

    /// <summary>
    /// Draws only the current animation frame at <paramref name="screenPos"/> — no name tag and
    /// no placeholder box. Used by the NPC dialog speaker slot, which supplies its own frame and
    /// name rendering. Returns false when no frame is available to draw.
    /// </summary>
    public bool DrawFrameOnly(SpriteBatch sb, Vector2 screenPos, bool flip = false)
    {
        var sprite = CurrentFrame;
        if (sprite is null) return false;
        sprite.Draw(sb, screenPos, flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None);
        return true;
    }

    public void FaceLeft(bool left) => _facingLeft = left;

    /// <summary>The on-screen rectangle the NPC actually occupies, derived from the current frame's
    /// real size + origin (flip-aware, matching <see cref="WzSprite.Draw(SpriteBatch,Vector2,SpriteEffects,Color?)"/>),
    /// so click/hover hit-testing matches the visible sprite rather than a fixed box.</summary>
    public Rectangle GetScreenBounds(Vector2 screenPos)
    {
        var s = CurrentFrame;
        if (s is null)
            return new((int)(screenPos.X - PlaceholderW / 2f), (int)(screenPos.Y - PlaceholderH),
                       PlaceholderW, PlaceholderH);
        var ox = _facingLeft ? s.Width - s.Origin.X : s.Origin.X;
        return new((int)(screenPos.X - ox), (int)(screenPos.Y - s.Origin.Y), s.Width, s.Height);
    }

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
