using System.Text;
using QuickReplyKit.Core.Text;

namespace QuickReplyKit.Tests;

public class CfHtmlTests
{
    private static int Offset(string cf, string key)
    {
        int i = cf.IndexOf(key, StringComparison.Ordinal) + key.Length;
        int j = i;
        while (j < cf.Length && char.IsDigit(cf[j])) j++;
        return int.Parse(cf[i..j]);
    }

    [Fact]
    public void Build_HasVersionAndMarkers()
    {
        string cf = CfHtml.Build("<p>hi</p>");
        Assert.StartsWith("Version:0.9", cf);
        Assert.Contains("<!--StartFragment-->", cf);
        Assert.Contains("<!--EndFragment-->", cf);
        Assert.Contains("<html><body>", cf);
    }

    [Fact]
    public void Build_FragmentOffsets_AreByteExact()
    {
        string fragment = "<p>Hello &amp; <b>world</b></p>";
        string cf = CfHtml.Build(fragment);
        byte[] bytes = Encoding.UTF8.GetBytes(cf);

        int startFragment = Offset(cf, "StartFragment:");
        int endFragment = Offset(cf, "EndFragment:");
        string extracted = Encoding.UTF8.GetString(bytes, startFragment, endFragment - startFragment);
        Assert.Equal(fragment, extracted);

        int startHtml = Offset(cf, "StartHTML:");
        Assert.Equal("<html>", Encoding.UTF8.GetString(bytes, startHtml, 6));

        int endHtml = Offset(cf, "EndHTML:");
        Assert.Equal(bytes.Length, endHtml);
    }

    [Fact]
    public void Build_MultibyteFragment_OffsetsAreByteCorrect()
    {
        string fragment = "<p>café — naïve 日本語 😀 emoji</p>";
        string cf = CfHtml.Build(fragment);
        byte[] bytes = Encoding.UTF8.GetBytes(cf);

        int startFragment = Offset(cf, "StartFragment:");
        int endFragment = Offset(cf, "EndFragment:");
        string extracted = Encoding.UTF8.GetString(bytes, startFragment, endFragment - startFragment);

        Assert.Equal(fragment, extracted);
        // Sanity: byte length exceeds char length because of multibyte content.
        Assert.True(Encoding.UTF8.GetByteCount(fragment) > fragment.Length);
    }
}
