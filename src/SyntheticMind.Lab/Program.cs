using SyntheticMind.Mind;

// The lab. Runs every rule against every stream and prints both numbers that matter.
//
// Read the table in pairs. Error alone says nothing: on `constant`, the collapsed fixture scores
// a perfect zero. Rank alone says nothing either: noise has magnificent rank and zero learning.
// A result only counts if the error beat copy-last AND the rank stayed off the floor.

const int Width = 4;
const int Ticks = 20_000;

IStream[] streams =
[
    new ConstantStream(Width),
    new NoiseStream(Width),
    new BouncingBallStream(Width),
];

Console.WriteLine();
Console.WriteLine($"  SyntheticMind lab — {Ticks:N0} ticks, {Width}-wide streams");
Console.WriteLine();

foreach (var stream in streams)
{
    Console.WriteLine($"  ── {stream.Name} {new string('─', Math.Max(0, 58 - stream.Name.Length))}");
    Console.WriteLine($"  {"rule",-16}{"early err",12}{"late err",12}{"ratio",9}   state");

    IUnitRule[] rules =
    [
        new MeanRule(Width),
        new CopyLastRule(Width),
        new CollapsedRule(Width),
        new LinearDeltaRule(Width, historyLength: 3),
    ];

    foreach (var rule in rules)
    {
        var result = Experiment.Run(rule, stream, Ticks);
        Console.WriteLine(
            $"  {result.Rule,-16}{result.EarlyError,12:E2}{result.LateError,12:E2}" +
            $"{result.LearningRatio,9:F3}   {result.Collapse}");
    }

    Console.WriteLine();
}

Console.WriteLine("  ── v1: learned state, and the collapse battle ─────────────");
Console.WriteLine("  (error is in INPUT space, comparable to the baselines above)");
Console.WriteLine();
Console.WriteLine($"  {"rule",-30}{"late err",12}{"ratio",9}   state");

var ballV1 = new BouncingBallStream(Width);
IUnitRule[] learnedRules =
[
    new CopyLastRule(Width),  // the bar, repeated here for a same-space comparison
    new LearnedPredictiveRule(Width, stateWidth: 4, drive: EncoderDrive.Variance),
    new LearnedPredictiveRule(Width, stateWidth: 4, drive: EncoderDrive.Predictability),
];

foreach (var rule in learnedRules)
{
    var result = Experiment.Run(rule, ballV1, Ticks);
    Console.WriteLine(
        $"  {result.Rule,-30}{result.LateError,12:E2}{result.LearningRatio,9:F3}   {result.Collapse}");
}

Console.WriteLine();
Console.WriteLine("  ── two-level hierarchy on bouncing-ball ───────────────────");
Console.WriteLine();

// Level 2's input is level 1's published state (12-wide: a 3-deep delay line of 4-wide input).
// Nobody tells level 2 what to represent. The question is whether being further from the input
// makes it find something slower on its own — SCAFFOLD.md §3.
var lower = new LinearDeltaRule(Width, historyLength: 3);
var upper = new LinearDeltaRule(lower.StateWidth, historyLength: 3);
var hierarchy = new Hierarchy(lower, upper);

var ball = new BouncingBallStream(Width);
ball.Reset();

var monitors = new[] { new CollapseMonitor(lower.StateWidth), new CollapseMonitor(upper.StateWidth) };
var lateError = new float[2];
const int Window = 1_000;

for (var t = 0; t < Ticks; t++)
{
    var ticks = hierarchy.Observe(ball.Next());
    if (t < Ticks - Window) continue;

    for (var level = 0; level < ticks.Length; level++)
    {
        lateError[level] += ticks[level].SquaredError;
        monitors[level].Record(ticks[level].State);
    }
}

for (var level = 0; level < hierarchy.Depth; level++)
    Console.WriteLine($"  level {level}          late err {lateError[level] / Window:E2}   {monitors[level].Report()}");

Console.WriteLine();
