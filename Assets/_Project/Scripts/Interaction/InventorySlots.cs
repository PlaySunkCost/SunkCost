using System;

namespace SunkCost.Interaction
{
    // Four inventory slots holding FishNet object ids (-1 empty). A plain struct so
    // one SyncVar carries the whole inventory and the editor checks can exercise it
    // without a network. FishNet generates the serializer from the public fields;
    // keep helpers as methods so nothing else is serialized.
    [Serializable]
    public struct InventorySlots
    {
        public const int Count = 4;
        public const int Empty = -1;

        public int slot0;
        public int slot1;
        public int slot2;
        public int slot3;

        public static InventorySlots None => new() { slot0 = Empty, slot1 = Empty, slot2 = Empty, slot3 = Empty };

        public int Get(int index)
        {
            switch (index)
            {
                case 0: return slot0;
                case 1: return slot1;
                case 2: return slot2;
                case 3: return slot3;
                default: return Empty;
            }
        }

        public InventorySlots With(int index, int objectId)
        {
            InventorySlots copy = this;
            switch (index)
            {
                case 0: copy.slot0 = objectId; break;
                case 1: copy.slot1 = objectId; break;
                case 2: copy.slot2 = objectId; break;
                case 3: copy.slot3 = objectId; break;
            }
            return copy;
        }

        public int IndexOf(int objectId)
        {
            if (objectId == Empty) return -1;
            for (int i = 0; i < Count; i++)
                if (Get(i) == objectId) return i;
            return -1;
        }

        public int FirstFree()
        {
            for (int i = 0; i < Count; i++)
                if (Get(i) == Empty) return i;
            return -1;
        }

        public bool IsEmpty()
        {
            return slot0 == Empty && slot1 == Empty && slot2 == Empty && slot3 == Empty;
        }

        public override string ToString() => $"[{slot0},{slot1},{slot2},{slot3}]";
    }
}
