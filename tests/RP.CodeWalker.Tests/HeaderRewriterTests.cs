using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RP.CodeWalker;

namespace RP.CodeWalker.Tests;

[TestClass]
public class HeaderRewriterTests
{
    private static readonly HeaderRewriter Rewriter = new("test header");

    [TestMethod]
    public void Apply_inserts_the_marker_above_the_first_line()
    {
        const string source = "using System;\n\nclass C { }\n";

        var result = Rewriter.Apply(source);

        result.Should().StartWith(HeaderRewriter.Marker);
        result.Should().Contain("using System;");
    }

    [TestMethod]
    public void Apply_preserves_the_original_code_below_the_header()
    {
        const string source = "namespace N;\nclass C { }\n";

        var result = Rewriter.Apply(source);

        // Everything after the inserted header must be the original text, untouched.
        result.Should().EndWith(source);
    }

    [TestMethod]
    public void Apply_is_idempotent_when_run_twice()
    {
        const string source = "class C { }\n";

        var once = Rewriter.Apply(source);
        var twice = Rewriter.Apply(once);

        twice.Should().Be(once);
    }

    [TestMethod]
    public void Apply_does_not_break_parsing_of_the_result()
    {
        const string source = "class C { }\n";

        var result = Rewriter.Apply(source);

        TypeMemberWalker.Inspect(result).Should().ContainSingle().Which.FullName.Should().Be("C");
    }
}
