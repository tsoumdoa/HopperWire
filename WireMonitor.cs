using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Undo;
using Grasshopper.Kernel.Undo.Actions;

namespace HopperWire
{
    internal sealed class WireInfo : WirePath
    {
        public IGH_Param Source { get; }
        public IGH_Param Target { get; }

        public WireInfo(IGH_Param source, IGH_Param target)
            : base(source.InstanceGuid, target.InstanceGuid,
                source.Attributes.OutputGrip, target.Attributes.InputGrip)
        {
            Source = source;
            Target = target;
        }
    }

    internal sealed class WireMonitor
    {
        private GH_Document _document;
        private double _faintThreshold;
        private double _hiddenThreshold;
        private float _spatialGridSize;
        private bool _debug;
        private bool _skipUndo;
        private readonly WireDisplayState _displayState;
        private readonly StringBuilder _debugLog = new StringBuilder();
        private long _stateRevisionBeforeProcessing;

        public bool HasStateChanges => _displayState.Revision != _stateRevisionBeforeProcessing;
        public string DebugLog => _debugLog.ToString();
        public int WireCount { get; private set; }
        public int ModifiedCount { get; private set; }

        public WireMonitor(GH_Document document, double faintThreshold, double hiddenThreshold,
            float spatialGridSize, bool debug, bool skipUndo, WireDisplayState displayState)
        {
            _document = document;
            _displayState = displayState;
            UpdateSettings(faintThreshold, hiddenThreshold, spatialGridSize, debug, skipUndo);
        }

        public void UpdateSettings(double faintThreshold, double hiddenThreshold,
            float spatialGridSize, bool debug, bool skipUndo)
        {
            _faintThreshold = faintThreshold;
            _hiddenThreshold = hiddenThreshold;
            _spatialGridSize = spatialGridSize;
            _debug = debug;
            _skipUndo = skipUndo;
            _debugLog.Clear();
        }

        public void ProcessAllWires()
        {
            if (_document == null) return;
            _stateRevisionBeforeProcessing = _displayState.Revision;
            _debugLog.Clear();
            WireCount = 0;
            ModifiedCount = 0;
            if (_debug)
                _debugLog.AppendLine($"Document: {_document.ObjectCount} objects; thresholds: " +
                    $"faint {_faintThreshold:F1}px, hidden {_hiddenThreshold:F1}px; grid {_spatialGridSize:F1}px");

            var wires = new List<WireInfo>();
            var seen = new HashSet<(Guid Source, Guid Target)>();
            foreach (var obj in _document.Objects)
            {
                if (obj is IGH_Param param) AddParamConnections(param, wires, seen);
                if (obj is IGH_Component component)
                {
                    foreach (var input in component.Params.Input) AddParamConnections(input, wires, seen);
                    foreach (var output in component.Params.Output) AddParamConnections(output, wires, seen);
                }
            }
            var layoutReasons = FindLayoutFaintReasons(wires);
            var crossingTargets = WireCrossings.FindTargetsToFaint(wires, _faintThreshold, _spatialGridSize);
            var targets = new Dictionary<Guid, (IGH_Param Target, WireDecision Mode)>();
            foreach (var wire in wires)
            {
                bool crossing = crossingTargets.Contains(wire.TargetId);
                bool layout = layoutReasons.TryGetValue(wire.TargetId, out var reason);
                var mode = WireLayoutRules.DecideDisplay(wire.Length, _faintThreshold, _hiddenThreshold,
                    crossing || layout);
                LogDecision(wire, mode, crossing, reason);
                if (targets.TryGetValue(wire.TargetId, out var previous))
                    mode = WireLayoutRules.MostRestrictive(previous.Mode, mode);
                targets[wire.TargetId] = (wire.Target, mode);
            }
            WireCount = wires.Count;

            // Grasshopper has one display per input; aggregate all incoming paths first.
            foreach (var target in targets.Values)
                ApplyTargetMode(target.Target, ToDisplay(target.Mode));

            // ManagedTargets is a snapshot: cleanup can remove deleted entries as it iterates.
            foreach (var id in _displayState.ManagedTargets)
            {
                if (targets.ContainsKey(id)) continue;
                var param = _document.FindParameter(id);
                if (param == null) _displayState.Remove(id);
                else if (param.SourceCount == 0) ApplyTargetMode(param, GH_ParamWireDisplay.@default);
            }
            if (ModifiedCount > 0 || HasStateChanges) _document.IsModified = true;
            if (_debug)
                _debugLog.AppendLine($"Processed {WireCount} connections; {crossingTargets.Count} crossing targets; " +
                    $"{layoutReasons.Count} layout targets; modified {ModifiedCount} input displays");
        }

        public void Dispose()
        {
            // Wire styles are document data and survive removal of the component.
            _document = null;
        }

