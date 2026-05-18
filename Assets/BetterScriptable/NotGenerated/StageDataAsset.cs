using UnityEngine;

[CreateAssetMenu(menuName = "Legacy/stage_data_asset")]
public sealed class StageDataAsset : ScriptableObject
{
    [System.Serializable]
    public sealed class StageData
    {
        [SerializeField] private int _id;
        [SerializeField] private string _name;

        public int Id => _id;
        public string Name => _name;
    }

    [SerializeField] private StageData[] _stageDatas = new StageData[0];
    public StageData[] StageDatas => _stageDatas;
}
