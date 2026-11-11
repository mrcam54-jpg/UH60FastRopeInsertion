namespace UH60FastRopeInsertion
{
    internal static class LandingBriefingText
    {
        internal static string Help=>UiText.Get("Select the squad's arrival point.");
        internal static string Confirm=>UiText.Get("CONFIRM");
        internal static string Name(LandingSite site)
        {
            if(site==null)return UiText.Get("Primary landing zone");
            if(site.IsMarked)return UiText.Get(site.Name);
            if(site.Id.EndsWith(".east"))return UiText.Get(site.Sector.LocationId=="rezervbase"?"Eastern base perimeter":"Eastern industrial outskirts");
            if(site.Id.EndsWith(".west"))return UiText.Get(site.Sector.LocationId=="rezervbase"?"Western base perimeter":"Western forest edge");
            switch(site.Sector.LocationId)
            {
                case "rezervbase":return UiText.Get("Corner barracks");
                case "bigmap":return UiText.Get("Western industrial outskirts");
                case "woods":return UiText.Get("South-eastern forest edge");
                case "shoreline":return UiText.Get("Western coast");
                case "lighthouse":return UiText.Get("Southern coast");
                case "interchange":return UiText.Get("South-eastern perimeter");
                case "sandbox":case "sandbox_high":return UiText.Get("Northern district");
                case "tarkovstreets":return UiText.Get("North-eastern outskirts");
                case "icebreaker":return UiText.Get("Aft landing pad");
                default:return UiText.Get("Primary landing zone");
            }
        }

        internal static string Description(LandingSite site,bool explicitlySelected)
        {
            string heading=(site?.Label??"LZ-A")+": "+Name(site);
            string brief=site?.Sector.LocationId=="rezervbase" && site?.Label=="LZ-A"
                ?UiText.Get("Insert near the outer base perimeter, beside the corner barracks.")
                :UiText.Get("UH-60 squad insertion into the selected area.");
            return "<b>"+heading+"</b>\n\n"+brief+"\n\n"
                +UiText.Get("Fast-rope descent after hovering. Continue on foot.")
                +(explicitlySelected?"":"\n\n"+UiText.Get("The crew will head here unless another landing zone is selected."));
        }
    }
}
