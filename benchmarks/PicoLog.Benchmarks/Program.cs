using PicoBench;
using PicoBench.Formatters;
using PicoLog.Benchmarks;

var consoleFormatter = new ConsoleFormatter();
var markdownFormatter = new MarkdownFormatter();
var target = args.FirstOrDefault()?.ToLowerInvariant();
var sections = new List<string>();
var markdownSections = new List<string>();

if (target is null or "main")
{
    var suite = BenchmarkRunner.Run<LoggingBenchmarks>();
    sections.Add(consoleFormatter.Format(suite));
    markdownSections.Add(markdownFormatter.Format(suite));
    await WriteSuiteMarkdownAsync("main", markdownFormatter.Format(suite));
}

if (target is null or "wait")
{
    var suite = BenchmarkRunner.Run<WaitLoggingBenchmarks>();
    sections.Add(consoleFormatter.Format(suite));
    markdownSections.Add(markdownFormatter.Format(suite));
    await WriteSuiteMarkdownAsync("wait", markdownFormatter.Format(suite));
}

Console.WriteLine(string.Join(Environment.NewLine + Environment.NewLine, sections));

var markdown = string.Join(Environment.NewLine + Environment.NewLine, markdownSections);
var markdownPath = Path.Combine(AppContext.BaseDirectory, "benchmark-results.md");
await File.WriteAllTextAsync(markdownPath, markdown);
Console.WriteLine($"\nMarkdown report saved to: {markdownPath}");

static Task WriteSuiteMarkdownAsync(string suiteName, string markdown) =>
    File.WriteAllTextAsync(
        Path.Combine(AppContext.BaseDirectory, $"benchmark-results-{suiteName}.md"),
        markdown
    );