        private void LogDecision(WireInfo wire, WireDecision decision, bool crossing, string layoutReason)
        {
            if (!_debug) return;
            string reason = wire.Length > _faintThreshold ? "length" :
                (crossing ? "crossing" : "") + (crossing && layoutReason != null ? ", " : "") + layoutReason;
            _debugLog.AppendLine($"{wire.Source.NickName} -> {wire.Target.NickName}: " +
                $"{wire.Length:F1}px, {wire.Horizontalness:F1} degrees = {decision}" +
                (reason.Length > 0 ? $" ({reason})" : ""));
        }

        private Dictionary<Guid, string> FindLayoutFaintReasons(List<WireInfo> wires)
        {
            var ownerByParam = new Dictionary<Guid, Guid>();
            var componentBounds = new List<(Guid Id, RectangleF Bounds)>();
            var groupsByOwner = new Dictionary<Guid, HashSet<Guid>>();
            foreach (var obj in _document.Objects)
            {
                if (obj is IGH_Component component)
                {
                    var id = component.InstanceGuid;
                    foreach (var input in component.Params.Input)
                        ownerByParam[input.InstanceGuid] = id;
                    foreach (var output in component.Params.Output)
                        ownerByParam[output.InstanceGuid] = id;
                    if (component.Attributes != null)
                        componentBounds.Add((id, component.Attributes.Bounds));
                }
                else if (obj is GH_Group group)
                {
                    foreach (var member in group.ObjectsRecursive())
                    {
                        if (member == null) continue;
                        if (!groupsByOwner.TryGetValue(member.InstanceGuid, out var memberships))
                        {
                            memberships = new HashSet<Guid>();
                            groupsByOwner.Add(member.InstanceGuid, memberships);
                        }
                        memberships.Add(group.InstanceGuid);
                    }
                }
            }

            var reasons = new Dictionary<Guid, string>();
            foreach (var wire in wires)
            {
                // These rules describe an individual path. A multi-source target has
                // only one display setting, so changing it would affect other paths.
                if (wire.Target.SourceCount != 1 || wire.Length > _faintThreshold)
                    continue;

                Guid sourceOwner = ownerByParam.TryGetValue(wire.Source.InstanceGuid, out var sourceId)
                    ? sourceId : wire.Source.InstanceGuid;
                Guid targetOwner = ownerByParam.TryGetValue(wire.Target.InstanceGuid, out var targetId)
                    ? targetId : wire.Target.InstanceGuid;
                groupsByOwner.TryGetValue(sourceOwner, out var sourceGroups);
                groupsByOwner.TryGetValue(targetOwner, out var targetGroups);

                bool groupBoundary = WireLayoutRules.CrossesGroupBoundary(sourceGroups, targetGroups);
                bool backward = WireLayoutRules.IsBackward(wire.P0, wire.P3);
                bool throughComponent = false;
                if (_debug || (!groupBoundary && !backward))
                {
                    foreach (var component in componentBounds)
                    {
                        if (component.Id == sourceOwner || component.Id == targetOwner ||
                            !component.Bounds.IntersectsWith(wire.Bounds))
                            continue;
                        if (WireLayoutRules.PassesThroughRectangle(wire.SegmentPoints, component.Bounds))
                        {
                            throughComponent = true;
                            break;
                        }
                    }
                }
                if (!groupBoundary && !backward && !throughComponent) continue;
                if (_debug)
                {
                    var labels = new List<string>();
                    if (groupBoundary) labels.Add("group boundary");
                    if (backward) labels.Add("backward flow");
                    if (throughComponent) labels.Add("through component");
                    reasons[wire.Target.InstanceGuid] = string.Join(", ", labels);
                }
                else reasons[wire.Target.InstanceGuid] = "";
            }
            return reasons;
        }

        private static void AddParamConnections(IGH_Param target, List<WireInfo> wires,
            HashSet<(Guid Source, Guid Target)> seen)
        {
            if (target?.Attributes == null) return;
            foreach (var source in target.Sources)
                if (source?.Attributes != null && seen.Add((source.InstanceGuid, target.InstanceGuid)))
                    wires.Add(new WireInfo(source, target));
        }

        private static GH_ParamWireDisplay ToDisplay(WireDecision decision)
        {
            return decision == WireDecision.Hidden ? GH_ParamWireDisplay.hidden :
                decision == WireDecision.Faint ? GH_ParamWireDisplay.faint : GH_ParamWireDisplay.@default;
        }

        private void ApplyTargetMode(IGH_Param target, GH_ParamWireDisplay mode)
        {
            if (_displayState.Apply(target.InstanceGuid, target.WireDisplay, mode,
                value => SetWireDisplay(target, value))) ModifiedCount++;
        }

        private void SetWireDisplay(IGH_Param param, GH_ParamWireDisplay newMode)
        {
            if (param == null || _document == null) return;
            if (!_skipUndo)
            {
                var record = new GH_UndoRecord("Set Wire Display");
                record.AddAction(new GH_WireDisplayAction(param));
                _document.UndoServer.PushUndoRecord(record);
            }
            param.WireDisplay = newMode;
        }
    }
}
