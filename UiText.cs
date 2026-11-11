using System;
using System.Collections.Generic;

namespace UH60FastRopeInsertion
{
    // Stable English config/log identifiers; only displayed text is translated. No OS locale.
    internal static class UiText
    {
        internal static Func<string> LanguageSource = () => null;
        internal static bool Russian
        {
            get
            {
                try { return IsRussian(LanguageSource?.Invoke()); } catch { return false; }
            }
        }
        internal static bool IsRussian(string language)
        {
            if (string.IsNullOrWhiteSpace(language)) return false;
            string code = language.Trim().Replace('_', '-');
            return code.Equals("ru", StringComparison.OrdinalIgnoreCase)
                || code.StartsWith("ru-", StringComparison.OrdinalIgnoreCase);
        }
        internal static string Get(string english)
        {
            return Russian && english != null && RussianText.TryGetValue(english, out string text) ? text : english;
        }
        internal static readonly Dictionary<string, string> RussianText = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DEPLOYMENT METHOD"] = "СПОСОБ ВЫСАДКИ",
            ["HELICOPTER INSERTION"] = "ВЫСАДКА С ВЕРТОЛЁТА",
            ["STANDARD SPAWN"] = "ОБЫЧНОЕ ПОЯВЛЕНИЕ",
            ["UH-60 LANDING ZONE"] = "ВЫБОР ЗОНЫ ВЫСАДКИ UH-60",
            ["BACK"] = "НАЗАД",
            ["CONFIRM"] = "ПОДТВЕРДИТЬ",
            ["Select the squad's arrival point."] = "Отметьте площадку прибытия отряда.",
            ["Helicopter insertion is currently unavailable."] = "Вертолётная высадка сейчас недоступна.",
            ["Scavs enter on foot."] = "Для Дикого — наземный вход.",
            ["Continuing from the neighbouring area."] = "Продолжение пути из соседнего района.",
            ["Select an arrival area."] = "Выберите район прибытия.",
            ["Ground Zero: helicopter insertion is unavailable among high-rise buildings."] = "Эпицентр: вертолётная высадка недоступна из-за плотной высотной застройки.",
            ["Only ground entry is available in this area."] = "В этом районе доступен наземный вход.",
            ["UH-60. Squad fast-rope insertion."] = "UH-60. Высадка отряда по тросам.",
            ["Start the raid from a ground position."] = "Начало рейда с наземной позиции.",
            ["Primary landing zone"] = "Основная площадка",
            ["Eastern base perimeter"] = "Восточный периметр базы",
            ["Eastern industrial outskirts"] = "Восточная окраина промзоны",
            ["Western base perimeter"] = "Западный периметр базы",
            ["Western forest edge"] = "Западная опушка",
            ["Corner barracks"] = "Угловые казармы",
            ["Western industrial outskirts"] = "Западная окраина промзоны",
            ["South-eastern forest edge"] = "Юго-восточная опушка",
            ["Western coast"] = "Западное побережье",
            ["Southern coast"] = "Южное побережье",
            ["South-eastern perimeter"] = "Юго-восточный периметр",
            ["Northern district"] = "Северный квартал",
            ["North-eastern outskirts"] = "Северо-восточная окраина",
            ["Aft landing pad"] = "Кормовая площадка",
            ["Barracks clearing"] = "Площадка у казарм",
            ["Perimeter clearing"] = "Площадка у периметра",
            ["Outer clearing"] = "Площадка на окраине",
            ["Warehouse yard"] = "Двор у складов",
            ["Roadside landing area"] = "Площадка у дороги",
            ["Village outskirts"] = "Окраина деревни",
            ["Forest clearing"] = "Лесная поляна",
            ["Lakeside approach"] = "Подход к озеру",
            ["Farm outskirts"] = "Окраина фермы",
            ["Coastal road"] = "Прибрежная дорога",
            ["Waterfront landing area"] = "Площадка у воды",
            ["Industrial perimeter"] = "Периметр промзоны",
            ["Coastal clearing"] = "Прибрежная площадка",
            ["Mall perimeter road"] = "Дорога у периметра ТЦ",
            ["Courtyard landing area"] = "Дворовая площадка",
            ["Insert near the outer base perimeter, beside the corner barracks."] = "Высадка у внешнего периметра базы, рядом с угловыми казармами.",
            ["UH-60 squad insertion into the selected area."] = "Прибытие отряда в выбранный район на UH-60.",
            ["Fast-rope descent after hovering. Continue on foot."] = "После зависания — спуск по тросам. Дальнейшее продвижение по земле.",
            ["The crew will head here unless another landing zone is selected."] = "Экипаж направится сюда, если не выбрать другую площадку.",
            ["01. General"] = "01. Основные",
            ["02. Timing"] = "02. Время",
            ["03. Camera"] = "03. Камера",
            ["04. Presentation"] = "04. Сцена",
            ["Helicopter volume"] = "Громкость вертолёта",
            ["Rope descent volume"] = "Громкость спуска по тросу",
            ["Volume of this helicopter only, 0-100 percent. Applies immediately; departure distance fading is retained."] = "Громкость только нашего вертолёта, 0–100%. Применяется сразу; затухание при отлёте сохраняется.",
            ["Volume of the quiet normalized rope sound, 0-100 percent. Follows each actual descent and stops at ground contact."] = "Громкость приглушённого звука троса, 0–100%. Сопровождает каждый спуск и заканчивается при касании земли.",
            ["05. Test camera"] = "05. Тестовая камера",
            ["05. Free camera"] = "05. Свободная камера",
            ["06. Cabin editor"] = "06. Редактор салона",
            ["01. Optimization"] = "01. Оптимизация",
            ["02. Diagnostics"] = "02. Диагностика",
            ["Enabled"] = "Включено",
            ["Automatic raid-start insertion"] = "Автоматическая высадка в начале рейда",
            ["Deployment mode"] = "Способ высадки",
            ["Debug overlay"] = "Окно диагностики",
            ["Verbose logging"] = "Подробный журнал",
            ["Quick scene test hotkey"] = "Клавиши повтора сцены",
            ["PIT squad wait seconds"] = "Ожидание отряда PIT, с",
            ["Flight speed multiplier"] = "Множитель скорости полёта",
            ["Fast-rope descent seconds"] = "Длительность спуска, с",
            ["Cabin look yaw"] = "Поворот головы в стороны",
            ["Cabin look up"] = "Взгляд вверх",
            ["Cabin look down"] = "Взгляд вниз",
            ["Cabin look sensitivity"] = "Чувствительность мыши",
            ["Cabin FOV bonus"] = "Прибавка к углу обзора",
            ["Aircraft model"] = "Модель вертолёта",
            ["Toggle inspection camera"] = "Переключение свободной камеры",
            ["Inspection move speed"] = "Скорость свободной камеры",
            ["Animate user poses"] = "Оживлять пользовательские позы",
            ["Use edited flight animation"] = "Использовать экспортированную анимацию",
            ["Animation file"] = "Файл анимации",
            ["Pose capture file"] = "Файл захвата поз",
            ["Reload animation"] = "Перезагрузить анимацию",
            ["Capture current poses"] = "Сохранить текущие позы",
            ["Reuse exact cable frames"] = "Повторно использовать сечения троса",
            ["Skip duplicate sky callbacks"] = "Пропускать одинаковые обновления неба",
            ["Log performance samples"] = "Записывать замеры производительности",
            ["Hide aircraft for comparison"] = "Скрыть вертолёт для сравнения",
            ["Enable the standalone UH-60 insertion scene."] = "Включить самостоятельную сцену высадки UH-60.",
            ["Master switch for automatic insertion. The native pre-raid selector chooses Helicopter or Standard; the manual test hotkey is independent."] = "Главный выключатель автоматической высадки. Перед рейдом выбирается вертолёт или обычное появление. Клавиши повтора работают независимо.",
            ["Remembered pre-raid choice: Helicopter = UH-60 insertion, Standard = ordinary spawn. On the location screen. With audited Infil2.1.0 and DynamicMaps1.2.1, Next opens the landing-zone map."] = "Сохранённый выбор: Helicopter — вертолёт, Standard — обычное появление. С Infil 2.1.0 и Dynamic Maps 1.2.1 кнопка «Далее» открывает карту высадки.",
            ["Show the current scene state, map, landing point and actor lanes."] = "Показывать диагностику этапа сцены, карты, точки и тросов.",
            ["Write additional camera, streaming and rope diagnostics."] = "Записывать дополнительные сведения о камере, загрузке мира и тросах.",
            ["Restart the presentation at the authored point in the current supported raid. Saves are not changed."] = "Повторить сцену в текущем поддерживаемом рейде. Сохранения не меняются.",
            ["Wait for PIT friends to finish spawning before capturing the roster."] = "Ожидать появления напарников PIT перед определением состава отряда.",
            ["Scales only the authored approach/departure time."] = "Меняет только скорость подлёта и отлёта.",
            ["Time for one actor to move from the door to touchdown."] = "Время спуска одного бойца от двери до земли.",
            ["Maximum free-look angle to either side."] = "Максимальный угол свободного поворота головы в каждую сторону.",
            ["Maximum upward look angle."] = "Максимальный угол взгляда вверх.",
            ["Maximum downward look angle."] = "Максимальный угол взгляда вниз.",
            ["Mouse sensitivity while the cinematic camera is active."] = "Чувствительность мыши во время сцены.",
            ["Extra vertical view angle only during cinematic camera ownership. Original raid FOV is restored at handoff."] = "Дополнительный вертикальный угол обзора только во время сцены. После высадки прежний FOV восстанавливается.",
            ["Icebreaker reads the installed airframe, native cabin clips, cable and carabiner locally; flight still uses Tactical Services. No silent fallback if unavailable."] = "Icebreaker использует установленную модель, анимации, трос и карабин. Полёт — Tactical Services. При отсутствии ресурсов скрытой замены нет.",
            ["Toggle aircraft-relative inspection: mouse look, WASD/QE move, Shift faster. F8 returns to the actor's eyes."] = "Свободная камера относительно вертолёта: мышь — обзор, WASD/QE — движение, Shift — быстрее. F8 — вернуться к бойцу.",
            ["Inspection camera speed in metres per second."] = "Скорость свободной камеры в метрах в секунду.",
            ["Add authored flight reactions, team gestures, coil push/kick and continuous exits to the edited cabin poses. Requires Use edited flight animation. Frozen per scene; repeat after changing."] = "Добавить реакции, жесты, сброс бухты и переходы к пользовательским позам. Требуется экспортированная анимация. После изменения повторите сцену.",
            ["Opt-in: play cabin-flight.csv only in the cabin; accepted rope descent is not edited. Disabled preserves test11 poses."] = "Проигрывать cabin-flight.csv только в салоне, не меняя спуск. При отключении используются прежние позы.",
            ["Local data-only Blender export. Reload explicitly after exporting. No network or script execution."] = "Локальные данные экспорта Blender. После экспорта перезагрузите анимацию. Без сетевых запросов и выполнения скриптов.",
            ["Capture the currently displayed cabin poses for importing into Blender; no profile or save changes."] = "Сохранить текущие позы для импорта в Blender, не меняя профиль и сохранения.",
            ["Reload a validated cabin clip without restarting EFT. Invalid data retains the previous clip."] = "Перезагрузить проверенную анимацию без перезапуска игры. При ошибке остаётся предыдущая.",
            ["During flight/hover before deployment: capture actual displayed poses to the local CSV."] = "Во время полёта или зависания до спуска сохранить отображаемые позы в локальный CSV.",
            ["Reuse identical cross-section calculations. Original mesh, every vertex, physics and update rate are preserved."] = "Повторно использовать одинаковые расчёты сечений, сохраняя исходную сетку, физику и частоту обновления.",
            ["Only during UH60 eye-camera presentation: skip same-frame callbacks with identical camera pose and FOV. Camera/culling/exposure updates remain intact."] = "Во время вида от первого лица пропускать одинаковые обновления неба в одном кадре. Обновления камеры, видимости и экспозиции сохраняются.",
            ["Log 5-second CPU/engine/GPU samples, including 10 seconds after aircraft cleanup. No in-raid overlay. GPU timing may be unavailable in this game build."] = "Записывать замеры CPU/GPU каждые 5 с и 10 с после удаления вертолёта. Без окна в рейде. GPU-замеры могут быть недоступны.",
            ["DIAGNOSTIC ONLY: hide owned aircraft renderers without removing it or changing its flight. Compare 5-second log windows; switch back off afterwards. Actors, ropes and map stay visible."] = "ТОЛЬКО ДИАГНОСТИКА: скрыть модель вертолёта, не меняя полёт. Сравните замеры за 5 с, затем выключите. Бойцы, тросы и карта остаются видимыми.",
        };
    }
}
