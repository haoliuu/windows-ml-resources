using AIDevGallery.Sample.Utils;
using Microsoft.Extensions.AI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AIDevGallery.Sample;

internal sealed partial class Sample : Microsoft.UI.Xaml.Controls.Page
{
    private CancellationTokenSource? cts;
    public ObservableCollection<Message> Messages { get; } = [];

    private bool isImeActive = true;

    private IChatClient? model;

    // Markers for the assistant's think area (displayed in a dedicated UI region).
    private static readonly string[] ThinkTagOpens = new[] { "<think>", "<thought>", "<reasoning>" };
    private static readonly string[] ThinkTagCloses = new[] { "</think>", "</thought>", "</reasoning>" };
    private static readonly int MaxOpenThinkMarkerLength = ThinkTagOpens.Max(s => s.Length);

    public Sample()
    {
        this.Unloaded += (s, e) => CleanUp();
        this.InitializeComponent();
    }

    private ScrollViewer? scrollViewer;

    // Model alias and variant ID from the Foundry Local catalog.
    // Run `foundry model list` in a terminal to see available models on your system.
    // The SDK handles model downloading and caching automatically.
    private static readonly string ModelAlias = "qwen2.5-coder-7b";
    private static readonly string VariantId = "qwen2.5-coder-7b-instruct-generic-cpu:4";

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        try
        {
            model = await FoundryLocalChatClientFactory.CreateAsync(ModelAlias, VariantId);
        }
        catch (Exception ex)
        {
            App.Window?.ShowException(ex);
        }

