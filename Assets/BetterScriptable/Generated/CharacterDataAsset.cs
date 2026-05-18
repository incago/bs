using BetterScriptable;
using UnityEngine;

namespace BetterScriptable.Generated
{
    [System.Serializable]
    public sealed class CharacterData
    {
        [SerializeField] private int _id;
        [SerializeField] private float _weight;
        [SerializeField] private string _thumbnailResourceKey;
        [SerializeField] private string _spineResourceKey;

        public int Id => _id;
        public float Weight => _weight;
        public string ThumbnailResourceKey => _thumbnailResourceKey;
        public string SpineResourceKey => _spineResourceKey;
    }

    public sealed class CharacterDataAsset : BetterScriptableAsset
    {
        // Paired asset creation is handled by the generated Editor factory.
        [SerializeField] private string _characterType;
        [SerializeField] private CharacterData[] _characterDatas = new CharacterData[0];

        public string CharacterType => _characterType;
        public CharacterData[] CharacterDatas => _characterDatas;
    }
}
