using SyntheticMind.Runtime;

// SyntheticMind — the console shell over the MindEngine (ARCHITECTURE §5). One executable, one
// continuous heartbeat: attend to an object, hear words, bind, remember, and — when it recognises an
// object it has a word for — SAY it, with a voice it babbled into being. Unsupervised, no labels.
//
//   dotnet run --project src/SyntheticMind -- [folder-of-videos]   # watch a folder on a loop
//   dotnet run --project src/SyntheticMind -- --live               # perceive the real room (webcam + mic)
//
// The engine lives in SyntheticMind.Runtime so the tuning window drives the very same mind.

var live = args.Contains("--live");
var world = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "temp");
var stateDir = MindPaths.State;   // repo-root/mind-state, wherever it's launched from

Console.WriteLine("\n  SyntheticMind — waking up. babbling to learn its own voice...");
var engine = new MindEngine(stateDir);
engine.Log += m => Console.WriteLine($"  {m}");
Console.CancelKeyPress += (_, e) => { e.Cancel = true; Console.WriteLine("\n  ...going to sleep. remembering."); engine.Stop(); };

if (live) engine.RunLive(); else engine.RunWorld(world);

Console.WriteLine($"\n  asleep. learned {engine.WordCount} words + {engine.ObjectCount} objects. memory in {stateDir}\\\n");
