using System.Xml.Linq;
using System.Text.RegularExpressions;

internal static class AccessibilityPolicyTests
{
    internal static readonly HashSet<string> Interactive = new(StringComparer.Ordinal)
    {
        "Button", "RepeatButton", "ToggleButton", "RadioButton", "CheckBox", "TextBox", "MaskedTextBox",
        "AutoCompleteBox", "ComboBox", "ComboBoxItem", "ListBox", "ListBoxItem", "DataGrid", "Slider",
        "NumericUpDown", "DatePicker", "TimePicker", "CalendarDatePicker", "Calendar", "MenuItem",
        "TabControl", "TabItem", "TreeView", "TreeViewItem", "SplitButton", "ToggleSplitButton", "HyperlinkButton",
        "ToggleSwitch", "GridSplitter", "ScrollBar", "Menu", "ContextMenu"
    };
    private static readonly HashSet<string> TextContent = new(StringComparer.Ordinal)
        { "Button", "RepeatButton", "ToggleButton", "RadioButton", "CheckBox", "MenuItem", "TabItem", "TreeViewItem", "ListBoxItem", "ComboBoxItem", "HyperlinkButton" };
    private static readonly Regex ChangingText = new("Status|Error|Message|Countdown|Progress", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // No source exceptions currently needed. Do not exempt application controls or entire files.
    // Any future third-party template exception must identify one file/element/rule and explain why.
    public static void Run()
    {
        var root = FindRoot();
        var files = Directory.GetFiles(Path.Combine(root, "BRU.Avtopark.TicketSalesAPP.Avalonia.Unity"), "*.axaml", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj")).Order().ToArray();
        var violations = files.SelectMany(p => Inspect(XDocument.Load(p, LoadOptions.SetLineInfo))
            .Select(v => Path.GetRelativePath(root, p) + ":" + v)).ToArray();
        if (violations.Length != 0) throw new Exception("Accessibility policy violations (" + violations.Length + "):\n" + string.Join('\n', violations));
        AssertJobsPager(root, "Employee");
        var invalid = XDocument.Parse("""
            <StackPanel><Button/><TextBox/><Image/><TextBlock Name="ErrorText"/><TextBlock Text="{Binding SynchronizationMessage}"/>
            <Button Content="OK"/><Button Content="Отмена"/><Button Content="Named" IsTabStop="False"/></StackPanel>
            """);
        if (Inspect(invalid).Count() != 8) throw new Exception("Accessibility policy negative fixtures failed.");
        var valid = XDocument.Parse("""
            <StackPanel><Button Content="OK" IsDefault="True"/><Button Content="Отмена" IsCancel="True"/>
            <TextBox AutomationProperties.Name="Поиск"/><Image AutomationProperties.AccessibilityView="Raw" IsTabStop="False"/>
            <TextBlock Text="{Binding ErrorMessage}" AutomationProperties.LiveSetting="Assertive"/>
            <Button AutomationProperties.Name="{Binding ActionName}"/><TextBox AutomationProperties.LabeledBy="{Binding #Label}"/>
            <TabItem Header="Обзор"/></StackPanel>
            """);
        if (Inspect(valid).Any()) throw new Exception("Accessibility policy positive fixtures failed.");
        Console.WriteLine($"PASS accessibility static policy: {files.Length} AXAML files, zero violations, no allowlist; positive/negative rule fixtures");
    }

    internal static IEnumerable<string> Inspect(XDocument document)
    {
        foreach (var element in document.Descendants())
        {
            var tag = element.Name.LocalName;
            var line = ((System.Xml.IXmlLineInfo)element).LineNumber;
            var id = Attribute(element, "Name") ?? Attribute(element, "Content") ?? Attribute(element, "Header") ?? tag;
            string Violation(string rule) => $"{line} {tag} [{id}]: {rule}";
            var named = Present(element, "AutomationProperties.Name") || Present(element, "AutomationProperties.LabeledBy");
            if (Interactive.Contains(tag) && !named && !(TextContent.Contains(tag) &&
                (StaticText(Attribute(element, "Content")) || StaticText(Attribute(element, "Header")) ||
                 element.Elements().Any(e => e.Name.LocalName is "ContentControl.Content" or "Button.Content" or "MenuItem.Header" or "TabItem.Header" && !e.HasElements && StaticText(e.Value)) ||
                 (!element.HasElements && StaticText(element.Value)))))
                yield return Violation("accessible name required");
            if (tag == "Image" && !named && Attribute(element, "AutomationProperties.AccessibilityView") != "Raw")
                yield return Violation("image must be named or decorative (Raw)");
            if (tag == "TextBlock" && ChangingText.IsMatch((Attribute(element, "Name") ?? "") + " " + (Attribute(element, "Text") is { } text && text.StartsWith('{') ? text : "")) &&
                Attribute(element, "AutomationProperties.LiveSetting") is not ("Polite" or "Assertive"))
                yield return Violation("changing status/error text requires LiveSetting");
            if (tag == "Button")
            {
                var content = Attribute(element, "Content")?.Trim();
                if (content is "OK" or "ОК" or "Ок" && Attribute(element, "IsDefault") != "True") yield return Violation("OK requires IsDefault");
                if (content is "Cancel" or "Отмена" && Attribute(element, "IsCancel") != "True") yield return Violation("Cancel requires IsCancel");
            }
            if (Attribute(element, "IsTabStop")?.Equals("False", StringComparison.OrdinalIgnoreCase) == true &&
                (Interactive.Contains(tag) || tag is not ("Image" or "PathIcon" or "MaterialIcon" or "TextBlock" or "Border" or "Rectangle" or "Ellipse" or "Path")))
                yield return Violation("IsTabStop=False is reserved for decoration");
            if (tag == "Setter" && Attribute(element, "Property") == "IsTabStop" && Attribute(element, "Value")?.Equals("False", StringComparison.OrdinalIgnoreCase) == true)
                yield return Violation("blanket IsTabStop=False style requires review; mark decoration locally");
        }
    }
    internal static void AssertJobsPager(string root, string screen)
    {
        var directory = Path.Combine(root, "BRU.Avtopark.TicketSalesAPP.Avalonia.Unity", "Views", "ManagementToolWindowsViews");
        XElement Pager(string name) => XDocument.Load(Path.Combine(directory, name + "ManagementToolWindow.axaml")).Descendants()
            .Single(e => e.Name.LocalName == "StackPanel" && e.Descendants().Any(c => Attribute(c, "Command") == "{Binding PreviousPageCommand}"));
        var expected = Pager("Job"); var actual = Pager(screen);
        expected.Attribute("Grid.Row")!.Remove(); actual.Attribute("Grid.Row")!.Remove();
        if (!XNode.DeepEquals(expected, actual)) throw new Exception(screen + " pager differs from accessible Jobs controls/styles/names.");
        if (Inspect(new XDocument(actual)).Any()) throw new Exception(screen + " pager violates accessibility policy.");
    }
    private static bool StaticText(string? value) => !string.IsNullOrWhiteSpace(value) && !value.TrimStart().StartsWith('{');
    private static bool Present(XElement e, string name) => !string.IsNullOrWhiteSpace(Attribute(e, name)) || e.Elements().Any(c => c.Name.LocalName == name && (c.HasElements || !string.IsNullOrWhiteSpace(c.Value)));
    private static string? Attribute(XElement e, string name) => e.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;
    internal static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "BRU.Avtopark.TicketSalesAPP.Avalonia.Unity", "Views"))) return dir.FullName;
        throw new DirectoryNotFoundException("Cannot locate AXAML source root.");
    }
}
