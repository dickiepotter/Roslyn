using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RP.CodeWalker;

namespace RP.CodeWalker.Tests;

[TestClass]
public class TypeMemberWalkerTests
{
    [TestMethod]
    public void Inspect_finds_a_namespaced_class_with_its_member_count()
    {
        const string source = """
            namespace Acme.Widgets;
            public class Gadget
            {
                public int Id { get; set; }
                public void Spin() { }
            }
            """;

        var types = TypeMemberWalker.Inspect(source);

        types.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new TypeSummary("Acme.Widgets.Gadget", "class", 2));
    }

    [TestMethod]
    public void Inspect_qualifies_nested_types_with_their_outer_type()
    {
        const string source = """
            namespace Acme;
            public class Outer
            {
                public class Inner { }
            }
            """;

        var types = TypeMemberWalker.Inspect(source);

        types.Select(t => t.FullName).Should().BeEquivalentTo(new[] { "Acme.Outer", "Acme.Outer.Inner" });
    }

    [TestMethod]
    public void Inspect_recognises_every_type_kind()
    {
        const string source = """
            namespace N;
            public class C { }
            public struct S { }
            public interface I { }
            public record R(int X);
            public enum E { A, B }
            """;

        var types = TypeMemberWalker.Inspect(source);

        types.Select(t => t.Kind).Should().BeEquivalentTo(new[] { "class", "struct", "interface", "record", "enum" });
    }
}
