using Microsoft.CodeAnalysis;

namespace RP.CodeWalker;

/// <summary>
/// Walks a whole loaded <see cref="Solution"/> — every project, every C# document — listing the
/// types it contains and optionally stamping each file with a <see cref="HeaderRewriter"/> header.
/// </summary>
/// <remarks>
/// This is what the old DevExpressCleaner needed ~700 lines of hand-rolled <c>.sln</c>/<c>.csproj</c>
/// loaders to achieve. Here the loading is somebody else's problem: the caller hands us an already
/// open <see cref="Solution"/> (see the tool's <c>MSBuildWorkspace</c> usage) and we just enumerate
/// it. Note this type deliberately knows nothing about MSBuild, so it stays trivially testable.
/// </remarks>
public sealed class SolutionInspector
{
    private readonly string _headerMessage;

    /// <summary>Creates an inspector that, when applying, stamps <paramref name="headerMessage"/>.</summary>
    public SolutionInspector(string headerMessage) => _headerMessage = headerMessage;

    /// <summary>
    /// Enumerates every C# document in the solution and reports the types found in each.
    /// </summary>
    public async Task<IReadOnlyList<DocumentReport>> InspectAsync(Solution solution, CancellationToken cancellationToken = default)
    {
        var reports = new List<DocumentReport>();

        foreach (var project in solution.Projects)
        {
            if (project.Language != LanguageNames.CSharp)
                continue;

            foreach (var document in project.Documents)
            {
                var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
                var types = TypeMemberWalker.Inspect(text.ToString());
                reports.Add(new DocumentReport(project.Name, document.Name, document.FilePath, types));
            }
        }

        return reports;
    }

    /// <summary>
    /// Returns a new <see cref="Solution"/> with the header stamped onto every C# document.
    /// Nothing is written to disk — Roslyn solutions are immutable, so the caller decides whether to
    /// persist the result (the tool writes each changed document's text back to its file).
    /// </summary>
    public async Task<Solution> ApplyHeadersAsync(Solution solution, CancellationToken cancellationToken = default)
    {
        var rewriter = new HeaderRewriter(_headerMessage);

        foreach (var projectId in solution.ProjectIds)
        {
            var project = solution.GetProject(projectId)!;
            if (project.Language != LanguageNames.CSharp)
                continue;

            foreach (var documentId in project.DocumentIds)
            {
                var document = solution.GetDocument(documentId)!;
                var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
                var updated = rewriter.Apply(text.ToString());

                if (!string.Equals(updated, text.ToString(), StringComparison.Ordinal))
                    solution = document.WithText(Microsoft.CodeAnalysis.Text.SourceText.From(updated)).Project.Solution;
            }
        }

        return solution;
    }
}

/// <summary>The types discovered in a single document.</summary>
/// <param name="ProjectName">The owning project's name.</param>
/// <param name="DocumentName">The document's file name.</param>
/// <param name="FilePath">The document's path on disk, if known.</param>
/// <param name="Types">The types declared in the document.</param>
public readonly record struct DocumentReport(
    string ProjectName,
    string DocumentName,
    string? FilePath,
    IReadOnlyList<TypeSummary> Types);