        App.Window?.ModelLoaded();
    }

    private void CleanUp()
    {
        CancelResponse();
        model?.Dispose();
    }

    private void CancelResponse()
    {
        StopBtn.Visibility = Visibility.Collapsed;
        SendBtn.Visibility = Visibility.Visible;
        EnableInputBoxWithPlaceholder();
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }

    private void TextBox_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter &&
            !Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) &&
            sender is TextBox &&
            !string.IsNullOrWhiteSpace(InputBox.Text) &&
            isImeActive == false)
        {
            var cursorPosition = InputBox.SelectionStart;
            var text = InputBox.Text;
            if (cursorPosition > 0 && (text[cursorPosition - 1] == '\n' || text[cursorPosition - 1] == '\r'))
            {
                text = text.Remove(cursorPosition - 1, 1);
                InputBox.Text = text;
            }

            InputBox.SelectionStart = cursorPosition - 1;

            SendMessage();
        }
        else
        {
            isImeActive = true;
        }
    }

    private void TextBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        isImeActive = false;
    }

    private void SendMessage()
    {
        if (InputBox.Text.Length > 0)
        {
            AddMessage(InputBox.Text);
            InputBox.Text = string.Empty;
            SendBtn.Visibility = Visibility.Collapsed;
        }
    }

    private void AddMessage(string text)
    {
        if (model == null)
        {
            return;
        }

        Messages.Add(new Message(text.Trim(), DateTime.Now, ChatRole.User));
        UpdateRewriteButtonState();
        UpdateClearButtonState();

        Task.Run(async () =>
        {
            var history = Messages.Select(m => new ChatMessage(m.Role, m.Content)).ToList();

            var responseMessage = new Message(string.Empty, DateTime.Now, ChatRole.Assistant)
            {
                IsPending = true
            };

            DispatcherQueue.TryEnqueue(() =>
            {
                Messages.Add(responseMessage);
                UpdateClearButtonState();
                StopBtn.Visibility = Visibility.Visible;
                InputBox.IsEnabled = false;
                InputBox.PlaceholderText = "Please wait for the response to complete before entering a new prompt";
            });

            cts = new CancellationTokenSource();

            history.Insert(0, new ChatMessage(ChatRole.System, "You are a helpful assistant"));

            int currentThinkTagIndex = -1; // -1 means not inside any think/auxiliary section
            string rolling = string.Empty;

            await foreach (var messagePart in model.GetStreamingResponseAsync(history, null, cts.Token))
            {
                var part = messagePart;

                DispatcherQueue.TryEnqueue(() =>
                {
                    if (responseMessage.IsPending)
                    {
                        responseMessage.IsPending = false;
                    }

                    // Parse character by character/fragment to identify think tags (e.g., <think>...</think>, <thought>...</thought>)
                    rolling += part;

                    while (!string.IsNullOrEmpty(rolling))
                    {
                        if (currentThinkTagIndex == -1)
                        {
                            // Find the earliest occurring open marker among supported think tags
                            int earliestIdx = -1;
                            int foundTagIndex = -1;
                            for (int i = 0; i < ThinkTagOpens.Length; i++)
                            {
                                int idx = rolling.IndexOf(ThinkTagOpens[i], StringComparison.Ordinal);
                                if (idx >= 0 && (earliestIdx == -1 || idx < earliestIdx))
                                {
                                    earliestIdx = idx;
                                    foundTagIndex = i;
                                }
                            }

                            if (earliestIdx >= 0)
                            {
                                // Output safe content before the start marker
                                if (earliestIdx > 0)
                                {
                                    responseMessage.Content = string.Concat(responseMessage.Content, rolling.AsSpan(0, earliestIdx));
                                }

                                // Enter think mode, discard the marker text itself
                                rolling = rolling.Substring(earliestIdx + ThinkTagOpens[foundTagIndex].Length);
                                currentThinkTagIndex = foundTagIndex;
                                continue;
                            }
                            else
                            {
                                // Start marker not found: only flush safe parts, keep the tail that might form a marker
                                int keep = MaxOpenThinkMarkerLength - 1;
                                if (rolling.Length > keep)
                                {
                                    int flushLen = rolling.Length - keep;
                                    responseMessage.Content = string.Concat(responseMessage.Content.TrimStart(), rolling.AsSpan(0, flushLen));
                                    rolling = rolling.Substring(flushLen);
                                }

                                break;
                            }
                        }
                        else
                        {
                            string closeMarker = ThinkTagCloses[currentThinkTagIndex];
                            int closeIdx = rolling.IndexOf(closeMarker, StringComparison.Ordinal);
                            if (closeIdx >= 0)
                            {
                                // Append content before the closing marker to the think box
                                if (closeIdx > 0)
                                {
                                    responseMessage.ThinkContent = string.Concat(responseMessage.ThinkContent, rolling.AsSpan(0, closeIdx));
                                }

                                // Exit think mode, discard the closing marker
                                rolling = rolling.Substring(closeIdx + closeMarker.Length);
                                currentThinkTagIndex = -1;
                                continue;
                            }
                            else
                            {
                                // Closing marker not found: only flush safe parts, keep the tail that might form a marker
                                int keep = closeMarker.Length - 1;
                                if (rolling.Length > keep)
                                {
                                    int flushLen = rolling.Length - keep;
                                    responseMessage.ThinkContent = string.Concat(responseMessage.ThinkContent, rolling.AsSpan(0, flushLen));
                                    rolling = rolling.Substring(flushLen);
                                }

                                break;
                            }
                        }
                    }

                });
            }

            // Flush remaining tail content (if any)
            DispatcherQueue.TryEnqueue(() =>
            {
                responseMessage.IsPending = false;
                if (!string.IsNullOrEmpty(rolling))
                {
                    if (currentThinkTagIndex != -1)
                    {
                        responseMessage.ThinkContent += rolling;
                    }
                    else
                    {
                        responseMessage.Content = responseMessage.Content.TrimStart() + rolling;
                    }
                }
            });

            cts?.Dispose();
            cts = null;

            DispatcherQueue.TryEnqueue(() =>
            {
                StopBtn.Visibility = Visibility.Collapsed;
                SendBtn.Visibility = Visibility.Visible;
                EnableInputBoxWithPlaceholder();
            });
        });
    }

    private void SendBtn_Click(object sender, RoutedEventArgs e)
    {
        SendMessage();
    }

    private void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        CancelResponse();
    }

    private void ClearBtn_Click(object sender, RoutedEventArgs e)
    {
        // Cancel any ongoing response generation before clearing chat
        CancelResponse();
        ClearChat();
    }

    private void RewriteBtn_Click(object sender, RoutedEventArgs e)
    {
        RewriteLastMessage();
    }

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SendBtn.IsEnabled = !string.IsNullOrWhiteSpace(InputBox.Text);
    }

    private void EnableInputBoxWithPlaceholder()
    {
        InputBox.IsEnabled = true;
        InputBox.PlaceholderText = "Enter your prompt (Press Shift + Enter to insert a newline)";
    }

    private void ClearChat()
    {
        Messages.Clear();
        UpdateRewriteButtonState();
        UpdateClearButtonState();
    }

    private void RewriteLastMessage()
    {
        var lastUserMessage = Messages.LastOrDefault(m => m.Role == ChatRole.User);
        if (lastUserMessage != null)
        {
            InputBox.Text = lastUserMessage.Content;
            InputBox.Focus(FocusState.Programmatic);

            InputBox.SelectionStart = InputBox.Text.Length;
            InputBox.SelectionLength = 0;
        }
    }

    private void UpdateRewriteButtonState()
    {
        foreach (var message in Messages.Where(m => m.Role == ChatRole.User))
        {
            message.IsLastUserMessage = false;
        }

        var lastUserMessage = Messages.LastOrDefault(m => m.Role == ChatRole.User);
        lastUserMessage?.IsLastUserMessage = true;
    }

    private void UpdateClearButtonState()
    {
        ClearBtn.IsEnabled = Messages.Count > 0;
    }

    private void InvertedListView_Loaded(object sender, RoutedEventArgs e)
    {
        scrollViewer = FindElement<ScrollViewer>(InvertedListView);

        ItemsStackPanel? itemsStackPanel = FindElement<ItemsStackPanel>(InvertedListView);
        if (itemsStackPanel != null)
        {
            itemsStackPanel.SizeChanged += ItemsStackPanel_SizeChanged;
        }
    }

    private void ItemsStackPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (scrollViewer != null)
        {
            bool isScrollbarVisible = scrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible;

            if (isScrollbarVisible)
            {
                InvertedListView.Padding = new Thickness(-12, 0, 12, 24);
            }
            else
            {
                InvertedListView.Padding = new Thickness(-12, 0, -12, 24);
            }
        }
    }

    private T? FindElement<T>(DependencyObject element)
        where T : DependencyObject
    {
        if (element is T targetElement)
        {
            return targetElement;
        }

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            var result = FindElement<T>(child);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }
}