using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis.MSBuild;
using RP.CodeWalker;

// codewalker — a headless, solution-wide C# inspector/transformer built on Roslyn.
//
//   codewalker                          inspect the bundled sample file
//   codewalker file <path.cs> [--apply] inspect (and optionally stamp a header on) one file
//   codewalker solution <path.sln>      inspect every C# document in a solution
//   codewalker solution <path.sln> --apply   …and write a header into each file
//
// The single-file modes are pure syntax work and need no SDK/MSBuild. The solution modes use
// MSBuildWorkspace, which needs MSBuild — registered up-front via MSBuildLocator.

const string HeaderMessage = "Added by RP.CodeWalker — see https://github.com/dickiepotter/Roslyn";

string command = args.Length > 0 ? args[0].ToLowerInvariant() : "sample";
bool apply = args.Contains("--apply", StringComparer.OrdinalIgnoreCase);

try
{
    switch (command)
    {
        case "sample":
            await InspectFileAsync(LocateSample(), apply: false);
            break;

        case "file":
            if (args.Length < 2)
                return Fail("Usage: codewalker file <path.cs> [--apply]");
            await InspectFileAsync(args[1], apply);
            break;

        case "solution":
            if (args.Length < 2)
                return Fail("Usage: codewalker solution <path.sln> [--apply]");
            await InspectSolutionAsync(args[1], apply);
            break;

        default:
            return Fail($"Unknown command '{command}'. Try: file | solution");
    }
}
catch (Exception ex)
{
    return Fail(ex.Message);
}

return 0;

// ---------------------------------------------------------------------------------------------

// Single file: parse, list its types, optionally stamp the header. No workspace, no MSBuild.
async Task InspectFileAsync(string path, bool apply)
{
    string source = await File.ReadAllTextAsync(path);

    Console.WriteLine($"Inspecting {path}");
    foreach (var type in TypeMemberWalker.Inspect(source))
        Console.WriteLine($"  {type.Kind,-9} {type.FullName}  ({type.MemberCount} members)");

    if (!apply)
        return;

    string updated = new HeaderRewriter(HeaderMessage).Apply(source);
    if (updated == source)
    {
        Console.WriteLine("  Header already present — left unchanged.");
        return;
    }

    await File.WriteAllTextAsync(path, updated);
    Console.WriteLine("  Header written.");
}

// Whole solution: let MSBuildWorkspace do the loading the old tool hand-rolled, then walk it.
async Task InspectSolutionAsync(string solutionPath, bool apply)
{
    // Must happen before any MSBuild type is touched.
    if (!MSBuildLocator.IsRegistered)
        MSBuildLocator.RegisterDefaults();

    using var workspace = MSBuildWorkspace.Create();
    workspace.WorkspaceFailed += (_, e) => Console.Error.WriteLine($"  ! {e.Diagnostic.Message}");

    Console.WriteLine($"Opening {solutionPath} …");
    var solution = await workspace.OpenSolutionAsync(solutionPath);

    var inspector = new SolutionInspector(HeaderMessage);
    foreach (var report in await inspector.InspectAsync(solution))
    {
        Console.WriteLine($"[{report.ProjectName}] {report.DocumentName}");
        foreach (var type in report.Types)
            Console.WriteLine($"  {type.Kind,-9} {type.FullName}  ({type.MemberCount} members)");
    }

    if (!apply)
        return;

    var stamped = await inspector.ApplyHeadersAsync(solution);
    int written = 0;
    foreach (var change in stamped.GetChanges(solution).GetProjectChanges())
    {
        foreach (var documentId in change.GetChangedDocuments())
        {
            var document = stamped.GetDocument(documentId)!;
            if (document.FilePath is null)
                continue;

            var text = await document.GetTextAsync();
            await File.WriteAllTextAsync(document.FilePath, text.ToString());
            written++;
        }
    }

    Console.WriteLine($"Headers written to {written} file(s).");
}

// The sample lives beside the repo so `codewalker` with no args always has something to chew on.
string LocateSample()
{
    // Walk up from the build output to the repo root, then into samples/.
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "samples", "Sample.cs")))
        dir = dir.Parent;

    return dir is null
        ? throw new FileNotFoundException("Could not locate samples/Sample.cs.")
        : Path.Combine(dir.FullName, "samples", "Sample.cs");
}

int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
