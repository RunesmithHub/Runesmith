using Runesmith.BundledPlugins;

if (args.Length is not (3 or 4) || (args.Length == 4 && args[3] != "--require"))
{
    Console.Error.WriteLine("Usage: Runesmith.BundledPlugins <bundled-plugins.json> <root.json> <cache folder> [--require]");
    return 2;
}

using var index = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
using var packages = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
foreach (var client in (HttpClient[])[index, packages])
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Runesmith-build");

var bundler = new Bundler(index, packages, Console.Out, required: args.Length == 4);
return await bundler.RunAsync(args[0], args[1], args[2]);
