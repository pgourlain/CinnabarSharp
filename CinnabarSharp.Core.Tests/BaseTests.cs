using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using CinnabarSharp.Core.Extensions;

namespace CinnabarSharp.Core.Tests;

public class BaseTests
{
    protected string SampleFilesDirectory()
    {
        var cwd = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        cwd = Path.Combine(cwd, "Data", "SampleFiles");
        return cwd;
    }
    protected FileInfo ImageSample1()
    {
        var cwd = Path.Combine(SampleFilesDirectory(), "sample1.png");
        return new FileInfo(cwd);
    }
    protected IServiceProvider CinnabarSharpService()
    {
        ServiceCollection services = new();

        services.AddLogging();
        services.AddCinnabarSharpServices();
        var sp = services.BuildServiceProvider();
        return sp;
    }
}