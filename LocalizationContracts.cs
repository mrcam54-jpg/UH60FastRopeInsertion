using UH60FastRopeInsertion;
using System.Text.RegularExpressions;

internal static class LocalizationContracts
{
    internal static void Run(Action<bool,string> check)
    {
        foreach(string locale in new[]{"ru","ru-RU","RU_ru","en","en-US","de",null})
        {
            UiText.LanguageSource=()=>locale;
            bool russian=locale!=null && locale.StartsWith("ru",StringComparison.OrdinalIgnoreCase);
            foreach(var pair in UiText.RussianText)
                check(UiText.Get(pair.Key)==(russian?pair.Value:pair.Key),"release text locale "+locale+" / "+pair.Key);
            foreach(var site in MapSafeSectors.All.SelectMany(s=>LandingSites.For(s.LocationId)))
            foreach(bool chosen in new[]{false,true})
            {
                string text=LandingBriefingText.Description(site,chosen);
                check(Regex.IsMatch(text,"[А-Яа-яЁё]")==russian,"release briefing locale");
                check(text.Contains(site.Label),"release LZ label retained");
            }
            foreach(InsertionAvailability state in Enum.GetValues<InsertionAvailability>())
                check(Regex.IsMatch(DeploymentPolicy.Description(state,DeploymentMode.Helicopter),"[А-Яа-яЁё]")==russian,"release availability locale");
        }
        foreach(string map in new[]{"sandbox","sandbox_high","SANDBOX"," Sandbox_High "})
        {
            check(!MapSafeSectors.TryGet(map,out _),"Ground Zero manual gate");
            check(LandingSites.For(map).Count==0,"Ground Zero no LZs");
            check(DeploymentPolicy.Availability(true,true,false,false,map,true)==InsertionAvailability.HighRise,"Ground Zero UI gate");
        }
        UiText.LanguageSource=()=>throw new Exception("no initialized game settings");
        check(UiText.Get("BACK")=="BACK","release locale exception fallback");
        UiText.LanguageSource=()=>null;
        var counts=new Dictionary<string,int>{{"rezervbase",3},{"bigmap",4},{"woods",4},{"shoreline",4},
            {"lighthouse",3},{"interchange",1},{"tarkovstreets",1}};
        foreach(var map in counts)
        {
            var sites=LandingSites.For(map.Key);
            check(sites.Count==map.Value && sites.All(s=>s.IsMarked),"release marked-point count");
            var firsts=new HashSet<string>();
            for(int seed=0;seed<100;seed++)
            {
                var world=new object();LandingRequest.BeginRaid(world,map.Key,true,seed);
                string first=LandingRequest.Current.Id;firsts.Add(first);
                check(LandingRequest.RandomFallback && sites.Any(s=>s.Id==first),"release no-Infil random point");
                LandingRequest.BeginRaid(world,map.Key,true,seed+1);
                check(LandingRequest.Current.Id==first,"release duplicate raid no reroll");
                check(LandingRequest.RandomSites.Select(s=>s.Id).Distinct().Count()==sites.Count,"release complete random permutation");
            }
            check(firsts.Count==sites.Count,"release all marked points can be chosen");
            LandingRequest.Select(sites.Last());LandingRequest.BeginRaid(new object(),map.Key,true,1);
            check(!LandingRequest.RandomFallback && LandingRequest.Current.Id==sites.Last().Id,"release explicit selection wins");
        }
    }
}
