using System;
using System.Drawing;
using HopperWire;

internal static class WireVisibilityChecks
{
    public static void Run(Action<bool, string> check)
    {
        var axisCases = new[] {
            (new PointF(10, 20), new PointF(-10, 20), true, "Opposite X signs"),
            (new PointF(10, 20), new PointF(10, -20), true, "Opposite Y signs"),
            (new PointF(10, 20), new PointF(-10, -20), true, "Both axes crossed"),
            (new PointF(-10, -20), new PointF(10, -20), true, "X crossing below the origin"),
            (new PointF(-10, -20), new PointF(-10, 20), true, "Y crossing left of the origin"),
            (new PointF(10, 20), new PointF(30, 40), false, "Positive quadrant stays local"),
            (new PointF(-10, -20), new PointF(-30, -40), false, "Negative quadrant stays local"),
            (new PointF(0, 20), new PointF(10, 20), false, "Starting on X zero is not a crossing"),
            (new PointF(0, 20), new PointF(-10, 20), false, "X zero to negative is not a crossing"),
            (new PointF(10, 0), new PointF(10, 20), false, "Starting on Y zero is not a crossing"),
            (new PointF(10, 0), new PointF(10, -20), false, "Y zero to negative is not a crossing"),
            (new PointF(0, 20), new PointF(0, -20), true, "Y crossing along X zero"),
            (new PointF(-10, 0), new PointF(10, 0), true, "X crossing along Y zero"),
            (PointF.Empty, PointF.Empty, false, "Coincident grips at the origin"),
            (new PointF(float.Epsilon, 20), new PointF(-float.Epsilon, 20), true,
                "Tiny opposite signs still cross")
        };
        foreach (var (source, target, expected, description) in axisCases)
        {
            check(WireLayoutRules.CrossesCoordinateAxes(source, target) == expected, description);
            check(WireLayoutRules.CrossesCoordinateAxes(target, source) == expected,
                description + " in the reverse direction");
        }

        var countCases = new[] {
            (0d, 3, false, WireDecision.Default, "Zero-length connection with three connections"),
            (799d, 3, false, WireDecision.Default, "Three connections stay Default when clear and nearby"),
            (800d, 3, false, WireDecision.Default, "Exactly Faint Threshold is still nearby"),
            (800d, 4, false, WireDecision.Faint, "Four nearby connections become Faint"),
            (800d, 8, false, WireDecision.Faint, "Eight nearby connections remain Faint"),
            (800d, 9, false, WireDecision.Faint, "Nine nearby connections cannot become Hidden"),
            (100d, int.MaxValue, false, WireDecision.Faint, "Any connection count keeps a nearby wire visible"),
            (100d, 1, true, WireDecision.Faint, "Existing nearby layout rules still faint"),
            (801d, 8, false, WireDecision.Faint, "Eight connections do not cross the hiding limit"),
            (801d, 9, false, WireDecision.Hidden, "Nine connections hide beyond Faint Threshold"),
            (801d, 9, true, WireDecision.Hidden, "Layout fainting cannot undo count-based hiding"),
            (1500d, 4, false, WireDecision.Faint, "Count fainting preserves the strict Hidden Threshold"),
            (1501d, 4, false, WireDecision.Hidden, "Count fainting cannot undo length-based hiding")
        };
        foreach (var (length, count, layout, expected, description) in countCases)
        {
            check(WireLayoutRules.DecideDisplay(length, 800, 1500, layout, count) == expected, description);
            check(WireLayoutRules.DecideDisplay(length, 800, 1500, layout, count, true) == WireDecision.Hidden,
                "Axis crossing takes precedence: " + description);
        }
        check(WireLayoutRules.DecideDisplay(0, 0, 0, false, 9) == WireDecision.Faint,
            "Zero thresholds still protect a coincident connection from count-based hiding");
        check(WireLayoutRules.DecideDisplay(800, 800, 800, false, 9) == WireDecision.Faint,
            "Equal distance thresholds protect the boundary length");
        check(WireLayoutRules.DecideDisplay(801, 800, 800, false, 1) == WireDecision.Hidden,
            "Equal distance thresholds hide beyond the boundary length");

        var targetId = Guid.NewGuid();
        WirePath Path(PointF start, PointF end) => new WirePath(Guid.NewGuid(), targetId, start, end);
        var targetGrip = new PointF(2100, 100);
        var nearby = Path(new PointF(2000, 100), targetGrip);
        var atThreshold = Path(new PointF(1300, 100), targetGrip);
        var longPath = Path(new PointF(100, 100), targetGrip);
        var beyondThreshold = Path(new PointF(1299, 100), targetGrip);
        var acrossX = Path(new PointF(-1, 100), targetGrip);
        var acrossY = Path(new PointF(2100, -1), targetGrip);
        var shortAcrossX = Path(new PointF(-1, 100), new PointF(1, 100));
        var shortAcrossY = Path(new PointF(100, -1), new PointF(100, 1));

        foreach (var wire in new[] { shortAcrossX, shortAcrossY })
        {
            var decision = new WireTargetDecision();
            decision.Add(wire, 800, 1500, false, 1);
            check(decision.Mode == WireDecision.Hidden, "A short axis crossing forces Hidden without other rules");
        }

        foreach (bool reverse in new[] { false, true })
        {
            WireDecision Combine(WirePath first, int firstCount, WirePath second, int secondCount)
            {
                var decision = new WireTargetDecision();
                if (reverse)
                {
                    decision.Add(second, 800, 1500, false, secondCount);
                    decision.Add(first, 800, 1500, false, firstCount);
                }
                else
                {
                    decision.Add(first, 800, 1500, false, firstCount);
                    decision.Add(second, 800, 1500, false, secondCount);
                }
                return decision.Mode;
            }

            check(Combine(nearby, 2, longPath, 2) == WireDecision.Faint,
                "A nearby incoming path caps a distant path at Faint in either order");
            check(Combine(atThreshold, 2, longPath, 2) == WireDecision.Faint,
                "Exactly Faint Threshold protects a shared input in either order");
            check(Combine(nearby, 9, beyondThreshold, 9) == WireDecision.Faint,
                "A nearby path caps count-based hiding in either order");
            check(Combine(beyondThreshold, 2, longPath, 2) == WireDecision.Hidden,
                "Without a nearby connection, length-based hiding wins");
            check(Combine(beyondThreshold, 9, beyondThreshold, 9) == WireDecision.Hidden,
                "Without a nearby connection, count-based hiding wins");
            check(Combine(nearby, 2, nearby, 2) == WireDecision.Default,
                "Multiple clear nearby connections stay Default");
            check(Combine(nearby, 4, nearby, 4) == WireDecision.Faint,
                "Multiple nearby connections beyond three become Faint");
            check(Combine(nearby, 2, acrossX, 2) == WireDecision.Hidden,
                "X axis crossing overrides nearby-input protection in either order");
            check(Combine(nearby, 2, acrossY, 2) == WireDecision.Hidden,
                "Y axis crossing overrides nearby-input protection in either order");
        }
    }
}
