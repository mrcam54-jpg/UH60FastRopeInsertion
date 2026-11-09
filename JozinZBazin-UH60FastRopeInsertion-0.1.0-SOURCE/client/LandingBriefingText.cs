namespace UH60FastRopeInsertion
{
    internal static class LandingBriefingText
    {
        internal const string Help="Отметьте площадку прибытия отряда.";
        internal const string Confirm="ПОДТВЕРДИТЬ";
        internal static string Name(LandingSite site)
        {
            if(site==null)return "Основная площадка";
            if(site.Id.EndsWith(".east"))return site.Sector.LocationId=="rezervbase"?"Восточный периметр базы":"Восточная окраина промзоны";
            if(site.Id.EndsWith(".west"))return site.Sector.LocationId=="rezervbase"?"Западный периметр базы":"Западная опушка";
            switch(site.Sector.LocationId)
            {
                case "rezervbase":return "Угловые казармы";
                case "bigmap":return "Западная окраина промзоны";
                case "woods":return "Юго-восточная опушка";
                case "shoreline":return "Западное побережье";
                case "lighthouse":return "Южное побережье";
                case "interchange":return "Юго-восточный периметр";
                case "sandbox":case "sandbox_high":return "Северный квартал";
                case "tarkovstreets":return "Северо-восточная окраина";
                case "icebreaker":return "Кормовая площадка";
                default:return site.Name;
            }
        }

        internal static string Description(LandingSite site,bool explicitlySelected)
        {
            string heading=(site?.Label??"LZ-A")+": "+Name(site);
            string brief=site?.Sector.LocationId=="rezervbase" && site?.Label=="LZ-A"
                ?"Высадка у внешнего периметра базы, рядом с угловыми казармами."
                :"Прибытие отряда в выбранный район на UH-60.";
            return "<b>"+heading+"</b>\n\n"+brief+"\n\n"
                +"После зависания — спуск по тросам. Дальнейшее продвижение по земле."
                +(explicitlySelected?"":"\n\nЭкипаж направится сюда, если не выбрать другую площадку.");
        }
    }
}
