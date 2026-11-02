namespace Json.Services;

public static class SampleOutputExporter
{
    public static Task ExportAsync(string phaseName)
    {
        string source = Path.Combine(AppContext.BaseDirectory, "Data");
        if (!Directory.Exists(source)) return Task.CompletedTask;
        string destination = Path.Combine(Directory.GetCurrentDirectory(), "sample-output", phaseName);
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
        Console.WriteLine($"Sample output exported to: {destination}");
        return Task.CompletedTask;
    }
}
