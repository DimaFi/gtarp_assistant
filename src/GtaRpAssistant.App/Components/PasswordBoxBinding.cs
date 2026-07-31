using System.Windows;
using System.Windows.Controls;

namespace GtaRpAssistant.App.Components;

public static class PasswordBoxBinding
{
    private static readonly DependencyProperty UpdatingProperty = DependencyProperty.RegisterAttached(
        "Updating", typeof(bool), typeof(PasswordBoxBinding), new PropertyMetadata(false));

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(PasswordBoxBinding), new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty PasswordProperty = DependencyProperty.RegisterAttached(
        "Password", typeof(string), typeof(PasswordBoxBinding), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetPassword(DependencyObject element, string value) => element.SetValue(PasswordProperty, value);
    public static string GetPassword(DependencyObject element) => (string)element.GetValue(PasswordProperty);

    private static void OnIsEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not PasswordBox box) return;
        if ((bool)e.OldValue) box.PasswordChanged -= BoxOnPasswordChanged;
        if ((bool)e.NewValue) box.PasswordChanged += BoxOnPasswordChanged;
    }

    private static void OnPasswordChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not PasswordBox box || (bool)box.GetValue(UpdatingProperty)) return;
        box.SetValue(UpdatingProperty, true);
        box.Password = e.NewValue as string ?? string.Empty;
        box.SetValue(UpdatingProperty, false);
    }

    private static void BoxOnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box) return;
        box.SetValue(UpdatingProperty, true);
        SetPassword(box, box.Password);
        box.SetValue(UpdatingProperty, false);
    }
}
