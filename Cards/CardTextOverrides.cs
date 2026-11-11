namespace CPT50DTools.Cards;

/// <summary>The text printed on a card.</summary>
public readonly record struct CardText(string Illustrator, string Title, string Serial);

/// <summary>
/// User edits to a card's printed text. Cards without an edit use the defaults from
/// genesis_card.xml. Edits are keyed by catalog texture name and last for the session.
/// </summary>
public static class CardTextOverrides
{
    private static readonly Dictionary<string, CardText> _overrides = new(StringComparer.OrdinalIgnoreCase);

    public static CardText Defaults(GenesisCardInfo info) => new(info.Illustrator, info.Title, info.SerialNumber);

    public static CardText Get(string cardName, GenesisCardInfo info)
    {
        lock (_overrides)
            return _overrides.TryGetValue(info.TextureName, out var t) ? t : Defaults(info);
    }

    private static string Key(string cardName) => GenesisCardCatalog.Instance.Lookup(cardName).TextureName;

    public static CardText Get(string cardName) => Get(cardName, GenesisCardCatalog.Instance.Lookup(cardName));

    public static bool HasOverride(string cardName)
    {
        lock (_overrides) return _overrides.ContainsKey(Key(cardName));
    }

    public static void Set(string cardName, CardText text)
    {
        var defaults = Defaults(GenesisCardCatalog.Instance.Lookup(cardName));
        lock (_overrides)
        {
            if (text == defaults) _overrides.Remove(Key(cardName));
            else _overrides[Key(cardName)] = text;
        }
    }

    public static void Reset(string cardName)
    {
        lock (_overrides) _overrides.Remove(Key(cardName));
    }
}
