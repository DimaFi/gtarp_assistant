using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GtaRpAssistant.Knowledge;

public sealed class CommunityReferenceLoader
{
    private static readonly DateTimeOffset UpdatedAt = new(2026, 7, 13, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ValidUntil = new(2026, 8, 13, 0, 0, 0, TimeSpan.Zero);

    public async Task<IReadOnlyList<KnowledgePackArticle>> LoadAsync(string directory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory)) return [];
        var articles = new List<KnowledgePackArticle>();
        await LoadAchievementsAsync(Path.Combine(directory, "achievements.json"), articles, cancellationToken);
        await LoadBpAsync(Path.Combine(directory, "bp-farming.json"), articles, cancellationToken);
        await LoadMedicalAsync(Path.Combine(directory, "medical-aid.json"), articles, cancellationToken);
        await LoadCookingAsync(Path.Combine(directory, "cooking-recipes.csv"), articles, cancellationToken);
        await LoadEconomyAsync(Path.Combine(directory, "economy-tables.json"), articles, cancellationToken);
        await LoadGaragesAsync(Path.Combine(directory, "garage-upgrades.json"), articles, cancellationToken);
        await LoadSkillsAsync(Path.Combine(directory, "skill-progression.json"), articles, cancellationToken);
        await LoadPlayerGuidesAsync(Path.Combine(directory, "player-guides.json"), articles, cancellationToken);
        await LoadPlayerGuidesAsync(Path.Combine(directory, "club-guides.json"), articles, cancellationToken);
        return articles;
    }

    private static async Task LoadAchievementsAsync(string path, List<KnowledgePackArticle> output, CancellationToken ct)
    {
        using var json = await ReadJsonAsync(path, ct); if (json is null) return;
        var index = 0;
        foreach (var item in json.RootElement.GetProperty("achievements").EnumerateArray())
        {
            var name = Text(item, "name"); var condition = Text(item, "condition"); var reward = Text(item, "reward");
            var availability = Optional(item, "availability"); var note = Optional(item, "note");
            var fact = $"По данным игроков: достижение «{name}» — {condition}. Награда: {reward}.";
            if (availability == "unavailable") fact += " Сейчас недоступно для получения.";
            else if (availability == "legacy-event") fact += " Относилось к завершённому событию.";
            if (!string.IsNullOrWhiteSpace(note)) fact += $" {note}";
            output.Add(Article($"community.achievement.{index++:D3}", name, "achievement", [name, $"как получить {name}", $"награда за {name}"], fact,
                [new($"как получить {name}", fact), new($"награда за {name}", fact)]));
        }
    }

    private static async Task LoadBpAsync(string path, List<KnowledgePackArticle> output, CancellationToken ct)
    {
        using var json = await ReadJsonAsync(path, ct); if (json is null) return;
        var index = 0;
        foreach (var group in new[] { "repeatable", "faction", "added2025-10-20" })
        foreach (var item in json.RootElement.GetProperty(group).EnumerateArray())
        {
            var action = Text(item, "action"); var reward = Text(item, "reward"); var note = Optional(item, "note");
            var fact = $"По данным игроков: {action} — {reward} в формате без VIP / Gold или Platinum VIP." + (note is null ? "" : $" {note}");
            output.Add(Article($"community.bp.{index++:D3}", $"BP: {action}", "bp-farming", [action, $"bp {action}", "фарм bp"], fact));
        }
        foreach (var item in json.RootElement.GetProperty("unavailable").EnumerateArray())
        {
            var action = item.GetString()!; var fact = $"По данным игроков: способ BP «{action}» сейчас недоступен.";
            output.Add(Article($"community.bp.{index++:D3}", $"Недоступный BP: {action}", "bp-farming", [action, "недоступный фарм bp"], fact));
        }
    }

    private static async Task LoadMedicalAsync(string path, List<KnowledgePackArticle> output, CancellationToken ct)
    {
        using var json = await ReadJsonAsync(path, ct); if (json is null) return;
        var index = 0;
        foreach (var item in json.RootElement.GetProperty("treatments").EnumerateArray())
        {
            var condition = Text(item, "condition"); var treatment = Text(item, "treatment");
            var fact = $"По данным игроков: при состоянии «{condition}» используйте: {treatment}. Это игровая шпаргалка, не медицинский совет.";
            output.Add(Article($"community.medical.{index++:D3}", condition, "medical-aid", [condition, $"что делать {condition}", $"лечение {condition}"], fact,
                [new($"что делать при {condition}", fact)]));
        }
    }

    private static async Task LoadCookingAsync(string path, List<KnowledgePackArticle> output, CancellationToken ct)
    {
        if (!File.Exists(path)) return;
        var lines = await File.ReadAllLinesAsync(path, ct);
        for (var i = 1; i < lines.Length; i++)
        {
            var row = ParseCsv(lines[i]); if (row.Count < 7 || string.IsNullOrWhiteSpace(row[0])) continue;
            var dish = row[0]; var details = new List<string> { $"ингредиенты: {row[1]}" };
            if (!string.IsNullOrWhiteSpace(row[2])) details.Add($"инструменты: {row[2]}");
            if (!string.IsNullOrWhiteSpace(row[3])) details.Add($"сытость {row[3]}");
            if (!string.IsNullOrWhiteSpace(row[4])) details.Add($"настроение {row[4]}");
            if (!string.IsNullOrWhiteSpace(row[5])) details.Add($"сила {row[5]}");
            if (!string.IsNullOrWhiteSpace(row[6])) details.Add($"навык {row[6]}");
            var fact = $"По данным игроков: {dish} — {string.Join("; ", details)}.";
            output.Add(Article($"community.recipe.{i:D3}", $"Рецепт: {dish}", "cooking", [dish, $"рецепт {dish}", $"как приготовить {dish}"], fact,
                [new($"рецепт {dish}", fact)]));
        }
    }

    private static async Task LoadEconomyAsync(string path, List<KnowledgePackArticle> output, CancellationToken ct)
    {
        using var json = await ReadJsonAsync(path, ct); if (json is null) return;
        var root = json.RootElement; var index = 0;
        foreach (var x in root.GetProperty("farmIncomeLevel5").EnumerateArray())
        {
            var name = Text(x, "activity");
            Add($"Доход фермы: {name}", "farm-income", [name, $"доход {name}"], $"{name}: за действие {x.GetProperty("perAction")} $, с Rednecks 5 — {x.GetProperty("perActionRednecks5")} $; ориентир за час {x.GetProperty("perHour")} $, с Rednecks 5 — {x.GetProperty("perHourRednecks5")} $.");
        }
        foreach (var x in root.GetProperty("vehicleWear").EnumerateArray())
        {
            var name = Text(x, "component"); var genitive = ComponentGenitive(name);
            Add($"Износ: {name}", "vehicle-wear", [name, $"износ {name}", $"износ {genitive}", $"ремонт {name}", $"ремонт {genitive}"], $"{name}: 100% износа примерно через {x.GetProperty("kmTo100Percent")} км; максимальная стоимость ремонта — {Text(x, "maxRepair")}.");
        }
        var classes = root.GetProperty("lscZeroMarkup").GetProperty("classes").EnumerateArray().Select(x => x.GetString()).ToArray();
        foreach (var x in root.GetProperty("lscZeroMarkup").GetProperty("items").EnumerateArray())
        {
            var name = Text(x, "name"); var prices = x.GetProperty("prices").EnumerateArray().Select((p, j) => $"{classes[j]} {p.GetInt32()}$");
            Add($"LSC: {name}", "lsc-prices", [name, $"цена {name}", "тюнинг lsc"], $"{name} при наценке LSC 0%: {string.Join(", ", prices)}.");
        }
        foreach (var x in root.GetProperty("alcohol").EnumerateArray())
        {
            var club = Text(x, "club"); var drink = Text(x, "drink");
            Add($"{club}: {drink}", "alcohol", [club, drink, $"{drink} {club}"], $"{club}, {drink}: {x.GetProperty("price")} $, длительность {x.GetProperty("duration")}, настроение {x.GetProperty("moodPercent")}%." );
        }
        foreach (var x in root.GetProperty("servicePriceExamples").EnumerateArray())
        {
            var business = Text(x, "business"); var item = Text(x, "item"); Add($"{business}: {item}", "service-prices", [business, item], $"{business}, {item}: {x.GetProperty("price")} $.");
        }
        foreach (var x in root.GetProperty("commercialDropsPercent").EnumerateArray())
        {
            var item = Text(x, "item"); var activity = Text(x, "activity");
            var values = x.EnumerateObject().Where(p => p.Name is not ("item" or "activity")).Select(p => $"{p.Name} {p.Value}%");
            Add($"Шанс {item}: {activity}", "drop-chance", [item, activity, $"шанс {item}"], $"Шанс получить {item} за «{activity}»: {string.Join(", ", values)}.");
        }
        void Add(string title, string mechanic, string[] aliases, string body) => output.Add(Article($"community.economy.{index++:D3}", title, mechanic, aliases, $"По данным игроков: {body}"));
    }

    private static async Task LoadGaragesAsync(string path, List<KnowledgePackArticle> output, CancellationToken ct)
    {
        using var json = await ReadJsonAsync(path, ct); if (json is null) return; var index = 0;
        foreach (var x in json.RootElement.GetProperty("houseAndApartmentUpgradeCosts").EnumerateArray())
        {
            var type = Text(x, "type"); var slots = x.GetProperty("baseSlots").GetInt32();
            var steps = x.GetProperty("upgrades").EnumerateObject().Select(p => $"до {p.Name} ГМ — {p.Value.GetInt32()}$");
            var fact = $"По данным игроков: {type}, базово {slots} ГМ: {string.Join(", ", steps)}; полная стоимость улучшений {x.GetProperty("fullCost").GetInt32()}$.";
            output.Add(Article($"community.garage.{index++:D3}", $"{type}: гараж {slots} ГМ", "garage-upgrades", [$"{type} {slots} гм", "улучшение гаража"], fact));
        }
        foreach (var x in json.RootElement.GetProperty("apartmentClassUpgrades").EnumerateArray())
        {
            var name = Text(x, "class"); var slots = x.GetProperty("baseSlots").GetInt32();
            var steps = x.GetProperty("steps").EnumerateArray().Select(s => $"{s.GetProperty("slots")} ГМ за {s.GetProperty("cost")} $, налог {s.GetProperty("tax")} $/ч");
            var fact = $"По данным игроков: квартира {name}, {slots} ГМ, базовый налог {x.GetProperty("baseTaxPerHour")} $/ч; {string.Join(", ", steps)}.";
            output.Add(Article($"community.garage.{index++:D3}", $"Гараж квартиры {name} {slots} ГМ", "garage-upgrades", [$"гараж {name}", $"квартира {name} гм"], fact));
        }
    }

    private static async Task LoadSkillsAsync(string path, List<KnowledgePackArticle> output, CancellationToken ct)
    {
        using var json = await ReadJsonAsync(path, ct); if (json is null) return; var index = 0;
        foreach (var x in json.RootElement.GetProperty("skills").EnumerateArray())
        {
            var name = Text(x, "name"); var actions = string.Join(" → ", x.GetProperty("actionsPerLevel").EnumerateArray().Select(v => v.GetInt32()));
            var time = x.TryGetProperty("timeToLevel5", out var level5) ? level5.GetString() : x.GetProperty("timeToMaximum").GetString();
            var fact = $"По данным игроков: навык {name}; действия по уровням {actions}; ориентир до максимума {time} ч без/с Platinum VIP. {Text(x, "benefits")}";
            output.Add(Article($"community.skill.{index++:D3}", $"Прокачка: {name}", "skill-progression", [name, $"прокачка {name}", $"сколько качать {name}"], fact));
        }
    }

    private static async Task LoadPlayerGuidesAsync(string path, List<KnowledgePackArticle> output, CancellationToken ct)
    {
        using var json = await ReadJsonAsync(path, ct); if (json is null) return;
        var root = json.RootElement;
        var updatedAt = DateTimeOffset.Parse(Text(root, "capturedAt"), CultureInfo.InvariantCulture);
        var validUntil = DateTimeOffset.Parse(Text(root, "reviewBy"), CultureInfo.InvariantCulture);
        foreach (var item in root.GetProperty("entries").EnumerateArray())
        {
            var id = Text(item, "id");
            var body = Text(item, "body");
            var fact = $"По данным игроков: {body}";
            var aliases = item.GetProperty("aliases").EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray();
            var answers = item.TryGetProperty("questions", out var questions)
                ? questions.EnumerateArray().Select(x => new PreparedAnswer(x.GetString() ?? "", fact)).Where(x => x.QuestionPattern.Length > 0).ToArray()
                : [];
            output.Add(Article($"community.guide.{id}", Text(item, "title"), Text(item, "mechanic"), aliases, fact, answers, updatedAt, validUntil));
        }
    }

    private static KnowledgePackArticle Article(string id, string title, string mechanic, string[] aliases, string fact, IReadOnlyList<PreparedAnswer>? answers = null,
        DateTimeOffset? updatedAt = null, DateTimeOffset? validUntil = null) =>
        new(id, "gta5rp", ["all"], title, "community", mechanic, aliases.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            "Community-confirmed player reference. Ответ должен сохранять пометку «По данным игроков».", [new($"{id}.fact.1", fact, true)], answers ?? [],
            new("Community-confirmed player data", null), 1, updatedAt ?? UpdatedAt, true, false, validUntil ?? ValidUntil, "Product owner confirmation");

    private static async Task<JsonDocument?> ReadJsonAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path); return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }
    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? "";
    private static string? Optional(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.GetString() : null;
    private static string ComponentGenitive(string value) => value switch
    {
        "Двигатель" => "двигателя",
        "Тормоза" => "тормозов",
        "Покрышки" => "покрышек",
        "Топливная система" => "топливной системы",
        "Выхлоп" => "выхлопа",
        "Кузов" => "кузова",
        "Аккумулятор" => "аккумулятора",
        "Подвеска" => "подвески",
        "Зажигание" => "зажигания",
        "Трансмиссия" => "трансмиссии",
        _ => value
    };
    private static List<string> ParseCsv(string line)
    {
        var result = new List<string>(); var value = new StringBuilder(); var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; } else quoted = !quoted; }
            else if (c == ',' && !quoted) { result.Add(value.ToString()); value.Clear(); }
            else value.Append(c);
        }
        result.Add(value.ToString()); return result;
    }
}
