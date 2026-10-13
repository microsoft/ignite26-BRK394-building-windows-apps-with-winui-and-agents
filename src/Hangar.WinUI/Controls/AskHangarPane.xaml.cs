using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hangar.Helpers;
using Hangar.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace Hangar.Controls;

/// <summary>
/// On-device assistant pane. Streams Aion Instruct / Phi Silica answers (or deterministic scripted
/// answers in fallback) and turns citations into action chips.
/// </summary>
public sealed partial class AskHangarPane : UserControl
{
    private CancellationTokenSource? _cts;
    private bool _isOpen;
    private bool _closing;

    public AskHangarPane()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => RefreshMode();
        Loaded += (_, _) =>
        {
            RefreshMode();
            RefreshSuggestions();
        };
        NavigationService.Navigated += (_, _) => DispatcherQueue.TryEnqueue(RefreshSuggestions);
    }

    public event EventHandler<AskChip>? ChipInvoked;

    public event EventHandler<string>? UseInSignOffRequested;

    public bool IsOpen => _isOpen;

    public void Toggle()
    {
        if (_isOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    public void Open()
    {
        RefreshMode();
        RefreshSuggestions();
        if (!_isOpen)
        {
            _isOpen = true;
            _closing = false;
            Visibility = Visibility.Visible;
            var close = (Storyboard)Resources["CloseStoryboard"];
            close.Stop();
            ((Storyboard)Resources["OpenStoryboard"]).Begin();
        }

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => Input.Focus(FocusState.Programmatic));
    }

    public void Close()
    {
        if (!_isOpen)
        {
            return;
        }

        _isOpen = false;
        _closing = true;
        var close = (Storyboard)Resources["CloseStoryboard"];
        close.Completed -= OnCloseCompleted;
        close.Completed += OnCloseCompleted;
        close.Begin();
    }

    private void OnCloseCompleted(object? sender, object e)
    {
        if (_closing && !_isOpen)
        {
            Visibility = Visibility.Collapsed;
        }

        _closing = false;
    }

    public void OpenAndAsk(string question)
    {
        Open();
        _ = AskAsync(question);
    }

    /// <summary>Clears the conversation (demo reset).</summary>
    public void Clear()
    {
        _cts?.Cancel();
        _cts = null;
        Messages.Children.Clear();
        EmptyState.Visibility = Visibility.Visible;
        Input.Text = string.Empty;
        RefreshSuggestions();
    }

    /// <summary>Re-reads the model mode and updates the honest badge.</summary>
    public void RefreshMode()
    {
        var svc = LocalModelService.Instance;
        var live = svc.IsLive;
        ModeLabel.Text = svc.ModeLabel;
        ToolTipService.SetToolTip(ModePill, svc.ModeTooltip);
        AutomationProperties.SetName(ModePill, $"Mode: {svc.ModeLabel}. {svc.ModeTooltip}");

        var fg = ThemeRes.Brush(this, live ? "AccentCyanBrush" : "NeutralStatusBrush");
        ModePill.Background = ThemeRes.Brush(this, live ? "AccentCyanSoftBrush" : "NeutralSoftBrush");
        ModePill.BorderBrush = fg;
        ModeDot.Fill = fg;
        ModeLabel.Foreground = fg;
        FooterText.Text = svc.FooterText;
    }

    private void RefreshSuggestions()
    {
        var page = AskContextBuilder.PageName(NavigationService.CurrentPage);
        Suggestions.Children.Clear();
        foreach (var question in AskContextBuilder.Suggestions(page))
        {
            var button = new Button
            {
                Style = (Style)Resources["AskSuggestionButtonStyle"],
                Content = question,
            };
            AutomationProperties.SetName(button, $"Ask: {question}");
            AutomationProperties.SetAutomationId(button, "AskSuggestion_" + Sanitize(question));
            button.Click += (_, _) => _ = AskAsync(question);
            Suggestions.Children.Add(button);
        }
    }

    private static string Sanitize(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private void OnPanePreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            Send();
        }
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e) =>
        SendButton.IsEnabled = !string.IsNullOrWhiteSpace(Input.Text);

    private void OnSendClick(object sender, RoutedEventArgs e) => Send();

    private void Send()
    {
        var text = Input.Text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        Input.Text = string.Empty;
        _ = AskAsync(text);
    }

    /// <summary>Asks a question and streams the answer into the conversation.</summary>
    public async Task AskAsync(string question)
    {
        question = question.Trim();
        if (question.Length == 0)
        {
            return;
        }

        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        var ct = cts.Token;

        EmptyState.Visibility = Visibility.Collapsed;
        AddUserBubble(question);

        var page = AskContextBuilder.PageName(NavigationService.CurrentPage);
        var intent = AskContextBuilder.DetectIntent(question, page);
        var row = new AnswerRow(this);
        Messages.Children.Add(row.Root);
        ScrollToEnd();

        var svc = LocalModelService.Instance;
        var raw = new StringBuilder();
        var gotToken = false;
        var usedLive = false;
        string? fallbackNote = null;
        IReadOnlyList<AskChip> chips;
        AskAnswer scripted = intent == AskIntent.Logbook
            ? ScriptedResponder.LogbookAnswer()
            : ScriptedResponder.Answer(question, page);

        void OnToken(string token)
        {
            lock (raw)
            {
                raw.Append(token);
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                string snapshot;
                lock (raw)
                {
                    snapshot = raw.ToString();
                }

                if (!gotToken)
                {
                    gotToken = true;
                    row.HideThinking();
                }

                row.SetText(AskChips.Strip(snapshot));
                ScrollToEnd();
            });
        }

        try
        {
            // A question asked while the NPU is still warming up waits briefly (shimmer stays up)
            // instead of silently dropping to scripted answers.
            if (svc.IsInitializing && !svc.ForceScripted && intent != AskIntent.Logbook)
            {
                row.SetThinkingText("Warming up the NPU…");
                await svc.WaitForInitializationAsync(TimeSpan.FromSeconds(25), ct);
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                row.SetThinkingText(svc.IsLive ? "Thinking on NPU…" : "Thinking…");
            }

            // Logbook entries are a regulated record: always a deterministic template, never free model prose.
            if (svc.IsLive && !svc.ForceScripted && intent != AskIntent.Logbook)
            {
                usedLive = true;
                var reason = await svc.StreamAsync(
                    AskContextBuilder.BuildSystemPrompt(page),
                    AskContextBuilder.BuildUserPrompt(question, page),
                    OnToken,
                    ct);
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                if (reason is not null)
                {
                    bool hadAny;
                    lock (raw)
                    {
                        hadAny = raw.Length > 0;
                    }

                    if (!hadAny)
                    {
                        usedLive = false;
                        fallbackNote = reason;
                    }
                }
            }

            if (!usedLive)
            {
                lock (raw)
                {
                    raw.Clear();
                }

                await ScriptedResponder.StreamAsync(AskChips.Strip(scripted.Text), OnToken, ct);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AskHangar] Ask failed: {ex}");
            fallbackNote = ex.Message;
        }

        if (ct.IsCancellationRequested)
        {
            return;
        }

        // Let queued token updates drain before finalizing.
        await Task.Yield();

        string finalRaw;
        lock (raw)
        {
            finalRaw = raw.ToString();
        }

        string display;
        if (usedLive)
        {
            display = AskChips.Strip(finalRaw).Trim();
            chips = AskChips.Derive(question, display, page);
        }
        else
        {
            display = AskChips.Strip(scripted.Text).Trim();
            chips = AskChips.Parse(scripted.Text);
        }

        row.Finish(display);

        if (fallbackNote is not null)
        {
            row.AddMeta("Live model unavailable for this answer — showing demo response.");
            RefreshMode();
        }

        if (intent == AskIntent.Logbook)
        {
            var draft = scripted.DraftText ?? ScriptedResponder.LogbookDraft();
            row.AddDraft(draft, d => UseInSignOffRequested?.Invoke(this, d));
        }

        if (chips.Count > 0)
        {
            row.AddChips(chips, chip => ChipInvoked?.Invoke(this, chip));
        }

        var peer = FrameworkElementAutomationPeer.FromElement(row.Root) ??
                   FrameworkElementAutomationPeer.CreatePeerForElement(row.Root);
        peer?.RaiseNotificationEvent(
            AutomationNotificationKind.ActionCompleted,
            AutomationNotificationProcessing.ImportantMostRecent,
            display,
            "AskHangarAnswer");

        ScrollToEnd();
    }

    private void AddUserBubble(string question)
    {
        var text = new TextBlock
        {
            Style = (Style)Resources["AskUserTextStyle"],
            Text = question,
        };
        var bubble = new Border
        {
            Style = (Style)Resources["AskUserBubbleStyle"],
            Child = text,
        };
        AutomationProperties.SetName(bubble, "You: " + question);
        Messages.Children.Add(bubble);
    }

    private void ScrollToEnd()
    {
        Scroller.UpdateLayout();
        Scroller.ChangeView(null, Scroller.ScrollableHeight, null, true);
    }

    /// <summary>One assistant answer: ✦ glyph, streamed text with caret, then extras.</summary>
    private sealed class AnswerRow
    {
        private readonly AskHangarPane _owner;
        private readonly StackPanel _body;
        private readonly TextBlock _text;
        private readonly Run _content;
        private readonly Run _caret;
        private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _caretTimer;
        private StackPanel? _thinking;
        private TextBlock? _thinkingText;
        private Storyboard? _shimmer;

        public AnswerRow(AskHangarPane owner)
        {
            _owner = owner;
            Root = new Grid { ColumnSpacing = 10 };
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AutomationProperties.SetName(Root, "Ask Hangar answer");

            var spark = new TextBlock { Style = owner.GetStyle("AskSparkStyle"), Text = "✦" };
            AutomationProperties.SetAccessibilityView(spark, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            Root.Children.Add(spark);

            _body = new StackPanel { Spacing = 10 };
            Grid.SetColumn(_body, 1);
            Root.Children.Add(_body);

            _content = new Run();
            _caret = new Run { Text = "▍", Foreground = ThemeRes.Brush(owner, "AccentCyanBrush") };
            _text = new TextBlock { Style = owner.GetStyle("AskAnswerTextStyle"), IsTextSelectionEnabled = true };
            _text.Inlines.Add(_content);
            _text.Inlines.Add(_caret);
            _text.Visibility = Visibility.Collapsed;
            _body.Children.Add(_text);

            BuildThinking();

            _caretTimer = owner.DispatcherQueue.CreateTimer();
            _caretTimer.Interval = TimeSpan.FromMilliseconds(480);
            _caretTimer.Tick += (_, _) => _caret.Text = _caret.Text.Length == 0 ? "▍" : string.Empty;
            _caretTimer.Start();
        }

        public Grid Root { get; }

        public void SetThinkingText(string text)
        {
            if (_thinkingText is not null && _thinking is not null)
            {
                _thinkingText.Text = text;
                AutomationProperties.SetName(_thinking, text.TrimEnd('…'));
            }
        }

        private void BuildThinking()
        {
            _thinking = new StackPanel { Spacing = 8 };
            AutomationProperties.SetName(_thinking, "Thinking on NPU");
            AutomationProperties.SetLiveSetting(_thinking, AutomationLiveSetting.Polite);
            _thinkingText = new TextBlock
            {
                Style = _owner.GetStyle("AskThinkingTextStyle"),
                Text = LocalModelService.Instance.IsLive && !LocalModelService.Instance.ForceScripted
                    ? "Thinking on NPU…"
                    : "Thinking…",
            };
            _thinking.Children.Add(_thinkingText);

            _shimmer = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
            var widths = new[] { 1.0, 0.82, 0.58 };
            for (var i = 0; i < widths.Length; i++)
            {
                var bar = new Rectangle
                {
                    Style = _owner.GetStyle("AskSkeletonStyle"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = 300 * widths[i],
                    Opacity = 0.35,
                };
                _thinking.Children.Add(bar);

                var anim = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(i * 140) };
                anim.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 0.35 });
                anim.KeyFrames.Add(new EasingDoubleKeyFrame
                {
                    KeyTime = TimeSpan.FromMilliseconds(520),
                    Value = 0.9,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
                anim.KeyFrames.Add(new EasingDoubleKeyFrame
                {
                    KeyTime = TimeSpan.FromMilliseconds(1040),
                    Value = 0.35,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
                Storyboard.SetTarget(anim, bar);
                Storyboard.SetTargetProperty(anim, "Opacity");
                _shimmer.Children.Add(anim);
            }

            _body.Children.Insert(0, _thinking);
            _shimmer.Begin();
        }

        public void HideThinking()
        {
            _shimmer?.Stop();
            if (_thinking is not null)
            {
                _body.Children.Remove(_thinking);
                _thinking = null;
            }

            _text.Visibility = Visibility.Visible;
        }

        public void SetText(string text) => _content.Text = text.TrimStart();

        public void Finish(string text)
        {
            HideThinking();
            _caretTimer.Stop();
            _text.Inlines.Remove(_caret);
            _content.Text = text;
            AutomationProperties.SetName(Root, "Ask Hangar answer: " + text);
        }

        public void AddMeta(string text) =>
            _body.Children.Add(new TextBlock { Style = _owner.GetStyle("AskMetaTextStyle"), Text = text });

        public void AddDraft(string draft, Action<string> onUse)
        {
            var draftText = new TextBlock
            {
                Style = _owner.GetStyle("AskDraftTextStyle"),
                Text = draft,
                IsTextSelectionEnabled = true,
            };
            var border = new Border { Style = _owner.GetStyle("AskDraftBorderStyle"), Child = draftText };
            AutomationProperties.SetName(border, "Draft logbook entry: " + draft);
            _body.Children.Add(border);

            var use = new Button
            {
                Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                Content = "Use in sign-off",
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            AutomationProperties.SetAutomationId(use, "AskUseInSignOffButton");
            AutomationProperties.SetName(use, "Use in sign-off. Pre-fills the release entry; the engineer signs.");
            ToolTipService.SetToolTip(use, "Pre-fills the Inspections release entry. It does not sign.");
            use.Click += (_, _) => onUse(draft);

            var note = new TextBlock
            {
                Style = _owner.GetStyle("AskSignNoteTextStyle"),
                Text = "AI drafted · Engineer signs",
                VerticalAlignment = VerticalAlignment.Center,
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row.Children.Add(use);
            row.Children.Add(note);
            _body.Children.Add(row);
        }

        public void AddChips(IReadOnlyList<AskChip> chips, Action<AskChip> onInvoke)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            AutomationProperties.SetName(wrap, "Related actions");
            foreach (var chip in chips)
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                content.Children.Add(new FontIcon { Glyph = chip.Glyph, FontSize = 12 });
                content.Children.Add(new TextBlock { Text = chip.Label });
                var button = new Button
                {
                    Style = _owner.GetStyle("AskChipButtonStyle"),
                    Content = content,
                };
                AutomationProperties.SetName(button, "Open " + chip.Label);
                AutomationProperties.SetAutomationId(button, $"AskChip_{chip.Kind}_{Sanitize(chip.Value)}");
                button.Click += (_, _) => onInvoke(chip);
                wrap.Children.Add(button);
            }

            _body.Children.Add(wrap);
        }
    }

    private Style GetStyle(string key) => (Style)Resources[key];
}
