using System;
using System.Collections.Generic;
using GH_IO.Serialization;
using Grasshopper.Kernel;

namespace HopperWire
{
    // Persist ownership, not geometry: every pass still evaluates the current layout.
    internal sealed class WireDisplayState
    {
        private readonly Dictionary<Guid, (GH_ParamWireDisplay Original, GH_ParamWireDisplay Applied)> _entries
            = new Dictionary<Guid, (GH_ParamWireDisplay, GH_ParamWireDisplay)>();

        public List<Guid> ManagedTargets => new List<Guid>(_entries.Keys);

        public GH_ParamWireDisplay Resolve(Guid id, GH_ParamWireDisplay current, GH_ParamWireDisplay requested)
        {
            var original = _entries.TryGetValue(id, out var entry) && current == entry.Applied
                ? entry.Original : current;
            var result = requested < original ? original : requested;
            if (result == original) _entries.Remove(id);
            else _entries[id] = (original, result);
            return result;
        }

        public bool TryGetOriginal(Guid id, GH_ParamWireDisplay current, out GH_ParamWireDisplay original)
        {
            original = current;
            if (!_entries.TryGetValue(id, out var entry) || current != entry.Applied) return false;
            original = entry.Original;
            return true;
        }

        public void Remove(Guid id) => _entries.Remove(id);
        public void Clear() => _entries.Clear();

        public void Write(GH_IWriter writer)
        {
            var state = writer.CreateChunk("ManagedWireDisplays");
            state.SetInt32("Count", _entries.Count);
            int index = 0;
            foreach (var entry in _entries)
            {
                var item = state.CreateChunk("Target", index++);
                item.SetGuid("Id", entry.Key);
                item.SetInt32("Original", (int)entry.Value.Original);
                item.SetInt32("Applied", (int)entry.Value.Applied);
            }
        }

        public void Read(GH_IReader reader)
        {
            Clear();
            var state = reader.FindChunk("ManagedWireDisplays");
            if (state == null) return; // Older files have no reliable ownership information.
            int count = state.GetInt32("Count");
            for (int index = 0; index < count; index++)
            {
                var item = state.FindChunk("Target", index);
                if (item == null) continue;
                var original = (GH_ParamWireDisplay)item.GetInt32("Original");
                var applied = (GH_ParamWireDisplay)item.GetInt32("Applied");
                if (original < GH_ParamWireDisplay.@default || applied > GH_ParamWireDisplay.hidden ||
                    original >= applied) continue;
                _entries[item.GetGuid("Id")] = (original, applied);
            }
        }
    }
}
