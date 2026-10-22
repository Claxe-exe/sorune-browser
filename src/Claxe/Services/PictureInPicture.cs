namespace Claxe.Services;

/// <summary>
/// Picture-in-picture uses the page's own HTML5 video with Chromium's PiP window, so playback,
/// position and play/pause stay in sync with the tab. Only main-frame videos are offered;
/// videos the site marks with disablePictureInPicture (or a runtime without the API) get no button.
/// </summary>
public static class PictureInPicture
{
    /// <summary>True when the page has a video that can go picture-in-picture (or already is).</summary>
    public const string DetectScript = """
        (() => {
          if (!document.pictureInPictureEnabled) return false;
          if (document.pictureInPictureElement) return true;
          return [...document.querySelectorAll("video")]
            .some(v => v.readyState > 0 && !v.disablePictureInPicture && v.offsetWidth * v.offsetHeight > 0);
        })()
        """;

    /// <summary>Opens the playing (or largest) video in PiP, or closes PiP. Returns "opened", "closed", "unsupported" or "error:…".</summary>
    public const string ToggleScript = """
        (async () => {
          try {
            if (document.pictureInPictureElement) { await document.exitPictureInPicture(); return "closed"; }
            const area = (v) => v.offsetWidth * v.offsetHeight;
            const videos = [...document.querySelectorAll("video")].filter(v => v.readyState > 0 && !v.disablePictureInPicture);
            if (!document.pictureInPictureEnabled || videos.length === 0) return "unsupported";
            const video = videos.find(v => !v.paused && area(v) > 0) || videos.sort((a, b) => area(b) - area(a))[0];
            await video.requestPictureInPicture();
            return "opened";
          } catch (e) {
            return "error:" + (e && e.name);
          }
        })()
        """;
}
