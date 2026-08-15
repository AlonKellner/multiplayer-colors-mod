using Godot;
using MegaCrit.Sts2.Core.Entities.Players;

namespace MultiplayerColors;

/// <summary>Guarded access to character art that a modded character might not ship.</summary>
public static class CharacterArt
{
    /// <summary>
    /// The character's <c>character_icon_&lt;id&gt;_outline.png</c>, or null when it has none.
    /// </summary>
    /// <remarks>
    /// A convention path, so a modded character need not provide it, and asking the preload cache for a
    /// missing texture is not guaranteed to fail quietly. Callers fall back to growing the plain icon.
    /// </remarks>
    public static Texture2D? IconOutlineOrNull(Player player)
    {
        try
        {
            return player.Character.IconOutlineTexture;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
