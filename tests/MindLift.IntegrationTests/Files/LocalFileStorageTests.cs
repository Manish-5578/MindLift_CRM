using System.Text;
using MindLift.Infrastructure.Files;
using Shouldly;

namespace MindLift.IntegrationTests.Files;

public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mindlift-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Saves_and_reads_back_a_file_under_the_root()
    {
        var storage = new LocalFileStorage(_root);
        var key = $"{Guid.NewGuid()}/{Guid.NewGuid()}.txt";

        await storage.SaveAsync(key, new MemoryStream(Encoding.UTF8.GetBytes("hello")));

        (await storage.ExistsAsync(key)).ShouldBeTrue();
        await using var read = await storage.OpenReadAsync(key);
        using var reader = new StreamReader(read);
        (await reader.ReadToEndAsync()).ShouldBe("hello");
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("a/../../escape.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\windows\\win.ini")]
    [InlineData("")]
    public async Task Rejects_keys_that_could_escape_the_storage_root(string key)
    {
        var storage = new LocalFileStorage(_root);

        await Should.ThrowAsync<ArgumentException>(() => storage.SaveAsync(key, new MemoryStream()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
