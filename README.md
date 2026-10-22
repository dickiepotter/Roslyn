# A Headless Code Walker with Roslyn

> A walk-through of building a small, headless tool that opens a whole C# solution, lists every
> type it contains, and stamps a banner comment onto each file — using the **.NET Compiler Platform
> ("Roslyn")**. The code favours clarity over cleverness so the ideas stay easy to follow.

*By Richard Potter. The source discussed here lives in this repository under `src/`.*

**Contents**

- [Introduction](#introduction)
- [The original idea, and why it died](#the-original-idea-and-why-it-died)
- [Two layers of Roslyn](#two-layers-of-roslyn)
- [Part 1 — Walking a single file (syntax only)](#part-1--walking-a-single-file-syntax-only)
- [Part 2 — Changing code without breaking it](#part-2--changing-code-without-breaking-it)
- [Part 3 — Scaling up to a whole solution](#part-3--scaling-up-to-a-whole-solution)
- [Running it](#running-it)
- [Gotchas worth knowing](#gotchas-worth-knowing)
- [Going further](#going-further)
- [Summary](#summary)
- [History](#history)

---

## Introduction

Most of us only ever meet a C# compiler as a black box: source goes in, an assembly comes out. But
a compiler spends its life building a precise, queryable *model* of your code — the names, the
types, the members, the relationships — and throws it away when it's done. **Roslyn** is Microsoft's
C# and VB compiler rebuilt as a library that hands you that model and lets you keep it, query it, and
rewrite it.

This tutorial builds a tiny command-line tool, `codewalker`, that does three things:

1. **Reads** a C# file and lists the types it declares, with a member count for each.
2. **Rewrites** a file to add a header comment — safely, and without disturbing the code beneath.
3. **Scales** both of those from one file up to every file in an entire solution.

It is deliberately small. The point is not the tool; it is to show how little code you need once you
stop parsing C# yourself and let the compiler do it for you. If you have ever reached for a regular
expression to "find all the classes" or "add a license header," this is the grown-up alternative.

## The original idea, and why it died

This repository has an origin story. Years ago I wrote a tool called *DevExpressCleaner* that wanted
to do exactly what `codewalker` does — walk a solution and transform every file. Back then there was
no public C# parser you could just reference, so it borrowed the one inside **DevExpress CodeRush**.
That meant:

- **~700 lines** of hand-rolled `.sln` and `.csproj` loaders, re-implementing what Visual Studio
  does to discover projects and references.
- A **reflection hack** to instantiate CodeRush's internal parser services, because they were never
  meant to run outside the Visual Studio process.
- A hard dependency on a **licensed, in-IDE product** pinned to **C# 3.0**.

It was clever, and it was a dead end. CodeRush was later rewritten on top of Roslyn, deleting the
entire API the tool depended on. None of it can be revived.

The happy ending is that **everything that tool fought for is now free and built-in.** The table
below is the whole migration:

| What *DevExpressCleaner* hand-rolled | The Roslyn equivalent |
| --- | --- |
| Reflection bootstrap of CodeRush's parser | *(nothing — Roslyn is headless by design)* |
| `SolutionLoader` + `ProjectLoader` + `MsBuildProjectLoader` (~700 lines) | `MSBuildWorkspace.OpenSolutionAsync(path)` |
| Walking `solution.AllProjects → AllFiles → AllTypes → Members` | `Solution.Projects → Documents → SyntaxTree` |
| `file.AddHeader(...)` then "need to save this file" | a `CSharpSyntaxRewriter` + `Document.WithText(...)` |

So this is not just a Roslyn primer — it is the *same program*, written the way it should have been
written all along.

## Two layers of Roslyn

The single most useful thing to understand before you start is that Roslyn has **two layers**, and
you should always reach for the lowest one that can answer your question.

1. **Syntax** — the shape of the text. "Is this a class? What is it called? How many members are
   between its braces?" A syntax tree is built by *parsing a single file*. It needs no project, no
   references, no other files. It is fast and it never fails to load.
2. **Semantics** — meaning resolved across the whole compilation. "What type does this expression
   evaluate to? Does this name refer to `System.String` or a local class called `String`?" Answering
   that needs every reference and every other file — i.e. a *compilation*, which means a *workspace*.

Our type-listing and header-stamping are pure **syntax** questions, so most of this tool needs
nothing but a parsed file. We only climb to the workspace layer in Part 3, when we want to find
*every* file in a solution — and even then we keep using the syntax layer to do the actual work.

## Part 1 — Walking a single file (syntax only)

A syntax tree is a tree, so the natural way to visit every node is the **visitor pattern**. Roslyn
ships the base class for you: `CSharpSyntaxWalker`. You override `Visit…` for the node kinds you care
about and ignore the rest.

Here is the core of [`TypeMemberWalker`](src/RP.CodeWalker/TypeMemberWalker.cs):

```csharp
public sealed class TypeMemberWalker : CSharpSyntaxWalker
{
    private readonly List<TypeSummary> _types = new();
    public IReadOnlyList<TypeSummary> Types => _types;

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        Record(node, "class", node.Members.Count);
        base.VisitClassDeclaration(node);   // keep walking — types can be nested
    }

    // …the same three lines for struct, interface, record and enum…
}
```

Two details earn their keep:

- **`base.VisitClassDeclaration(node)` is not optional.** It is what tells the walker to descend
  *into* the class. Forget it and you will find top-level types but miss every nested one.
- **`node.Members.Count` is the member count "for free."** No string parsing, no brace counting —
  the parser already grouped the members for us.

Getting a *fully-qualified* name (`Acme.Billing.Invoice.Status`) is just as declarative. Instead of
tracking namespaces as we descend, we ask any declaration to look *up* at its ancestors:

```csharp
foreach (var ancestor in node.Ancestors())
{
    switch (ancestor)
    {
        case BaseTypeDeclarationSyntax outerType:    // a nesting type
            parts.Push(outerType.Identifier.Text);
            break;
        case BaseNamespaceDeclarationSyntax ns:      // block OR file-scoped namespace
            parts.Push(ns.Name.ToString());
            break;
    }
}
```

Because `BaseNamespaceDeclarationSyntax` is the shared base of both the classic `namespace X { }`
and the modern file-scoped `namespace X;`, this one branch handles both styles without us thinking
about it. That is the recurring theme of Roslyn: *the model already drew the distinctions you would
otherwise have to draw by hand.*

Kicking it off is three lines — parse text, get the root, visit it:

```csharp
var walker = new TypeMemberWalker();
walker.Visit(CSharpSyntaxTree.ParseText(source).GetRoot());
return walker.Types;
```

## Part 2 — Changing code without breaking it

Listing code is read-only. Changing it raises a question that regex-based tools always get wrong:
**how do you edit code without corrupting it?**

Roslyn's answer is that syntax trees are **immutable**. You never mutate a tree; you produce a *new*
tree that shares everything unchanged with the old one. The helper for transformations is
`CSharpSyntaxRewriter` — a walker that *returns* nodes, letting you swap any node for a replacement.

Our [`HeaderRewriter`](src/RP.CodeWalker/HeaderRewriter.cs) wants to add a comment at the very top of
the file. In Roslyn, comments and whitespace are **trivia** — they hang off tokens rather than being
nodes themselves — so a header is "leading trivia on the first token of the file":

```csharp
public override SyntaxNode? VisitCompilationUnit(CompilationUnitSyntax node)
{
    var first = node.GetFirstToken();

    // Idempotency: if our marker is already there, change nothing.
    if (first.LeadingTrivia.ToFullString().Contains(Marker, StringComparison.Ordinal))
        return node;

    var header = SyntaxFactory.ParseLeadingTrivia(BuildHeaderText());
    var newFirst = first.WithLeadingTrivia(header.AddRange(first.LeadingTrivia));
    return node.ReplaceToken(first, newFirst);
}
```

Three things make this safe in a way a text-prepend never is:

- **It attaches to the first token, not line 0.** Whatever that token is — a `using`, an attribute, a
  `namespace` — the header lands cleanly above it and the existing leading trivia is preserved by
  `header.AddRange(first.LeadingTrivia)`.
- **It is idempotent.** Run the tool a hundred times and the marker is added once. That single check
  is the difference between a tool you can put in a build and one you run nervously by hand.
- **The result is guaranteed to still parse.** We produced a valid tree, not a hopeful string, so the
  output is valid C# by construction. (Our test suite proves this by re-parsing the result.)

The whole transformation, from text to text, is then:

```csharp
public string Apply(string source)
{
    var tree = CSharpSyntaxTree.ParseText(source);
    var rewritten = Visit(tree.GetRoot());
    return rewritten!.ToFullString();   // ToFullString keeps every space and comment
}
```

> **`ToString()` vs `ToFullString()`** — a classic Roslyn trap. `ToString()` gives you the *significant*
> text and drops leading/trailing trivia; `ToFullString()` round-trips the source *exactly*. When you
> are writing files back, you almost always want `ToFullString()`.

## Part 3 — Scaling up to a whole solution

One file is the unit of work; a solution is just *a lot of files*. This is the part the old tool
needed hundreds of lines for, and it is now a single call. The `MSBuildWorkspace` knows how to read
`.sln`/`.csproj` exactly as Visual Studio does:

```csharp
using var workspace = MSBuildWorkspace.Create();
var solution = await workspace.OpenSolutionAsync(solutionPath);

foreach (var project in solution.Projects)
    foreach (var document in project.Documents)
        // …reuse TypeMemberWalker on document text — the Part 1 code, unchanged…
```

Notice that [`SolutionInspector`](src/RP.CodeWalker/SolutionInspector.cs) **reuses the syntax-layer
walker** from Part 1. We climbed to the workspace layer only to *enumerate* the files; the actual
inspection is still the cheap, project-free code we already wrote and tested.

Applying the header across a solution leans on the same immutability idea, one level up: a `Solution`
is immutable too, so `document.WithText(...)` returns a *new* solution with that one document
changed. We thread those changes through and let the caller decide what to persist:

```csharp
solution = document.WithText(SourceText.From(updated)).Project.Solution;
```

Then the tool asks the new solution what actually changed and writes only those files back to disk:

```csharp
foreach (var change in stamped.GetChanges(solution).GetProjectChanges())
    foreach (var id in change.GetChangedDocuments())
        await File.WriteAllTextAsync(doc.FilePath, (await doc.GetTextAsync()).ToString());
```

`GetChanges` is the quiet hero here: we never track "dirty" files ourselves, we just diff the new
solution against the old one and trust Roslyn's bookkeeping.

## Running it

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download) (or newer).

```bash
# Inspect the bundled sample file (no arguments needed):
dotnet run --project src/RP.CodeWalker.Tool

# Inspect any single file, then stamp a header onto it:
dotnet run --project src/RP.CodeWalker.Tool -- file path/to/File.cs
dotnet run --project src/RP.CodeWalker.Tool -- file path/to/File.cs --apply

# Inspect — or header-stamp — an entire solution:
dotnet run --project src/RP.CodeWalker.Tool -- solution path/to/App.sln
dotnet run --project src/RP.CodeWalker.Tool -- solution path/to/App.sln --apply
```

The single-file modes need nothing but the SDK. The `solution` modes use `MSBuildWorkspace`, which
locates a real MSBuild via `MSBuildLocator.RegisterDefaults()` at start-up.

Inspecting the sample prints:

```
Inspecting samples/Sample.cs
  class     Acme.Billing.Invoice  (4 members)
  enum      Acme.Billing.Invoice.Status  (3 members)
  interface Acme.Billing.IInvoiceStore  (2 members)
  record    Acme.Billing.Money  (0 members)
```

### Tests

```bash
dotnet test
```

The tests cover the parts that need no SDK — the walker and the rewriter — including the promise that
the rewrite is idempotent and that its output still parses.

## Gotchas worth knowing

These are the things that cost an afternoon if nobody tells you:

- **`MSBuildLocator.RegisterDefaults()` must run before you touch any MSBuild type** — including
  before the first use of `MSBuildWorkspace`. Register once, early, and guard it with
  `if (!MSBuildLocator.IsRegistered)`.
- **`MSBuildWorkspace` does not read the new `.slnx` solution format** (as of Roslyn 4.11). This repo
  therefore ships a classic `.sln`. If `OpenSolutionAsync` reports *"No file format header found,"* a
  `.slnx` is the usual cause.
- **`WorkspaceFailed` is a warning channel, not a crash.** A solution can open with non-fatal
  diagnostics (a missing analyzer, say) and still be perfectly walkable. Subscribe to it, print them,
  but don't abort.
- **`ToFullString()`, not `ToString()`,** whenever you intend to write source back out (see above).
- **The walker will also see generated documents** (`*.g.cs`, `AssemblyInfo.cs`). For a real tool
  you would skip those — an easy first extension (see below).

## Going further

The shape of this tool is a launch-pad. Natural next steps, roughly in order of effort:

- **Skip generated files** by filtering documents whose path sits under `obj/` or that carry a
  `<auto-generated>` header.
- **Climb to the semantic layer.** Call `project.GetCompilationAsync()` and a `SemanticModel`, and
  you can ask questions syntax can't: find every *public* type missing an XML doc comment, or every
  call to an obsolete method.
- **Become a real refactoring.** Swap `CSharpSyntaxRewriter` work for `DocumentEditor`, and you can
  rename symbols, add usings, or reorder members with formatting handled for you.
- **Ship it as a Roslyn analyzer/code-fix** so the same logic runs live in the IDE with squiggles —
  the modern endpoint of the journey CodeRush itself made.

## Summary

The lesson of this little tool is a big one: **stop treating source code as text.** The compiler has
already done the hard, error-prone work of turning your files into a precise model of types, members
and trivia — Roslyn simply lets you borrow it. A job that once needed a licensed in-IDE parser, a
reflection hack and hundreds of lines of solution-loading is now a visitor, a rewriter, and a single
`OpenSolutionAsync` call — small enough to read in one sitting and safe enough to run in a build.

## History

- **2026** — Rewritten from scratch on Roslyn / .NET 8 as this tutorial. The original
  *DevExpressCleaner* (≈2015), built on DevExpress CodeRush's `StructuralParser`, is retired: the
  API it depended on no longer exists, and everything it attempted is shown here in a fraction of the
  code.

## Licence and disclaimer

This is a personal project by Richard Potter, shared in the hope it's useful. It's free to use under the MIT licence (see [LICENSE](LICENSE)). It's provided as is: I make no promises that it works or suits your purpose, and I take no responsibility for anything that happens from using it. It isn't a product of, or supported by, any company I work for.
