using UnityEngine;

[CreateAssetMenu(menuName = "Legacy/stage_data_asset")]
public sealed class StageDataAsset : ScriptableObject
{
    [System.Serializable]
    public sealed class StageData
    {
        [SerializeField] private int _id;
        [SerializeField] private string _firstName;
        [SerializeField] private string _lastName;
        [SerializeField] private float weight;

        public int Id => _id;
        public string FirstName => _firstName;
        public string lirstName => _lastName;
        public float Weight => weight;
    }

    [SerializeField] private StageData[] _stageDatas = new StageData[0];
    public StageData[] StageDatas => _stageDatas;
}
