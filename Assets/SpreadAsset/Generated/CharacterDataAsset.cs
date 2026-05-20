using SpreadAsset;
using UnityEngine;

namespace SpreadAsset.Generated
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
        [SerializeField] private SpreadAsset.NotGenerated.UserEnum _userEnum;

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
        public SpreadAsset.NotGenerated.UserEnum UserEnum => _userEnum;
    }

    [System.Serializable]
    public sealed class SkillData
    {
        [SerializeField] private int _id;
        [SerializeField] private string _name;
        [SerializeField] private int _cost;
        [SerializeField] private float _cooldown;
        [SerializeField] private float _duration;

        public int Id => _id;
        public string Name => _name;
        public int Cost => _cost;
        public float Cooldown => _cooldown;
        public float Duration => _duration;
    }

    public sealed class CharacterDataAsset : SpreadAssetObject
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
