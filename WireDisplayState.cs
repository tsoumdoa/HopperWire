using System;
using System.Collections.Generic;
using GH_IO.Serialization;
using Grasshopper.Kernel;

namespace HopperWire
{
    // Persist managed targets, not user preferences or geometry.
    internal sealed class WireDisplayState
    {
        private readonly Dictionary<Guid, GH_ParamWireDisplay> _entries
            = new Dictionary<Guid, GH_ParamWireDisplay>();

        public List<Guid> ManagedTargets => new List<Guid>(_entries.Keys);
        public long Revision { get; private set; }

        public bool Apply(Guid id, GH_ParamWireDisplay current, GH_ParamWireDisplay requested,
            Action<GH_ParamWireDisplay> setDisplay)
        {
            bool changed = current != requested;
            if (changed) setDisplay(requested);
            // Keep Default entries too: undo may restore an older style on a disconnected input.
            RecordApplied(id, requested);
            return changed;
        }

        public void RecordApplied(Guid id, GH_ParamWireDisplay applied)
        {
            if (_entries.TryGetValue(id, out var previous) && previous == applied) return;
            _entries[id] = applied;
            Revision++;
        }

        public void Remove(Guid id)
        {
            if (_entries.Remove(id)) Revision++;
        }

        public void Clear()
        {
            if (_entries.Count == 0) return;
            _entries.Clear();
            Revision++;
        }

        public void Write(GH_IWriter writer)
        {
            var state = writer.CreateChunk("ManagedWireDisplays");
            state.SetInt32("Version", 2);
            state.SetInt32("Count", _entries.Count);
            int index = 0;
            foreach (var entry in _entries)
            {
                var item = state.CreateChunk("Target", index++);
                item.SetGuid("Id", entry.Key);
                item.SetInt32("Applied", (int)entry.Value);
            }
        }

        public void Read(GH_IReader reader)
        {
            Clear();
            var state = reader.FindChunk("ManagedWireDisplays");
            if (state == null) return;
            // Enumerate actual chunks rather than trusting a potentially damaged Count.
            foreach (var chunk in state.Chunks)
            {
                if (chunk.Name != "Target" || !(chunk is GH_IReader item)) continue;
                Guid id = Guid.Empty;
                int applied = -1;
                if (!item.TryGetGuid("Id", ref id) || id == Guid.Empty ||
                    !item.TryGetInt32("Applied", ref applied) ||
                    applied < (int)GH_ParamWireDisplay.@default || applied > (int)GH_ParamWireDisplay.hidden)
                    continue;
                // Version 1 also stored Original; deliberately ignore that old preference.
                RecordApplied(id, (GH_ParamWireDisplay)applied);
            }
        }
    }
}
