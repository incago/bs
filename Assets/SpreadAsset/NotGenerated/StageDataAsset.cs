using UnityEngine;

[CreateAssetMenu(menuName = "Legacy/stage_data_asset")]
public sealed class StageDataAsset : ScriptableObject
{
    [System.Serializable]
    public sealed class RoomData
    {
        [SerializeField] private int _id;
    }

    [System.Serializable]
    public sealed class StageData
    {
        [SerializeField] private int _id;
        [SerializeField] private string _firstName;
        [SerializeField] private string _lastName;
        [SerializeField] private float weight;
        [SerializeField] private RoomData[] _roomDatas = new RoomData[0]; // 중첩된 배열 테스트용

        public int Id => _id;
        public string FirstName => _firstName;
        public string lirstName => _lastName;
        public float Weight => weight;
        public RoomData[] RoomDatas => _roomDatas;

    }


    [SerializeField] private StageData[] _stageDatas = new StageData[0];
    public StageData[] StageDatas => _stageDatas;
}
