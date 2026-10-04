using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GUI.Resources;

/// <summary>
/// Shared numeric-input filter for the parameter-editor TextBoxes. Two event
/// handlers (PreviewTextInput + DataObject.Pasting) limit the proposed text to
/// digits, an optional leading minus, and at most one decimal point.
/// </summary>
public partial class ParameterTemplates : ResourceDictionary
{
    /// <summary>Permits digits, an optional leading minus, and a single decimal point.</summary>
    private static readonly Regex NumericInputPattern = new(@"^-?\d*\.?\d*$", RegexOptions.Compiled);

    public ParameterTemplates()
    {
        InitializeComponent();
    }

    private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox tb) { e.Handled = true; return; }
        var proposed = GetProposedText(tb, e.Text);
        e.Handled = !NumericInputPattern.IsMatch(proposed);
    }

    private void NumericTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox tb) { e.CancelCommand(); return; }
        var paste = e.DataObject.GetData(typeof(string)) as string;
        if (paste is null) { e.CancelCommand(); return; }
        if (!NumericInputPattern.IsMatch(GetProposedText(tb, paste))) e.CancelCommand();
    }

    private static string GetProposedText(TextBox tb, string inserted)
    {
        var selectionLength = tb.SelectionLength;
        var before = tb.Text.Remove(tb.SelectionStart, selectionLength);
        var after = tb.Text.Substring(tb.SelectionStart + selectionLength);
        return before + inserted + after;
    }
}