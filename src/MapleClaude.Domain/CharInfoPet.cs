namespace MapleClaude.Domain;

/// <summary>
/// One active pet as carried in the <c>CharacterInfo</c> (S→C op 61) response —
/// the per-pet block the server writes in <c>CUIUserInfo::SetMultiPetInfo</c>.
/// POD with no dependencies so both the wire decoder (<c>MapleClaude.Net</c>) and
/// the profile window (the exe UI) can share it.
/// </summary>
public sealed class CharInfoPet
{
    /// <summary>Pet template id (Item.wz <c>Pet/&lt;id&gt;.img</c>).</summary>
    public int TemplateId;
    public string Name = string.Empty;
    public byte Level;
    public short Tameness;
    public byte Repleteness;
    public short PetSkill;
    /// <summary>Pet-wear (cosmetic) item id, 0 if none.</summary>
    public int PetWear;
}
