namespace Runesmith.Composition.Tests;

/// <summary>Test plugins that use each other: Greeter offers a greeting through its contracts, and the others use it in different ways.</summary>
internal static class Greeters
{
    public static TestPlugin Greeter { get; } = new("tests.greeter", "Tests.Greeter")
    {
        Contracts = "namespace Tests.Greeter; public interface IGreeter { string Greet(); }",
        Implementation = """
            using System.Composition;
            namespace Tests.Greeter;
            [Export(typeof(IGreeter))]
            public sealed class Greeter : IGreeter { public string Greet() => "Hello from the greeter"; }
            """,
    };

    /// <summary>Declares the greeter and imports its greeting through the contract.</summary>
    public static TestPlugin Welcome { get; } = new("tests.welcome", "Tests.Welcome")
    {
        Dependencies = [("tests.greeter", "^1.0.0")],
        BuiltAgainst = [Greeter],
        Implementation = """
            using System.Composition;
            using Tests.Greeter;
            namespace Tests.Welcome;
            public sealed class Welcome
            {
                private readonly IGreeter greeter;
                [ImportingConstructor] public Welcome(IGreeter greeter) => this.greeter = greeter;
                [Export("greeting")] public string Greeting => greeter.Greet();
            }
            """,
    };

    /// <summary>Is built against the greeter's contracts without declaring it.</summary>
    public static TestPlugin Stowaway { get; } = new("tests.stowaway", "Tests.Stowaway")
    {
        BuiltAgainst = [Greeter],
        Implementation = """
            using System.Composition;
            using Tests.Greeter;
            namespace Tests.Stowaway;
            public sealed class Stowaway
            {
                [ImportingConstructor] public Stowaway(IGreeter greeter) { }
                [Export("stowaway")] public string Name => "stowaway";
            }
            """,
    };

    /// <summary>Has contracts that use the greeter's contracts without declaring it.</summary>
    public static TestPlugin LeakyContracts { get; } = new("tests.leaky", "Tests.Leaky")
    {
        BuiltAgainst = [Greeter],
        Contracts = "namespace Tests.Leaky; public interface ILoud { Tests.Greeter.IGreeter Greeter { get; } }",
    };
}
