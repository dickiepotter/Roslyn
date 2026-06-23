using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RP.CodeWalker;

/// <summary>
/// Walks a C# syntax tree and records every type declaration it finds, together with the type's
/// fully-qualified name and its direct member count.
/// </summary>
/// <remarks>
/// This is the syntax-only half of the tool: it needs nothing but a parsed tree, so it works on a
/// single file with no project, no references and no compilation. It is the direct descendant of
/// the old CodeRush <c>file.AllTypes</c> walk — only now the parser is free, headless and current.
/// </remarks>
public sealed class TypeMemberWalker : CSharpSyntaxWalker
{
    private readonly List<TypeSummary> _types = new();

    /// <summary>The type declarations discovered, in source order.</summary>
    public IReadOnlyList<TypeSummary> Types => _types;

    /// <summary>Parses <paramref name="source"/> and returns the types it declares.</summary>
    /// <param name="source">C# source text.</param>
    public static IReadOnlyList<TypeSummary> Inspect(string source)
    {
        var walker = new TypeMemberWalker();
        walker.Visit(CSharpSyntaxTree.ParseText(source).GetRoot());
        return walker.Types;
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        Record(node, "class", node.Members.Count);
        base.VisitClassDeclaration(node);
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        Record(node, "struct", node.Members.Count);
        base.VisitStructDeclaration(node);
    }

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        Record(node, "interface", node.Members.Count);
        base.VisitInterfaceDeclaration(node);
    }

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        Record(node, "record", node.Members.Count);
        base.VisitRecordDeclaration(node);
    }

    public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        Record(node, "enum", node.Members.Count);
        base.VisitEnumDeclaration(node);
    }

    private void Record(BaseTypeDeclarationSyntax node, string kind, int memberCount) =>
        _types.Add(new TypeSummary(FullNameOf(node), kind, memberCount));

    /// <summary>
    /// Builds a fully-qualified name by walking the ancestors of a declaration: outer types
    /// contribute their identifier, the enclosing namespace (file-scoped or block) its dotted name.
    /// </summary>
    private static string FullNameOf(BaseTypeDeclarationSyntax node)
    {
        var parts = new Stack<string>();
        parts.Push(node.Identifier.Text);

        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case BaseTypeDeclarationSyntax outerType:
                    parts.Push(outerType.Identifier.Text);
                    break;
                case BaseNamespaceDeclarationSyntax ns:
                    parts.Push(ns.Name.ToString());
                    break;
            }
        }

        return string.Join(".", parts);
    }
}
