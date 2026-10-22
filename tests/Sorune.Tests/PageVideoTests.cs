using Claxe.Services;

namespace Sorune.Tests;

public class PageVideoTests
{
    [Fact]
    public void YouTubeAndStreamsAreLeftOut()
    {
        const string json = """
            [
              {"url": "https://cdn.example.com/media/cat-video.mp4?token=1", "label": "Cats"},
              {"url": "https://rr3---sn-abc.googlevideo.com/videoplayback?id=1", "label": "x"},
              {"url": "https://www.youtube.com/watch?v=abc", "label": "x"},
              {"url": "https://cdn.example.com/live/master.m3u8", "label": "x"},
              {"url": "https://cdn.example.com/v/8f3a9c2b1d7e.webm", "label": "Nice: clip?"}
            ]
            """;

        var videos = PageVideos.Parse(json);

        Assert.Equal(2, videos.Count);
        Assert.Equal("cat-video.mp4", videos[0].Name);
        Assert.Equal("Nice  clip.webm", videos[1].Name); // no letters in the URL name: the page's label, made file-safe
    }

    [Theory]
    [InlineData("https://m.youtube.com/watch?v=1", true)]
    [InlineData("https://youtu.be/1", true)]
    [InlineData("https://notyoutube.com/a.mp4", false)]
    [InlineData("https://example.com/a.mp4", false)]
    public void BlockedHosts(string url, bool blocked) => Assert.Equal(blocked, PageVideos.IsBlocked(url));
}
