namespace SunkCost.Interaction
{
    // An item with a state of its own, on the same prefab as its CarryableItem:
    // what it is called right now and, if it changes, what left click does.
    public interface IItemTag
    {
        string DisplayName { get; }
        ItemUseAction? UseActionOverride { get; }
    }
}
