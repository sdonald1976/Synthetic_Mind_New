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
Console.WriteLine("  ── the thesis: two LEARNED units, does the top go slower? ──");
Console.WriteLine();
Console.WriteLine("  On the ball, position changes every tick (~fast) and direction flips only");
Console.WriteLine("  at the walls (~30 ticks, slow). Abstraction = level 1 runs slower than");
Console.WriteLine("  level 0 while staying informative (rank stays up). Nobody tells it to.");
Console.WriteLine();

// Each level is a learned-state unit. Level 1's input is level 0's published state — it never
// sees the ball, only level 0's report of it. If the thesis holds, distance from the input alone
// makes level 1 settle on something slower-moving.
var lower = new LearnedPredictiveRule(Width, stateWidth: 4, drive: EncoderDrive.Variance);
var upper = new LearnedPredictiveRule(lower.StateWidth, stateWidth: 4, drive: EncoderDrive.Variance);
var hierarchy = new Hierarchy(lower, upper);

var ball = new BouncingBallStream(Width);
ball.Reset();

const int Window = 4_000;   // wider window: persistence needs a long look to be stable
var collapse = new[] { new CollapseMonitor(lower.StateWidth), new CollapseMonitor(upper.StateWidth) };
var timescale = new[] { new TimescaleMonitor(lower.StateWidth), new TimescaleMonitor(upper.StateWidth) };
var lateError = new float[2];

for (var t = 0; t < Ticks; t++)
{
    var ticks = hierarchy.Observe(ball.Next());
    if (t < Ticks - Window) continue;

    for (var level = 0; level < ticks.Length; level++)
    {
        lateError[level] += ticks[level].SquaredError;
        collapse[level].Record(ticks[level].State);
        timescale[level].Record(ticks[level].State);
    }
}

for (var level = 0; level < hierarchy.Depth; level++)
    Console.WriteLine(
        $"  level {level} ({(level == 0 ? "near input" : "far from input")})   " +
        $"err {lateError[level] / Window:E2}   {timescale[level].Report()}   {collapse[level].Report()}");

// "Slower" has to mean MEANINGFULLY slower, not noise. Require level 1's characteristic
// timescale to exceed level 0's by at least 25% — otherwise a 2% jitter reads as a discovery.
var t0 = timescale[0].Report().CharacteristicTicks;
var t1 = timescale[1].Report().CharacteristicTicks;
var slower = t1 > t0 * 1.25f;
var informative = !collapse[1].Report().Collapsed;
Console.WriteLine();
Console.WriteLine($"  verdict: level 1 {(slower ? "is MEANINGFULLY slower" : "runs at the same speed as")} level 0" +
                  $" ({t1:F0} vs {t0:F0} ticks), {(informative ? "still informative" : "COLLAPSED")}  →  " +
                  $"{(slower && informative ? "abstraction emerged" : "NO emergent abstraction this run")}");
Console.WriteLine();
