using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RP.CodeWalker;

/// <summary>
/// A <see cref="CSharpSyntaxRewriter"/> that stamps a banner comment onto the top of a file — the
/// modern, idempotent replacement for the old CodeRush <c>file.AddHeader(...)</c> call.
/// </summary>
/// <remarks>
/// The header is inserted as leading trivia on the very first token of the compilation unit, so it
/// survives ahead of any <c>using</c> directives or namespace. The rewrite is idempotent: if the
/// marker line is already present the tree is returned untouched, so the tool can be run repeatedly.
/// </remarks>
public sealed class HeaderRewriter : CSharpSyntaxRewriter
{
    /// <summary>The marker line used to detect (and avoid duplicating) a previously-added header.</summary>
    public const string Marker = "// <auto-header> RP.CodeWalker";

    private readonly string _message;

    /// <summary>Creates a rewriter that writes <paramref name="message"/> beneath the marker line.</summary>
    /// <param name="message">A human-readable line describing why the header was added.</param>
    public HeaderRewriter(string message) => _message = message;

    /// <summary>Returns <paramref name="source"/> with the header inserted, or unchanged if already present.</summary>
    public string Apply(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var rewritten = Visit(tree.GetRoot());
        return rewritten!.ToFullString();
    }

    public override SyntaxNode? VisitCompilationUnit(CompilationUnitSyntax node)
    {
        var first = node.GetFirstToken();

        // Idempotency: don't add a second header on a re-run.
        if (first.LeadingTrivia.ToFullString().Contains(Marker, StringComparison.Ordinal))
            return node;

        var header = SyntaxFactory.ParseLeadingTrivia(BuildHeaderText());
        var newFirst = first.WithLeadingTrivia(header.AddRange(first.LeadingTrivia));
        return node.ReplaceToken(first, newFirst);
    }

    private string BuildHeaderText() =>
        $"{Marker}{Environment.NewLine}// {_message}{Environment.NewLine}{Environment.NewLine}";
}
