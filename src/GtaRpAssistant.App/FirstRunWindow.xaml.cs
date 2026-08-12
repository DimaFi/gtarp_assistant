using System.Windows;

namespace GtaRpAssistant.App;

public partial class FirstRunWindow : Window
{
    private readonly (string Icon, string Title, string Description, string Hint)[] _steps =
    [
        ("▣", "Режим работы", "Настроим основные функции по очереди. Все параметры можно изменить позже.", "Займёт около двух минут."),
        ("◉", "Микрофон", "На странице «Аудио» выберите микрофон и нажмите «Проверить». Разрешение Windows запрашивается только при использовании.", "Можно пропустить, если нужен только текстовый поиск."),
        ("AI", "Локальный ИИ", "Самый приватный вариант. На странице «AI и модели» нажмите «Установить и настроить», затем выберите рекомендованную модель.", "Приложение само найдёт LM Studio, запустит API и проверит модель."),
        ("☁", "Облачный ИИ", "Альтернатива локальному: включите облачный маршрут, вставьте HTTPS endpoint, модель и API-ключ, затем сохраните.", "Ключ хранится локально через Windows DPAPI. Отправка данных требует явного разрешения."),
        ("♫", "Голос", "Выберите системный голос и режим кнопки. Голосовые функции необязательны.", "По умолчанию ассистент не слушает постоянно."),
        ("◇", "Приватность", "Проверьте, какие данные разрешено отправлять в облако. Скриншот всегда показывается перед отправкой.", "Локальная база знаний работает без облака."),
        ("V", "GTA 5 RP", "Выберите сервер и оставьте автоматическое обнаружение GTA включённым.", "Ассистент не внедряется в игру и не автоматизирует действия."),
        ("✓", "Готово", "Откроем экран AI с одной кнопкой автоматической настройки. Если ИИ пока не нужен, выберите режим только базы знаний.", "Все шаги доступны в меню приложения.")
    ];
    private int _index;
    public bool KnowledgeOnly { get; private set; }
    public FirstRunWindow() { InitializeComponent(); Render(); }
    private void Render() { var s = _steps[_index]; ProgressText.Text = $"Шаг {_index + 1} из {_steps.Length}"; StepIcon.Text=s.Icon; StepTitle.Text=s.Title; StepDescription.Text=s.Description; StepHint.Text=s.Hint; BackButton.IsEnabled=_index>0; NextButton.Content=_index==_steps.Length-1?"Открыть настройку AI":"Далее"; }
    private void NextClick(object sender, RoutedEventArgs e) { if (_index < _steps.Length-1) { _index++; Render(); } else { DialogResult=true; Close(); } }
    private void BackClick(object sender, RoutedEventArgs e) { if (_index>0) { _index--; Render(); } }
    private void SkipClick(object sender, RoutedEventArgs e) { KnowledgeOnly=true; DialogResult=true; Close(); }
}
