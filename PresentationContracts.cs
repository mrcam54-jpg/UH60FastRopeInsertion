using System;
using System.Linq;
using UH60FastRopeInsertion;

internal static class PresentationContracts
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (float dt in new[] { 0f, 1f/144f, 1f/60f, 1f/24f, .25f })
        foreach (float scale in new[] { 0f, .1f, .25f, .5f, 1f, 2f, 4f })
        {
            check(PresentationPolicy.Delta(dt*scale, scale, 0, true) == 0, "photo freeze stops scene");
            check(PresentationPolicy.Delta(dt*scale, scale, 0, false) == dt*scale, "native scaled scene clock");
        }
        foreach (string command in new[] { "Escape", "ToggleInventory", "ToggleGoggles" })
            check(PresentationPolicy.AllowsCommand(command), "native menu/inventory/NVG command passes");
        foreach (string command in new[] { "Shoot", "Aim", "Jump", "DropBackpack", "ReloadWeapon", "Interact" })
            check(!PresentationPolicy.AllowsCommand(command), "gameplay stays locked");
        foreach (float duration in new[] { 3.4f, 5.2f, 9f })
        {
            check(PresentationPolicy.SlideEnvelope(duration, duration) == 0, "rope audio ends at contact");
            check(PresentationPolicy.SlideEnvelope(duration-.01f, duration) > 0, "rope audio lasts through slide");
            check(PresentationPolicy.SlideEnvelope(-.1f, duration) == 0, "no audio before slide");
        }
        var audio=RopeAudioData.Read(out int rate);
        check(rate==48000 && audio.Length>rate*3 && audio.Length<rate*5, "supplied audio mono48k active region");
        check(audio.All(x=>float.IsFinite(x) && Math.Abs(x)<.7f), "quiet unclipped PCM");
        check(Math.Sqrt(audio.TakeLast(rate/10).Select(x=>(double)x*x).Average())>.002, "no trailing silence in loop");
    }
}
