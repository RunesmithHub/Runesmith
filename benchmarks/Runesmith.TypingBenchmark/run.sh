#!/usr/bin/env bash
# Publishes Runesmith as a release does, runs the typing benchmark in a virtual display several times and prints the median of each
# measurement over the runs.
# Run it in the dev shell (nix develop), which has xvfb-run. Usage: run.sh [runs] [results folder]
# With RUNESMITH_TYPING_BENCHMARK_LANGUAGE=java it types Java into a project made of the JDK's sources; java must be on the PATH.
set -euo pipefail

runs=${1:-5}
root=$(cd "$(dirname "$0")/../.." && pwd)
home=$(mktemp -d)
results=${2:-$home/results}

dotnet publish "$root/src/Runesmith.App" -c Release -r linux-x64 --self-contained false -o "$home/app" -nologo -v q
dotnet build "$root/benchmarks/Runesmith.TypingBenchmark" -c Release -nologo -v q
mkdir -p "$home/plugins/runesmith.typing-benchmark"
cp -r "$root/benchmarks/Runesmith.TypingBenchmark/bin/Release/net10.0/." "$home/plugins/runesmith.typing-benchmark/"

language=${RUNESMITH_TYPING_BENCHMARK_LANGUAGE:-csharp}
if [ "$language" = java ]; then
  # A Maven project made of the JDK's own java.util sources, typed into inside Collectors.toList().
  folder=$home/java-project
  jdk=$(dirname "$(readlink -f "$(command -v java)")")
  mkdir -p "$folder/src/main/java"
  (cd "$folder/src/main/java" && "$jdk/jar" xf "$jdk/../lib/src.zip" java.base/java/util/ && mv java.base/java . && rmdir java.base)
  printf '<project><modelVersion>4.0.0</modelVersion><groupId>bench</groupId><artifactId>bench</artifactId><version>1</version>%s</project>\n' \
    '<properties><maven.compiler.release>25</maven.compiler.release></properties>' > "$folder/pom.xml"
  file=$folder/src/main/java/java/util/stream/Collectors.java
  target=$file:$(grep -n 'toList() {' "$file" | head -1 | cut -d: -f1)
else
  folder=$root
  target=$root/src/Runesmith.Editor/TextArea.cs:202
fi

for run in $(seq "$runs"); do
  echo "Run $run of $runs"
  RUNESMITH_HOME=$home RUNESMITH_TYPING_BENCHMARK=$results RUNESMITH_TYPING_BENCHMARK_FILE=$target RUNESMITH_TYPING_BENCHMARK_LANGUAGE=$language \
    xvfb-run -a -s "-screen 0 1440x900x24" dotnet "$home/app/runesmith.dll" --new-instance "$folder" \
    > "$home/run-$run.log" 2>&1 || { echo "Run $run failed; see $home/run-$run.log"; exit 1; }
done

cat "$results/summary.md"
echo "Results: $results"
