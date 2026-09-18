namespace SunkCost.Interaction
{
    // An item with a state of its own, on the same prefab as its CarryableItem:
    // what it is called right now and, if it changes, what left click does.
    public interface IItemTag
    {
        string DisplayName { get; }
        ItemUseAction? UseActionOverride { get; }
        // A different inventory icon while tagged (an empty tank), or null for the prefab's.
        UnityEngine.Texture2D IconOverride { get; }
    }
}
