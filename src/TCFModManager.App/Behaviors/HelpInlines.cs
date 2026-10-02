using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Behaviors;

//
// HelpInlines.Step="{Binding}" on a TextBlock - fills it with one Help step, the control labels it
// names in bold.
//
// Inlines can't be bound, so this builds them. It rebuilds whenever the step raises a change,
// which is how a language switch reaches it: the step object stays the same, only what it reads
// out changes, and a plain binding to the object would never look again.
//
public static class HelpInlines
{
    public static readonly DependencyProperty StepProperty = DependencyProperty.RegisterAttached(
        "Step", typeof(HelpStepViewModel), typeof(HelpInlines),
        new PropertyMetadata(null, OnStepChanged));

    public static HelpStepViewModel? GetStep(DependencyObject d) => (HelpStepViewModel?)d.GetValue(StepProperty);

    public static void SetStep(DependencyObject d, HelpStepViewModel? value) => d.SetValue(StepProperty, value);

    // The handler hooked onto the current step, kept so it can be unhooked when the step changes.
    private static readonly DependencyProperty HandlerProperty = DependencyProperty.RegisterAttached(
        "Handler", typeof(EventHandler<PropertyChangedEventArgs>), typeof(HelpInlines));

    private static void OnStepChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block) return;

        if (e.OldValue is INotifyPropertyChanged old
            && block.GetValue(HandlerProperty) is EventHandler<PropertyChangedEventArgs> previous)
        {
            PropertyChangedEventManager.RemoveHandler(old, previous, string.Empty);
            block.ClearValue(HandlerProperty);
        }

        if (e.NewValue is HelpStepViewModel step)
        {
            EventHandler<PropertyChangedEventArgs> handler = (_, _) => Fill(block, step);
            PropertyChangedEventManager.AddHandler(step, handler, string.Empty);
            block.SetValue(HandlerProperty, handler);
        }

        Fill(block, e.NewValue as HelpStepViewModel);
    }

    private static void Fill(TextBlock block, HelpStepViewModel? step)
    {
        block.Inlines.Clear();
        if (step is null) return;

        foreach (var run in step.Runs)
        {
            block.Inlines.Add(run.IsLabel
                ? new Bold(new Run(run.Text))
                : new Run(run.Text));
        }

        AutomationText(block, step.PlainText);
    }

    private static void AutomationText(TextBlock block, string text) =>
        System.Windows.Automation.AutomationProperties.SetName(block, text);
}
