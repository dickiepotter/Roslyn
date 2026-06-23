namespace RP.CodeWalker;

/// <summary>
/// A flattened description of one type declaration found in a source file: its fully-qualified
/// name, the keyword that introduced it, and how many members it directly declares.
/// </summary>
/// <param name="FullName">Namespace-qualified, nesting-aware name, e.g. <c>Acme.Billing.Invoice</c>.</param>
/// <param name="Kind">The declaration keyword: <c>class</c>, <c>struct</c>, <c>interface</c>, <c>record</c> or <c>enum</c>.</param>
/// <param name="MemberCount">The number of members declared directly on the type.</param>
public readonly record struct TypeSummary(string FullName, string Kind, int MemberCount);
