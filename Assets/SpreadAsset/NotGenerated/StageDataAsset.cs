using SpreadAsset;
using UnityEngine;

namespace SpreadAsset.Generated
{
    [System.Serializable]
    public sealed class StageData
    {
        [SerializeField] private int _id;
        [SerializeField] private string _firstName;
        [SerializeField] private string _lastName;
        [SerializeField] private float _weight;
        [SerializeField] private int[] _roomDataIds;

        public int Id => _id;
        public string FirstName => _firstName;
        public string LastName => _lastName;
        public float Weight => _weight;
        public int[] RoomDataIds => _roomDataIds;
    }

    [System.Serializable]
    public sealed class RoomData
    {
        [SerializeField] private int _id;
        [SerializeField] private string _name;

        public int Id => _id;
        public string Name => _name;
    }

    public sealed class StageDataAsset : SpreadAssetObject
    {
        // Paired asset creation is handled by the generated Editor factory.
        [SerializeField] private StageData[] _stageDatas = new StageData[0];
        [SerializeField] private RoomData[] _roomDatas = new RoomData[0];
        [System.NonSerialized] private System.Collections.Generic.Dictionary<int, StageData> _stageDatasById;
        [System.NonSerialized] private System.Collections.Generic.Dictionary<int, RoomData> _roomDatasById;

        public StageData[] StageDatas => _stageDatas;
        public RoomData[] RoomDatas => _roomDatas;

        public bool TryGetStageDataById(int key, out StageData value)
        {
            return GetStageDatasByIdLookup().TryGetValue(key, out value);
        }

        public StageData GetStageDataById(int key)
        {
            TryGetStageDataById(key, out StageData value);
            return value;
        }

        private System.Collections.Generic.Dictionary<int, StageData> GetStageDatasByIdLookup()
        {
            if (_stageDatasById != null)
            {
                return _stageDatasById;
            }

            _stageDatasById = new System.Collections.Generic.Dictionary<int, StageData>();
            if (_stageDatas == null)
            {
                return _stageDatasById;
            }

            foreach (StageData row in _stageDatas)
            {
                if (row == null)
                {
                    continue;
                }
                _stageDatasById[row.Id] = row;
            }

            return _stageDatasById;
        }

        public bool TryGetRoomDataById(int key, out RoomData value)
        {
            return GetRoomDatasByIdLookup().TryGetValue(key, out value);
        }

        public RoomData GetRoomDataById(int key)
        {
            TryGetRoomDataById(key, out RoomData value);
            return value;
        }

        private System.Collections.Generic.Dictionary<int, RoomData> GetRoomDatasByIdLookup()
        {
            if (_roomDatasById != null)
            {
                return _roomDatasById;
            }

            _roomDatasById = new System.Collections.Generic.Dictionary<int, RoomData>();
            if (_roomDatas == null)
            {
                return _roomDatasById;
            }

            foreach (RoomData row in _roomDatas)
            {
                if (row == null)
                {
                    continue;
                }
                _roomDatasById[row.Id] = row;
            }

            return _roomDatasById;
        }

        public void ClearLookupCaches()
        {
            _stageDatasById = null;
            _roomDatasById = null;
        }

        private void OnEnable()
        {
            ClearLookupCaches();
        }
    }
}
