namespace CinnabarSharp.Vector.Tests;

/// <summary>
/// Pins the rendering of every sample at 1× and 2.5× by checksum, like BlendOpsTests: the same bytes must come out on every OS.
/// A change here is intended only when the rasterizer is changed on purpose. The PNGs are written to render-output/ to look at.
/// </summary>
public class VectorRasterizerTests
{
    private static readonly Dictionary<string, string> Expected = new()
    {
        ["clip.svg@1"] = "84E28C203ED1975F9CCD7FD1A9AFB520891FD13F078C85C26FB27AC6141DBFC9",
        ["clip.svg@2.5"] = "156E2283FCFC38C37973074C9A3D02BF57E3CA1DFF5DA131D0B518F33A891C9C",
        ["gradients.svg@1"] = "B06E145FA87BC8863D0BE0BA18F2EA6B313C8CDAD33C6A1569374D9FDF6E87DD",
        ["gradients.svg@2.5"] = "517E0AB490858BECD3EFC92B2521BC453122F2F67F0FD13F5587B2989AF57AE7",
        ["image.svg@1"] = "85F3A2177AD813BD2F21C523FD7CE9312E017A1A4F0A358A0C52CF005D3BAA7A",
        ["image.svg@2.5"] = "482A00A42ECE050045420A734C3961E7CBA32BD767E428C04E8C14F7D7271AF7",
        ["logo.svg@1"] = "83188D3B0814C449AEAA82BD0E1B63F152D202899B5BDECB45925FD12CAE497B",
        ["logo.svg@2.5"] = "B332AD180A03079F18B351824BFF4802366F63E1189625421259420C6542C51B",
        ["paths.svg@1"] = "B924EE50D43C4BCA073636C048D7ABE8587A29679DB89FB803D139B93D7DC732",
        ["paths.svg@2.5"] = "C832F36453831F32A66099DE33AFB980DDD6AD696D5784199C9F9AB847BD9935",
        ["shapes.svg@1"] = "B84ADC3260B1CC33995FEE600C031B06FB513D989D220F588DF404F7D6C02563",
        ["shapes.svg@2.5"] = "F14AD5E0198A4D43540DB2195408E339B329E0B35787DDE2AB9973C606455164",
        ["strokes.svg@1"] = "E22911FB4D8D686EFBB27E8A040ED3DDE17BADC05B474EEE311440C6CEE0B740",
        ["strokes.svg@2.5"] = "C667986CF5338F83EAEBE41C47CBFF5C220DBE9107F7CA7082FA8055C12CCA0C",
        ["styles.svg@1"] = "563F326C671E69437F17AF6048E007C47E0A9D0A54DAD59908BA64441538C4FD",
        ["styles.svg@2.5"] = "052E3FEE3C84C7C7E77A8C0F5D0BE2BBF7A2E75C26C4A55000BCE6BC12C7E9E4",
        ["text.svg@1"] = "B57F39A4C9230656824A8644673865901DBD50471AD9803F241E403F52134BD1",
        ["text.svg@2.5"] = "F9348A624002188561103386DB55D6B4DEC9E6C94BF098BDBB2D61B2E0B4C43C",
        ["transforms.svg@1"] = "F21EA4FF453243D5CEDB5DD332CA703CBA3E7BBD751B858044D8177127BB2518",
        ["transforms.svg@2.5"] = "6C3ECBED31C2D7FB7D666A53AD0077CA71B3B6F8860F59314FAD0C28268D357A",
        ["units.svg@1"] = "E3BA99C7C589A3FD7E7D2E89A37899AE5ADCA90AB732C262D119623579425D74",
        ["units.svg@2.5"] = "3C6B5B1637803C5126B2AD72F5B1B2BA22BD112DC0C5262641902D0F0F08983B",
        ["unknown.svg@1"] = "63EED10BB0CFE2213A176E4DBB2C0D2743C1161ECBB4EE956043816288CF8B1B",
        ["unknown.svg@2.5"] = "247CC62CCDEB8715D197E2EC09AC4D35C3EEC7EA56C23127DBB4AF1E0CE85739",
        ["use.svg@1"] = "AE923452E6CA77793FE33F66EBD4AE18A8A4C1479A53FECFA2E4C6CA6C54ADF3",
        ["use.svg@2.5"] = "65243876B3349EEA353D5E7911B27D7997D935DDF64FDDF3ED10F00A8F225C14",
    };

    public static IEnumerable<object[]> Cases() =>
        from name in SvgTestFiles.All()
        from scale in new[] { 1.0, 2.5 }
        select new object[] { name, scale };

    public static byte[] RenderSample(string name, double scale, out int width, out int height)
    {
        var root = SvgParser.ParseFile(SvgTestFiles.PathOf(name)).Root;
        var options = new RenderOptions
        {
            ImageDecoder = new FakeImageDecoder(),
            GlyphProvider = new BoxGlyphProvider(),
            BaseFolder = SvgTestFiles.Folder,
            Background = new VColor(240, 240, 240, 255),
        };
        var (pixels, w, h) = VectorRasterizer.RenderAll(root, scale, options);
        (width, height) = (w, h);
        return pixels;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Sample_renders_to_the_pinned_checksum(string name, double scale)
    {
        var pixels = RenderSample(name, scale, out var width, out var height);
        var key = $"{name}@{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var actual = PixelAssert.Checksum(pixels);
        var path = PixelAssert.WritePng(key.Replace('@', '_'), pixels, width, height);
        File.AppendAllText(Path.Combine(PixelAssert.OutputFolder, "checksums.txt"), $"[\"{key}\"] = \"{actual}\",\n");
        Assert.True(Expected.TryGetValue(key, out var expected), $"No checksum pinned for {key}: {actual} (image: {path})");
        Assert.True(expected == actual, $"Checksum of {key} changed.\n expected {expected}\n actual   {actual}\n image: {path}");
    }
}
