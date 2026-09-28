namespace CinnabarSharp.Mcp.Tests;

public class FileAccessPolicyTests
{
    [Fact]
    public void Paths_inside_the_allowed_folders_resolve()
    {
        var folder = Directory.CreateTempSubdirectory("cinnabar-policy-").FullName;
        try
        {
            var policy = new FileAccessPolicy([folder]);
            var real = FileAccessPolicy.RealPath(folder);
            Assert.Equal(Path.Combine(real, "a", "b.png"), policy.Resolve("a/b.png"));
            Assert.Equal(Path.Combine(real, "c.png"), policy.Resolve(Path.Combine(folder, "x", "..", "c.png")));
            Assert.Equal(real, policy.Resolve(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Siblings_with_the_same_prefix_are_outside()
    {
        var folder = Directory.CreateTempSubdirectory("cinnabar-policy-").FullName;
        try
        {
            var policy = new FileAccessPolicy([folder]);
            Assert.Throws<ModelContextProtocol.McpException>(() => policy.Resolve(folder + "-evil/x.png"));
            Assert.Throws<ModelContextProtocol.McpException>(() => policy.Resolve("../x.png"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Tilde_is_the_home_folder()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var policy = new FileAccessPolicy([home]);
        Assert.Equal(Path.Combine(FileAccessPolicy.RealPath(home), "x.png"), policy.Resolve("~/x.png"));
    }
}
