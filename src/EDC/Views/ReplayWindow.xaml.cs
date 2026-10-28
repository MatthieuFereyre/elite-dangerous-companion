using System.IO;
using EDC.Core;
using EDC.Resources;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace EDC.Views;

/// <summary>Shows the switches a journal would have caused, without a sound.</summary>
public partial class ReplayWindow : FluentWindow
{
    public ReplayWindow(string journal, TimeSpan calmDelay)
    {
        InitializeComponent();
        Caption.Text = string.Format(Strings.ReplayCaption, Path.GetFileName(journal), calmDelay.TotalSeconds);
        Output.Text = string.Join(Environment.NewLine, JournalReplay.Run(File.ReadLines(journal), calmDelay));
    }

    /// <summary>Asks for a journal, newest first in the game's folder; null if none is picked.</summary>
    public static string? PickJournal()
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.ReplayPick,
            Filter = Strings.ReplayFilter,
        };
        if (Directory.Exists(JournalTail.DefaultDirectory))
            dialog.InitialDirectory = JournalTail.DefaultDirectory;

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
