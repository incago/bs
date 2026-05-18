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
        [SerializeField] private Vector3 _resourceOffset;
        [SerializeField] private float _runSpeed;
        [SerializeField] private float _actionCooldown;
        [SerializeField] private float _attackCooldown;
        [SerializeField] private float _healthPoint;
        [SerializeField] private float _attackPoint;

        public int Id => _id;
        public float Weight => _weight;
        public string ThumbnailResourceKey => _thumbnailResourceKey;
        public string SpineResourceKey => _spineResourceKey;
        public Vector3 ResourceOffset => _resourceOffset;
        public float RunSpeed => _runSpeed;
        public float ActionCooldown => _actionCooldown;
        public float AttackCooldown => _attackCooldown;
        public float HealthPoint => _healthPoint;
        public float AttackPoint => _attackPoint;
    }

    [System.Serializable]
    public sealed class SkillData
    {
        [SerializeField] private int _id;

        public int Id => _id;
    }

    public sealed class CharacterDataAsset : BetterScriptableAsset
    {
        // Paired asset creation is handled by the generated Editor factory.
        [SerializeField] private string _characterType;
        [SerializeField] private CharacterData[] _characterDatas = new CharacterData[0];
        [SerializeField] private SkillData[] _skillDatas = new SkillData[0];

        public string CharacterType => _characterType;
        public CharacterData[] CharacterDatas => _characterDatas;
        public SkillData[] SkillDatas => _skillDatas;
    }
}
