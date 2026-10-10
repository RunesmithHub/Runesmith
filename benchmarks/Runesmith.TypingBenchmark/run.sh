#!/usr/bin/env bash
# Publishes Runesmith as a release does, runs the typing benchmark in a virtual display several times and prints the median of each
# measurement over the runs. It needs no language plugin: the benchmark writes a C# file of 10,000 lines, which Runesmith highlights with its
# own grammar, and gives it semantic tokens and occurrence highlights of its own.
# Run it in the dev shell (nix develop), which has xvfb-run. Usage: run.sh [runs] [results folder]
set -euo pipefail

runs=${1:-5}
root=$(cd "$(dirname "$0")/../.." && pwd)
home=$(mktemp -d)
results=${2:-$home/results}
folder=$home/sample

dotnet publish "$root/src/Runesmith.App" -c Release -r linux-x64 --self-contained false -o "$home/app" -nologo -v q
dotnet build "$root/benchmarks/Runesmith.TypingBenchmark" -c Release -nologo -v q
mkdir -p "$home/plugins/runesmith.typing-benchmark" "$folder"
cp -r "$root/benchmarks/Runesmith.TypingBenchmark/bin/Release/net10.0/." "$home/plugins/runesmith.typing-benchmark/"
# Without the plugin hub, no network traffic makes the runs differ.
echo '{ "plugins.hub.enabled": false }' > "$home/settings.json"

for run in $(seq "$runs"); do
  echo "Run $run of $runs"
  # Each run starts as the first did: no session to restore, and no unfinished starts that would offer safe mode.
  rm -rf "$home/state"
  RUNESMITH_HOME=$home RUNESMITH_TYPING_BENCHMARK=$results RUNESMITH_TYPING_BENCHMARK_FILE=$folder/Sample.cs \
    xvfb-run -a -s "-screen 0 1440x900x24" dotnet "$home/app/runesmith.dll" --new-instance "$folder" \
    > "$home/run-$run.log" 2>&1 || { echo "Run $run failed; see $home/run-$run.log"; exit 1; }
done

cat "$results/summary.md"
echo "Results: $results"
