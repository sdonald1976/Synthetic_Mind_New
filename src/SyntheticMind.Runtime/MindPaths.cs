namespace SyntheticMind.Runtime;

/// <summary>
/// Where the mind keeps its things. The problem this solves: relative paths resolve against the
/// launch directory, so running the tuner from its bin folder buried mind-state and downloads in
/// bin\Debug where nobody could find them. This anchors everything to the repo root (the folder with
/// SyntheticMind.sln) regardless of how the app was started, so the console and the tuner also share
/// one mind. Falls back to the current directory if no repo root is found.
/// </summary>
public static class MindPaths
{
    public static string Root { get; } = Resolve();

    public static string State => Path.Combine(Root, "mind-state");
    public static string Downloads => Path.Combine(Root, "youtube-tuner");

    private static string Resolve()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SyntheticMind.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }
}
