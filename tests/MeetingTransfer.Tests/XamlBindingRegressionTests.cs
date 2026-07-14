using System.Xml.Linq;

namespace MeetingTransfer.Tests;

public sealed class XamlBindingRegressionTests
{
    [Fact]
    public void InlineRunBindings_AreExplicitlyOneWay()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appDirectory = Path.Combine(repositoryRoot, "src", "MeetingTransfer.App");
        var xamlFiles = Directory.EnumerateFiles(appDirectory, "*.xaml", SearchOption.TopDirectoryOnly);

        var unsafeBindings = xamlFiles
            .SelectMany(path => XDocument.Load(path).Descendants()
                .Where(element => element.Name.LocalName == "Run")
                .SelectMany(element => element.Attributes()
                    .Where(attribute => attribute.Value.Contains("{Binding", StringComparison.Ordinal) &&
                        !attribute.Value.Contains("Mode=OneWay", StringComparison.Ordinal))
                    .Select(attribute => $"{Path.GetFileName(path)}: {attribute.Value}")))
            .ToArray();

        Assert.True(
            unsafeBindings.Length == 0,
            "Inline Run bindings must be explicitly OneWay because WPF can otherwise try to write to read-only view-model properties: " +
            string.Join("; ", unsafeBindings));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MeetingTransfer.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate MeetingTransfer.sln from the test output directory.");
    }
}
