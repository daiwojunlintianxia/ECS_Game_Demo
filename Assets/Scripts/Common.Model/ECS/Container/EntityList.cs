using System.Collections.Generic;

namespace GamesTan.ECS {
    public unsafe class EntityList {
        private List<EntityRef> _datas = new List<EntityRef>();
        private Dictionary<uint, int> _data2SlotId = new Dictionary<uint, int>();

        public void Add(EntityRef data) {
            if (_data2SlotId.ContainsKey(data._InternalData))
                return;
            _datas.Add(data);
            _data2SlotId[data._InternalData] = _datas.Count - 1;
        }

        public bool Remove(EntityRef data) {
            if (_data2SlotId.TryGetValue(data._InternalData, out var slotId)) {
                _datas[slotId] = _datas[_datas.Count - 1];
                _data2SlotId[_datas[slotId]._InternalData] = slotId;
                _datas.RemoveAt(_datas.Count - 1);
                _data2SlotId.Remove(data._InternalData);
                return true;
            }

            return false;
        }

        public bool Has(EntityRef data) {
            return _data2SlotId.ContainsKey(data._InternalData);
        }

        public List<EntityRef> GetInternalData() => _datas;
    }
}