using BetterScriptable;
using UnityEngine;

namespace BetterScriptable.Generated
{
    [System.Serializable]
    public sealed class ItemData
    {
        [SerializeField] private int _id;
        [SerializeField] private float _weight;
        [SerializeField] private Vector2 _position;

        public int Id => _id;
        public float Weight => _weight;
        public Vector2 Position => _position;
    }

    public sealed class ItemDataAsset : BetterScriptableAsset
    {
        // Paired asset creation is handled by the generated Editor factory.
        [SerializeField] private string _itemCategory;
        [SerializeField] private ItemData[] _itemDatas = new ItemData[0];

        public string ItemCategory => _itemCategory;
        public ItemData[] ItemDatas => _itemDatas;
    }
}
