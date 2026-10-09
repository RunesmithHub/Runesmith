using Runesmith.LanguageServer;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("""
        Usage: runesmith-language-server [--stdio]

        Serves Runesmith's language analyzers over the Language Server Protocol on standard input and output.
        """);
    return 0;
}

var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "runesmith", "language-server");
await using var server = new LanguageServer(Console.OpenStandardInput(), Console.OpenStandardOutput(), AnalyzerCatalog.Create(), cache);
server.Start();
await server.Exited;
return 0;
