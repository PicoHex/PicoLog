using PicoBench;
using PicoBench.Formatters;
using PicoLog.Benchmarks;

// Run the attribute-based benchmark suite.
var suite = BenchmarkRunner.Run<LoggingBenchmarks>();

// Print results to the console.
var consoleFormatter = new ConsoleFormatter();
Console.WriteLine(consoleFormatter.Format(suite));

// Also write a Markdown report next to the executable.
var markdownFormatter = new MarkdownFormatter();
var markdownPath = Path.Combine(AppContext.BaseDirectory, "benchmark-results.md");
await File.WriteAllTextAsync(markdownPath, markdownFormatter.Format(suite));
Console.WriteLine($"\nMarkdown report saved to: {markdownPath}");
