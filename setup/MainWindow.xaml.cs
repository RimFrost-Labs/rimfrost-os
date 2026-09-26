using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RimFrostSetup
{
    public partial class MainWindow : Window
    {
        const long MinRimFrost = 64 * SystemInfo.GB;
        // Rough size of the staging partitions before we know the exact ISO size
        const long StagingEstimate = 10 * SystemInfo.GB;

        SystemInfo sys;
        bool blocked;
        int page;
        CancellationTokenSource cts;
        bool working;

        class CheckRow
        {
            public string Mark { get; set; }
            public Brush MarkBrush { get; set; }
            public string Title { get; set; }
            public string Detail { get; set; }
        }

        public MainWindow()
        {
            InitializeComponent();
            ShowPage(0);
            Loaded += async (s, e) => await RunChecks();
        }

        async Task RunChecks()
        {
            try
            {
                sys = await Task.Run(() => SystemCheck.Gather());
                var items = SystemCheck.Evaluate(sys, StagingEstimate, MinRimFrost);
                blocked = items.Any(i => i.Verdict == Verdict.Block);
                CheckList.ItemsSource = items.Select(i => new CheckRow
                {
                    Title = i.Title,
                    Detail = i.Detail,
                    Mark = i.Verdict == Verdict.Ok ? "✓" : i.Verdict == Verdict.Warn ? "!" : "✕",
                    MarkBrush = (Brush)FindResource(i.Verdict == Verdict.Ok ? "Ice" : i.Verdict == Verdict.Warn ? "Warn" : "Bad"),
                }).ToList();
                CheckBusy.Visibility = Visibility.Collapsed;
                bool resume = await Task.Run(() => Stager.HasStaging(sys));
                ResumeCard.Visibility = resume ? Visibility.Visible : Visibility.Collapsed;
                NextBtn.IsEnabled = !blocked;
                if (blocked)
                    CheckBusy.Text = "RimFrost OS can't be installed on this PC yet. Fix the items marked ✕ and run Setup again.";
                if (blocked) CheckBusy.Visibility = Visibility.Visible;
                SetupSlider();
            }
            catch (Exception ex)
            {
                Log.Write("checks failed: " + ex);
                CheckBusy.Text = "Setup couldn't look at your drive: " + ex.Message;
            }
        }

        void SetupSlider()
        {
            long max = sys.MaxForRimFrost(StagingEstimate);
            double maxGb = Math.Floor(max / (double)SystemInfo.GB);
            if (maxGb < 64)
            {
                ModeNext.IsEnabled = false;
                ModeReplace.IsChecked = true;
                SizeHint.Text = "Not enough room next to Windows on this drive.";
                return;
            }
            SizeSlider.Maximum = maxGb;
            SizeSlider.Value = Math.Min(maxGb, Math.Max(64, Math.Round(maxGb / 2 / 8) * 8));
            SizeHint.Text = $"Windows keeps {SystemCheck.Gb(sys.CSize - (long)(SizeSlider.Value * SystemInfo.GB) - StagingEstimate)} and all its files.";
        }

        void Size_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SizeText == null || sys == null) return;
            long bytes = (long)(Math.Round(SizeSlider.Value) * SystemInfo.GB);
            SizeText.Text = SystemCheck.Gb(bytes);
            SizeHint.Text = $"Windows keeps {SystemCheck.Gb(sys.CSize - bytes - StagingEstimate)}. Games take space: 150 GB or more is comfortable.";
        }

        void Mode_Changed(object sender, RoutedEventArgs e)
        {
            if (SizeSlider != null) SizeSlider.IsEnabled = ModeNext.IsChecked == true;
        }

        InstallMode Mode => ModeReplace.IsChecked == true ? InstallMode.ReplaceWindows : InstallMode.NextToWindows;
        long RimFrostBytes => (long)(Math.Round(SizeSlider.Value) * SystemInfo.GB);

        void ShowPage(int p)
        {
            page = p;
            var pages = new FrameworkElement[] { PageCheck, PageChoose, PageConfirm, PageWork, PageDone };
            for (int i = 0; i < pages.Length; i++) pages[i].Visibility = i == p ? Visibility.Visible : Visibility.Collapsed;
            StepText.Text = p <= 2 ? $"Step {p + 1} of 3" : "";
            BackBtn.Visibility = p == 1 || p == 2 ? Visibility.Visible : Visibility.Collapsed;
            NextBtn.Content = p == 2 ? "Install" : "Next";
            if (p == 2) { BuildPlan(); NextBtn.IsEnabled = BackedUp.IsChecked == true; }
            if (p == 1) NextBtn.IsEnabled = true;
        }

        void BuildPlan()
        {
            PlanList.Children.Clear();
            var steps = new List<string>();
            if (Mode == InstallMode.NextToWindows)
                steps.Add($"Windows makes {SystemCheck.Gb(RimFrostBytes)} of room for RimFrost OS, plus about 9 GB for the installer. Your files stay where they are.");
            else
                steps.Add("Windows makes about 9 GB of room for the installer. Windows itself is erased later, during the install.");
            if (sys.BitLockerProtection == 1)
                steps.Add("BitLocker is paused for the next few restarts.");
            steps.Add("RimFrost OS is downloaded (about 8 GB) and checked.");
            steps.Add("Your PC restarts into the installer. " + (Mode == InstallMode.NextToWindows
                ? "Choose \"Use free space\" there, so Windows is kept."
                : "Choose to erase the drive there."));
            steps.Add("When it's done, your PC restarts into RimFrost OS and Setup's leftovers are removed.");
            int n = 1;
            foreach (var s in steps)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                var num = new TextBlock { Text = n++.ToString("00"), Foreground = (Brush)FindResource("Ice"), FontFamily = new FontFamily("Consolas") };
                var txt = new TextBlock { Text = s };
                Grid.SetColumn(txt, 1);
                row.Children.Add(num); row.Children.Add(txt);
                PlanList.Children.Add(row);
            }
        }

        void BackedUp_Changed(object sender, RoutedEventArgs e)
        {
            if (page == 2) NextBtn.IsEnabled = BackedUp.IsChecked == true;
        }

        void Back_Click(object sender, RoutedEventArgs e) => ShowPage(page - 1);

        async void Next_Click(object sender, RoutedEventArgs e)
        {
            if (page < 2) { ShowPage(page + 1); return; }
            if (page == 2) { await Install(); return; }
            if (page == 4) Restart();
        }

        async Task Install()
        {
            ShowPage(3);
            NextBtn.Visibility = Visibility.Collapsed;
            working = true;
            cts = new CancellationTokenSource();
            var stager = new Stager(sys, Mode, RimFrostBytes, (text, frac) => Dispatcher.Invoke(() =>
            {
                WorkText.Text = text;
                WorkBar.IsIndeterminate = frac < 0;
                if (frac >= 0) WorkBar.Value = frac;
            }));
            try
            {
                await Task.Run(() => stager.Run(cts.Token));
                working = false;
                ShowDone(true, null);
            }
            catch (OperationCanceledException)
            {
                working = false;
                await UndoWithMessage("Setup stopped. Your drive is back the way it was.");
            }
            catch (Exception ex)
            {
                working = false;
                Log.Write("install failed: " + ex);
                await UndoWithMessage("Setup stopped: " + ex.Message + "\n\nYour drive is back the way it was. The log is at " + Log.FilePath + ".");
            }
        }

        async Task UndoWithMessage(string message)
        {
            WorkText.Text = "Putting your drive back the way it was…";
            WorkBar.IsIndeterminate = true;
            try { await Task.Run(() => Stager.Undo(sys)); }
            catch (Exception ex) { Log.Write("undo failed: " + ex); message += "\n\nCleaning up didn't fully work; see the log."; }
            ShowDone(false, message);
        }

        void ShowDone(bool ok, string message)
        {
            ShowPage(4);
            CancelBtn.Content = ok ? "Restart later" : "Close";
            NextBtn.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
            NextBtn.IsEnabled = true;
            NextBtn.Content = "Restart now";
            if (ok)
            {
                DoneTitle.Text = "Ready to install";
                DoneText.Text = "Save your work. When your PC restarts, the RimFrost OS installer opens by itself." +
                                (Mode == InstallMode.NextToWindows ? " Choose \"Use free space\" so Windows is kept." : "");
                DoneNoteText.Text = "If your PC starts Windows instead, restart and open the boot menu (usually F12, F11, F8 or Esc) and pick \"RimFrost OS Setup\"." +
                                    (sys.SecureBoot == true ? " If it says the installer isn't allowed to start, allow third-party (Microsoft UEFI CA) certificates under Secure Boot in the firmware settings." : "");
            }
            else
            {
                DoneTitle.Text = "Setup stopped";
                DoneText.Text = message;
                DoneNote.Visibility = Visibility.Collapsed;
            }
        }

        void Restart()
        {
            Shell.Run(Shell.SystemTool("shutdown.exe"), "/r /t 5 /c \"Restarting into the RimFrost OS installer\"", check: false);
            Close();
        }

        async void Remove_Click(object sender, RoutedEventArgs e)
        {
            RemoveBtn.IsEnabled = false;
            NextBtn.IsEnabled = false;
            CheckBusy.Visibility = Visibility.Visible;
            CheckBusy.Text = "Removing the installer partitions…";
            await Task.Run(() => Stager.Undo(sys));
            ResumeCard.Visibility = Visibility.Collapsed;
            await RunChecks();
        }

        void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (working)
            {
                if (MessageBox.Show("Stop Setup and put your drive back the way it was?", "RimFrost Setup",
                                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    cts?.Cancel();
                return;
            }
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (working) { e.Cancel = true; Cancel_Click(null, null); }
            base.OnClosing(e);
        }
    }
}
